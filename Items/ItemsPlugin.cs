using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppEntities;
using MelonLoader;
using POGContentLib.Core;
using Unity.Netcode;
using UnityEngine;

namespace POGContentLib.Items
{
    /// <summary>Use context. owner runs host-authoritative.</summary>
    public sealed class UseContext
    {
        public bool IsHost { get; internal set; }
        public HostBridge Bridge { get; internal set; }
    }

    /// <summary>Use handler. Return false if the effect did not apply (the use is not consumed).</summary>
    public delegate bool ModItemUseHandler(InventoryItem item, Entity owner, UseContext ctx);

    /// <summary>
    /// Items domain plugin. Registers definitions/handlers, builds templates on scene load, and
    /// runs the host-side use flow. Network prefab registration and loot injection are done by
    /// the core (NetworkRegistrar / LootInjector) at the correct lifecycle moments.
    /// </summary>
    public sealed class ItemsPlugin : IPOGPlugin
    {
        public string Name => "Items";
        public string Version => "0.1.0";

        internal static ItemsPlugin Instance { get; private set; }

        private readonly Dictionary<string, ModItemUseHandler> _handlers = new();
        private readonly List<ModItemDefinition> _defs = new();
        private readonly HashSet<string> _built = new();

        /// <summary>Resolve a definition by hash (used by ModItemHandle).</summary>
        internal static ModItemDefinition ResolveDefinition(uint hash)
            => CoreServices.Content.GetDefinitionByHash(hash) as ModItemDefinition;

        // ---- Public API (via Content.Items) ----

        public void RegisterUseHandler(string handlerId, ModItemUseHandler handler)
        {
            if (string.IsNullOrEmpty(handlerId) || handler == null) return;
            _handlers[handlerId] = handler;
            MelonLogger.Msg($"[POGContentLib.Items] Use handler: {handlerId}");
        }

