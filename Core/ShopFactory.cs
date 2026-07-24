using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppGame.Spawning;
using Il2CppInventory;
using Il2CppInterop.Runtime;
using MelonLoader;
using Unity.Netcode;
using UnityEngine;

namespace POGContentLib.Core
{
    /// <summary>
    /// Creates shop slots at runtime. Grounded in the n0x-confirmed shop principle
    /// (ITEM_SYSTEM.md §8): a ShopLocation is a placed component = a position (m_itemHolder) +
    /// a price label (m_priceTagText) + one item, drawn either from a fixed m_networkPrefab or
    /// (if that is null) a random m_lootTable (InventoryLootTable, uniform). The price is NOT set
    /// on the slot — AddItem copies the chosen item's m_shopCost into m_currentPrice, and
    /// OnPriceChanged updates the label. So "random priced slot" = null m_networkPrefab +
    /// an m_lootTable pool whose items each carry m_shopCost.
    ///
    /// EXPERIMENTAL (v0.2). Two things are only statically reasoned and MUST be validated in a
    /// 2-player game before trusting them (marked RUNTIME-TODO below):
    ///   1. Whether a runtime clone of a scene ShopLocation replicates to clients as a dynamic
    ///      NetworkObject, and the correct registration/timing under ForceSamePrefabs=TRUE.
    ///   2. Whether the cloned m_priceTagText (a scene-local TMP child) rebinds under the clone.
    /// </summary>
    public class ShopFactory
    {
        private readonly ContentRegistry _registry;

        public ShopFactory(ContentRegistry registry) { _registry = registry; }

        /// <summary>Find a live ShopLocation to use as a clone template (only present in shop scenes).</summary>
        public static ShopLocation FindTemplate()
        {
            var all = Resources.FindObjectsOfTypeAll<ShopLocation>();
            foreach (var s in all)
                if (s != null && s.gameObject != null && !s.gameObject.scene.IsValid() == false) return s;
            return all != null && all.Length > 0 ? all[0] : null;
        }

        /// <summary>
        /// Build an InventoryLootTable pool (the random stock) from mod item prefabs. Each item's
        /// m_shopCost is its displayed price. Duplicate an item to weight it (uniform draw).
        /// </summary>
        public static InventoryLootTable CreatePool(string name, IEnumerable<InventoryItem> items)
        {
            var table = ScriptableObject.CreateInstance(Il2CppType.Of<InventoryLootTable>())
                            .Cast<InventoryLootTable>();
            table.name = name;
            var list = new Il2CppSystem.Collections.Generic.List<InventoryItem>();
            foreach (var it in items) if (it != null) list.Add(it);
            table.Items = list;
            return table;
        }

        /// <summary>
        /// Create a RANDOM priced shop slot at a position by cloning a template ShopLocation.
        /// Host-side. Returns the new ShopLocation (or null on failure). The slot draws a uniform
        /// item from <paramref name="pool"/> each populate and prices it from the item's m_shopCost.
        /// </summary>
        public ShopLocation PlaceRandomSlot(string slotId, Vector3 position, Quaternion rotation,
                                            InventoryLootTable pool, ShopLocation template = null)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer)
            {
                MelonLogger.Warning("[POGContentLib] PlaceRandomSlot requires host.");
                return null;
            }
            template = template ?? FindTemplate();
            if (template == null)
            {
                MelonLogger.Warning("[POGContentLib] No ShopLocation template in memory (not a shop scene?).");
                return null;
            }
            if (pool == null)
            {
                MelonLogger.Error("[POGContentLib] PlaceRandomSlot needs an InventoryLootTable pool.");
                return null;
            }

            var go = UnityEngine.Object.Instantiate(template.gameObject);
            go.name = $"ModShopSlot_{slotId}";
            go.transform.SetPositionAndRotation(position, rotation);

            var slot = go.GetComponent<ShopLocation>();
            if (slot == null) { UnityEngine.Object.Destroy(go); return null; }

            // Random slot: null the fixed prefab, set the pool. (SpawnerBase.m_networkPrefab.)
            slot.m_networkPrefab = null;
            slot.m_lootTable = pool;

            // Deterministic hash so the clone is a distinct network prefab on every peer.
            var netObj = go.GetComponent<NetworkObject>();
            if (netObj == null) { UnityEngine.Object.Destroy(go); return null; }
            netObj.GlobalObjectIdHash = ContentRegistry.ComputeHash("POGShop", slotId);
            _registry.RegisterPrefab(netObj);

            // RUNTIME-TODO(1): registration timing. Under ForceSamePrefabs=TRUE, AddNetworkPrefab
            // must run before the session starts on every peer — a slot created at scene load
            // (post-StartHost) may need a different path (pre-session declaration, or an
            // INetworkPrefabInstanceHandler). Here we register + spawn best-effort; verify in-game.
            try { nm.AddNetworkPrefab(go); } catch (Exception ex)
            { MelonLogger.Warning($"[POGContentLib] Shop slot AddNetworkPrefab: {ex.Message}"); }

            try { netObj.Spawn(true); }
            catch (Exception ex)
            {
                MelonLogger.Error($"[POGContentLib] Shop slot Spawn failed: {ex.Message}");
                UnityEngine.Object.Destroy(go);
                return null;
            }

            // RUNTIME-TODO(2): confirm the cloned m_priceTagText rebinds (scene-local TMP ref);
            // if not, re-point slot.m_priceTagText to the child TMP under the clone here.
            MelonLogger.Msg($"[POGContentLib] Placed random shop slot '{slotId}' (hash={netObj.GlobalObjectIdHash:X8}).");
            return slot;
        }
    }
}
