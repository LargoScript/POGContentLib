using System;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using Unity.Netcode;

namespace POGContentLib.Core
{
    /// <summary>
    /// Template factory. Under IL2CPP an InventoryItem cannot be created from scratch — a
    /// vanilla shell prefab must be cloned. The clone gets a DETERMINISTIC GlobalObjectIdHash
    /// (otherwise NGO resolves the spawn to the shell — the old mod's core bug).
    /// </summary>
    public class ShellFactory
    {
        private readonly ContentRegistry _registry;

        public ShellFactory(ContentRegistry registry) { _registry = registry; }

        /// <summary>
        /// Recommended shells. Diamond is the default (has InventoryItem+NetworkObject+Rigidbody,
        /// no VoIP/VFX/spawnOnDestroy). SpeakingStone is deliberately NOT offered as a shell —
        /// it drags voice/VFX and m_spawnOnDestroy (infinite-spawn risk).
        /// </summary>
        public enum ItemShellKind
        {
            Diamond,     // default
            Coin,
            Gem,
        }

        public static string ShellPrefabName(ItemShellKind kind) => kind switch
        {
            ItemShellKind.Coin => "Item_Coin",
            ItemShellKind.Gem => "Item_Gem_Emerald",
            _ => "Item_Diamond",
        };

        /// <summary>
        /// Find a vanilla shell prefab by name. Prefers a "clean" asset instance (not a
        /// scene-spawned one) so we don't clone dirty live state.
        /// </summary>
        public static InventoryItem FindShell(string prefabName)
        {
            var all = Resources.FindObjectsOfTypeAll<InventoryItem>();
            InventoryItem live = null;
            foreach (var it in all)
            {
                if (it == null || it.gameObject == null) continue;
                if (it.gameObject.name != prefabName) continue;
                // An asset instance does not belong to a scene (scene.IsValid() == false).
                if (!it.gameObject.scene.IsValid()) return it;
                live = live ?? it;
            }
            return live; // fallback: whatever we found, if no clean asset exists
        }

        /// <summary>
        /// Build an item template: clone the shell, disable it, DontDestroyOnLoad, assign the
        /// deterministic hash. Visual/identity are applied separately by the domain module.
        /// Returns null if the shell is not in memory yet (retry on the next scene load).
        /// </summary>
        public InventoryItem BuildItemTemplate(ItemShellKind shellKind, string modId, string contentId)
        {
            string shellName = ShellPrefabName(shellKind);
            var shell = FindShell(shellName);
            if (shell == null)
            {
                MelonLogger.Warning($"[POGContentLib] Shell not in memory yet: {shellName} (will retry).");
                return null;
            }

            var go = UnityEngine.Object.Instantiate(shell.gameObject);
            go.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.name = $"ModItem_{modId}_{contentId}";

            var item = go.GetComponent<InventoryItem>();
            if (item == null)
            {
                MelonLogger.Error($"[POGContentLib] Shell {shellName} has no InventoryItem; aborting {contentId}.");
                UnityEngine.Object.Destroy(go);
                return null;
            }

            // Deterministic GlobalObjectIdHash (writable uint32) — BEFORE network registration.
            var netObj = go.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                MelonLogger.Error($"[POGContentLib] Shell {shellName} has no NetworkObject; aborting {contentId}.");
                UnityEngine.Object.Destroy(go);
                return null;
            }
            uint hash = ContentRegistry.ComputeHash(modId, contentId);
            netObj.GlobalObjectIdHash = hash;

            // Strip the dangerous self-spawn chain inherited from some shells.
            item.m_spawnOnDestroy = null;

            _registry.RegisterPrefab(netObj);
            MelonLogger.Msg($"[POGContentLib] Built template {go.name} (shell={shellName}, hash={hash:X8}).");
            return item;
        }
    }
}
