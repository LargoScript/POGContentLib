using System.Collections.Generic;
using Il2Cpp;
using Il2CppGame.Spawning;
using MelonLoader;
using POGContentLib.Core;
using Unity.Netcode;
using UnityEngine;

namespace POGContentLib.Items
{
    /// <summary>
    /// Shop API (EXPERIMENTAL, v0.2). Adds priced shop slots at runtime, built on the confirmed
    /// shop principle (see ITEM_SYSTEM.md §8). Two ways to sell a mod item:
    ///
    ///  1. Inject into an EXISTING random shop pool (works today, statically sound): give the item
    ///     a ShopCost and a loot placement into a named shop InventoryLootTable, e.g.
    ///       Loot = LootPlacement.InTables("LootTable_Curiosa_Slot2")
    ///     The vanilla hub "Curiosa" random slots then draw and price it.
    ///
    ///  2. Create a NEW random priced slot at a position via PlaceRandomSlot (this API). Host-side.
    ///     Runtime-uncertain (dynamic scene-NetworkObject replication + price-tag rebind) — validate
    ///     in a 2-player game before relying on it.
    ///
    /// Call these host-side after a shop scene has loaded (e.g. your own OnSceneWasLoaded + IsServer).
    /// </summary>
    public sealed class ShopsApi
    {
        /// <summary>Resolve a registered mod item's InventoryItem prefab by id.</summary>
        public static InventoryItem ResolveItem(string modId, string contentId)
        {
            uint hash = ContentRegistry.ComputeHash(modId, contentId);
            var no = CoreServices.Content.GetPrefabByHash(hash);
            return no != null ? no.GetComponent<InventoryItem>() : null;
        }

        /// <summary>
        /// Place a new random priced shop slot at a position, stocked from the given registered
        /// mod items (each must have ShopCost set — that is its price). Duplicate an id to weight it.
        /// Returns the new ShopLocation, or null on failure. Host-only.
        /// </summary>
        public ShopLocation PlaceRandomSlot(string slotId, Vector3 position, Quaternion rotation,
                                            params (string modId, string contentId)[] items)
        {
            if (!CoreServices.Ready) return null;
            var prefabs = new List<InventoryItem>();
            foreach (var (m, c) in items)
            {
                var it = ResolveItem(m, c);
                if (it != null) prefabs.Add(it);
                else MelonLogger.Warning($"[POGContentLib] Shop slot '{slotId}': item {m}:{c} not built yet.");
            }
            if (prefabs.Count == 0)
            {
                MelonLogger.Error($"[POGContentLib] Shop slot '{slotId}' has no resolvable items.");
                return null;
            }
            var pool = ShopFactory.CreatePool($"ModShopPool_{slotId}", prefabs);
            return CoreServices.Shops.PlaceRandomSlot(slotId, position, rotation, pool);
        }
    }
}