        public void RegisterItem(ModItemDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.ModId) || string.IsNullOrEmpty(def.ContentId))
            {
                MelonLogger.Error("[POGContentLib.Items] Invalid ModItemDefinition.");
                return;
            }
            if (!CoreServices.Content.RegisterDefinition(def)) return;
            _defs.Add(def);
            // If a scene is already loaded (late registration), try to build immediately.
            TryBuild(def);
        }

        // ---- Lifecycle ----

        public void Build(PluginGraph graph)
        {
            Instance = this;
            Content.Items.Plugin = this;

            // On every scene load: build pending templates; host-side, inject loot into loaded tables.
            graph.AddSystem(PluginStage.SceneLoaded, Name, () =>
            {
                foreach (var def in _defs) TryBuild(def);

                // Retry any queued visual probes (prefabs load progressively across scenes).
                Content.Diagnostics.RetryPending();

                var nm = NetworkManager.Singleton;
                if (nm != null && nm.IsServer)
                    CoreServices.Loot.InjectIntoLoadedTables();
            });
        }

        private void TryBuild(ModItemDefinition def)
        {
            string full = ContentRegistry.FullId(def.ModId, def.ContentId);
            if (_built.Contains(full)) return;

            var template = CoreServices.Shells.BuildItemTemplate(def.ShellKind, def.ModId, def.ContentId);
            if (template == null) return; // shell not in memory yet — retry on the next scene load

            ApplyIdentity(template, def);
            ApplyVisual(template, def);
            ApplyCapabilities(template, def);
            template.gameObject.AddComponent<ModItemHandle>();

            // Request a natural drop (executed host-side by LootInjector).
            if (def.Loot != null)
                CoreServices.Loot.AddRequest(template, def.Loot.Category, def.Loot.TableNames, def.Loot.Weight);

            _built.Add(full);
        }

        private static void ApplyIdentity(InventoryItem item, ModItemDefinition def)
        {
            item.m_itemID = ContentRegistry.FullId(def.ModId, def.ContentId);

            // Classification — without these the item mis-routes to the wrong slot and has no
            // minimap marker (ITEM_SYSTEM.md §2). Fields are set directly (interop exposes them).
            item.m_itemType = def.ItemType;
            item.m_mapIconType = def.MapIcon;

            // Economy / satiety (§4). GoldValue = loot budget, ShopCost = price (decoupled).
            item.m_goldValue = def.GoldValue;
            item.m_foodValue = def.FoodValue;
            item.m_shopCost = def.ShopCost;

            item.m_doConsume = def.Consumable;
            item.m_isConsumed = false;
            item.m_spawnAmount = 0;
            item.m_spawnChance = 0f;

            // Handling / "weight" (ITEM_ANATOMY.md §1.4) — plain fields, statically sound.
            item.m_isBigItem = def.Big;
            item.m_staminaPenaltyOnPickup = def.PickupStaminaPenalty;
            item.m_movementFactorWhenHeld = def.MovementFactorWhenHeld;

            // Icon: direct override wins, else resolve a game sprite by name.
            if (def.Icon != null) item.m_itemIcon = def.Icon;
            else if (!string.IsNullOrEmpty(def.IconSpriteName))
            {
                var sprite = GameAssets.FindSprite(def.IconSpriteName);
                if (sprite != null) item.m_itemIcon = sprite;
            }

            // Tooltip/name. Registering the translation is only half of it — the item must also POINT
            // at our term, otherwise it keeps the shell's ids and shows "Diamond / Best rock in world".
            // The ids live on NetworkInteractableBase (InventoryItem's base): m_itemTooltipID and
            // m_itemTooltipDescriptionID, which UIItemTooltip reads via GetTooltipTitleId/DescriptionId.
            string nameKey = !string.IsNullOrEmpty(def.TooltipKey)
                ? def.TooltipKey
                : $"MOD/{def.ModId}/{def.ContentId}";
            string descKey = !string.IsNullOrEmpty(def.TooltipDescriptionKey)
                ? def.TooltipDescriptionKey
                : $"MOD/{def.ModId}/{def.ContentId}_Desc";

            if (!string.IsNullOrEmpty(def.DisplayName))
            {
                ModLocalization.Register(nameKey, def.DisplayName);
                item.m_itemTooltipID = nameKey;
            }
            if (!string.IsNullOrEmpty(def.DisplayDescription))
            {
                ModLocalization.Register(descKey, def.DisplayDescription);
                item.m_itemTooltipDescriptionID = descKey;
            }
        }

        /// <summary>
        /// Attach declared capability components (EXPERIMENTAL, v0.2 — see ItemCapability). Each attach
        /// is guarded so one bad capability can't abort the whole item build. Runs on every peer at the
        /// same build point, so the resulting component set stays identical (ForceSamePrefabs).
        /// </summary>
        private static void ApplyCapabilities(InventoryItem item, ModItemDefinition def)
        {
            if (def.Capabilities == null || def.Capabilities.Count == 0) return;
            foreach (var cap in def.Capabilities)
            {
                if (cap == null) continue;
                try
                {
                    cap.Attach(item);
                    MelonLogger.Msg($"[POGContentLib.Items] Capability '{cap.Name}' attached to {def.ContentId} (EXPERIMENTAL).");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[POGContentLib.Items] Capability '{cap.Name}' failed on {def.ContentId}: {ex.Message}");
                }
            }
        }

        private static void ApplyVisual(InventoryItem item, ModItemDefinition def)
        {
            var go = item.gameObject;
            try
            {
                if (def.Visual != null)
                {
                    switch (def.Visual.Kind)
                    {
                        case ItemVisualKind.GameMesh:
                            var source = GameAssets.FindItemPrefab(def.Visual.SourcePrefabName);
                            if (source != null)
                            {
                                // Swap the mesh on the shell's own renderers so the world view, the
                                // in-hand view and inventory hiding all keep working (see ReplaceMeshes).
                                ItemVisuals.ReplaceMeshes(go, source.gameObject, def.Visual.ChildName);
                                if (def.Visual.StripSpeakingStone)
                                {
                                    ItemVisuals.StripVoiceComponents(go);
                                    ItemVisuals.HideSpeakingStoneVfx(go);
                                }
                            }
                            else MelonLogger.Warning($"[POGContentLib.Items] Visual source not found: {def.Visual.SourcePrefabName}");
                            break;

                        case ItemVisualKind.Png when def.Icon == null:
                            var icon = AssetLoader.LoadPngAsSprite(def.Visual.Path);
                            if (icon != null) item.m_itemIcon = icon;
                            break;

                        case ItemVisualKind.BundlePrefab:
                            ApplyBundleVisual(item, def);
                            break;
                    }
                }

                if (def.Tint.HasValue)
                    ItemVisuals.ApplyTint(go, def.Tint.Value, def.Visual?.ChildName);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[POGContentLib.Items] Visual failed for {def.ContentId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Load a custom mesh/prefab from an AssetBundle and use it as the item's visual. The prefab
        /// brings its own engine-side effects (particles, lights, trails, animation); custom C#
        /// scripts cannot survive IL2CPP and are dropped by Unity on load. Bundles must be built with
        /// the game's Unity/HDRP version — mismatched shaders are repaired onto HDRP/Lit.
        /// RUNTIME-TODO (Milestone 0.2): the bundle round-trip is not proven in-game yet.
        /// </summary>
        private static void ApplyBundleVisual(InventoryItem item, ModItemDefinition def)
        {
            var v = def.Visual;
            if (string.IsNullOrEmpty(v.Path) || string.IsNullOrEmpty(v.AssetName))
            {
                MelonLogger.Warning($"[POGContentLib.Items] Bundle visual for {def.ContentId} needs both a bundle path and an asset name.");
                return;
            }

            var bundle = CoreServices.Assets.LoadBundle(v.Path);
            if (bundle == null) return;

            var prefab = CoreServices.Assets.LoadAsset<GameObject>(bundle, v.AssetName);
            if (prefab == null)
            {
                MelonLogger.Error($"[POGContentLib.Items] Asset '{v.AssetName}' not found in bundle '{v.Path}'.");
                return;
            }

            var visual = ItemVisuals.AttachBundlePrefab(item.gameObject, prefab, v.LocalOffset, v.LocalEuler, v.LocalScale);
            if (visual == null) return;

            if (v.RepairShaders) ItemVisuals.RepairBundleShaders(visual);
            MelonLogger.Msg($"[POGContentLib.Items] Custom mesh '{v.AssetName}' applied to {def.ContentId} (EXPERIMENTAL).");
        }

        // ---- Use flow (host-authoritative) ----

        /// <summary>Try to use an item. Called from the Interact Harmony patch.</summary>
        public void TryUseItem(InventoryItem item, Entity owner)
        {
            if (item == null || owner == null) return;

            var netObj = item.GetComponent<NetworkObject>();
            if (netObj == null) return;
            var def = ResolveDefinition(netObj.GlobalObjectIdHash);
            if (def == null) return;

            // From here on the item IS ours, so every early exit is a reason the player's click did
            // nothing — say which one. A silent no-op here once hid a broken use flow completely.
            var handle = item.GetComponent<ModItemHandle>();
            if (handle != null && handle.IsConsumed)
            {
                MelonLogger.Msg($"[POGContentLib.Items] {def.ContentId}: already consumed.");
                return;
            }

            if (string.IsNullOrEmpty(def.UseHandlerId))
            {
                MelonLogger.Msg($"[POGContentLib.Items] {def.ContentId}: no UseHandlerId set — nothing to run.");
                return;
            }
            if (!_handlers.TryGetValue(def.UseHandlerId, out var handler))
            {
                MelonLogger.Warning($"[POGContentLib.Items] {def.ContentId}: use handler " +
                                    $"'{def.UseHandlerId}' is not registered.");
                return;
            }

            var nm = NetworkManager.Singleton;
            bool isHost = nm != null && nm.IsServer;

            // Host-authoritative: a client would route the request via HostBridge (v2). For now, host-only.
            if (!isHost)
            {
                MelonLogger.Msg("[POGContentLib.Items] Use on client deferred (HostBridge RPC is a v2 feature).");
                return;
            }

            var ctx = new UseContext { IsHost = true, Bridge = CoreServices.Bridge };
            if (!handler(item, owner, ctx))
            {
                // The handler declined (e.g. "the bucket is moving") — the use is NOT spent.
                MelonLogger.Msg($"[POGContentLib.Items] {def.ContentId}: handler declined; use not spent.");
                return;
            }

            if (handle != null)
            {
                handle.UsesRemaining--;
                if (handle.UsesRemaining <= 0 && def.Consumable)
                {
                    handle.IsConsumed = true;
                    try { item.ConsumeItem_Owner(); }
                    catch (Exception ex)
                    { MelonLogger.Warning($"[POGContentLib.Items] Consume failed: {ex.Message}"); }
                }
            }
        }

        public bool IsModItem(InventoryItem item)
        {
            if (item == null) return false;
            var netObj = item.GetComponent<NetworkObject>();
            return netObj != null && CoreServices.Content.GetDefinitionByHash(netObj.GlobalObjectIdHash) != null;
        }
    }
}
