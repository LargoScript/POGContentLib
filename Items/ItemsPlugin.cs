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

            // Icon: direct override wins, else resolve a game sprite by name.
            if (def.Icon != null) item.m_itemIcon = def.Icon;
            else if (!string.IsNullOrEmpty(def.IconSpriteName))
            {
                var sprite = GameAssets.FindSprite(def.IconSpriteName);
                if (sprite != null) item.m_itemIcon = sprite;
            }

            // Tooltip/name fallback via the localization patch.
            if (!string.IsNullOrEmpty(def.TooltipKey) && !string.IsNullOrEmpty(def.DisplayName))
                ModLocalization.Register(def.TooltipKey, def.DisplayName);
            if (!string.IsNullOrEmpty(def.TooltipDescriptionKey) && !string.IsNullOrEmpty(def.DisplayDescription))
                ModLocalization.Register(def.TooltipDescriptionKey, def.DisplayDescription);
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
                                ItemVisuals.AttachGameMesh(go, source.gameObject, def.Visual.ChildName);
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
                            // TODO(Milestone 0.2): apply mesh/prefab from the bundle (needs smoke test).
                            MelonLogger.Msg($"[POGContentLib.Items] Bundle visual pending runtime smoke test: {def.ContentId}");
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

        // ---- Use flow (host-authoritative) ----

        /// <summary>Try to use an item. Called from the Interact Harmony patch.</summary>
        public void TryUseItem(InventoryItem item, Entity owner)
        {
            if (item == null || owner == null) return;

            var netObj = item.GetComponent<NetworkObject>();
            if (netObj == null) return;
            var def = ResolveDefinition(netObj.GlobalObjectIdHash);
            if (def == null) return;

            var handle = item.GetComponent<ModItemHandle>();
            if (handle != null && handle.IsConsumed) return;

            if (string.IsNullOrEmpty(def.UseHandlerId) || !_handlers.TryGetValue(def.UseHandlerId, out var handler))
                return;

            var nm = NetworkManager.Singleton;
            bool isHost = nm != null && nm.IsServer;

            // Host-authoritative: a client would route the request via HostBridge (v2). For now, host-only.
            if (!isHost)
            {
                MelonLogger.Msg("[POGContentLib.Items] Use on client deferred (HostBridge RPC is a v2 feature).");
                return;
            }

            var ctx = new UseContext { IsHost = true, Bridge = CoreServices.Bridge };
            if (!handler(item, owner, ctx)) return;

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
