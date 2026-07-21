using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInventory;
using MelonLoader;
using UnityEngine;

namespace POGContentLib.Core
{
    /// <summary>Loot table category (an array in Il2Cpp.LootTable).</summary>
    public enum LootCategory { Treasure, Food, Useful, Big, Shop }

    /// <summary>
    /// Injects mod items into vanilla loot so they appear naturally. Two table families
    /// (ITEM_SYSTEM.md §5.6), each with its own technique — the framework handles both:
    ///  • Il2Cpp.LootTable — FIXED Il2CppReferenceArray per category → array-realloc.
    ///    Used by biome loot, secret rooms, LootSpawner. Target by LootCategory (all tables),
    ///    optionally restricted to named tables (e.g. one biome).
    ///  • Il2CppInventory.InventoryLootTable — growable List → List.Add. Used by ore veins,
    ///    shop pools, gambling, reward chests, dungeon-piece overrides. Target by explicit name.
    ///
    /// There is no rarity field: weight = copies appended (uniform draw), and value fields
    /// decide the value-budget match. Zero consumer patching — consumers read the array/list at
    /// call time. Host-side, at scene load, before dungeon generation.
    /// </summary>
    public class LootInjector
    {
        private readonly ContentRegistry _registry;
        private readonly List<Request> _requests = new();
        // Idempotency: (table instanceId, item hash) already injected.
        private readonly HashSet<long> _done = new();

        public LootInjector(ContentRegistry registry) { _registry = registry; }

        private struct Request
        {
            public InventoryItem Item;
            public LootCategory? Category;   // for the fixed-array family
            public string[] TableNames;      // name filter (both families); null = all LootTables of Category
            public int Weight;
            public uint Hash;
        }

        /// <summary>Register a placement (the domain module calls this after building a template).</summary>
        public void AddRequest(InventoryItem prefab, LootCategory? category, string[] tableNames, int weight)
        {
            if (prefab == null) return;
            var netObj = prefab.GetComponent<Unity.Netcode.NetworkObject>();
            uint hash = netObj != null ? netObj.GlobalObjectIdHash : 0u;
            _requests.Add(new Request
            {
                Item = prefab,
                Category = category,
                TableNames = tableNames,
                Weight = Math.Max(1, weight),
                Hash = hash,
            });
        }

        private static bool NameMatches(string[] filter, string name)
            => filter == null || Array.IndexOf(filter, name) >= 0;

        /// <summary>
        /// Inject all requests into loaded tables. Host-only. Idempotent: a repeat call on a new
        /// scene only appends into newly-loaded table instances.
        /// </summary>
        public void InjectIntoLoadedTables()
        {
            if (_requests.Count == 0) return;

            var fixedTables = Resources.FindObjectsOfTypeAll<LootTable>();
            var listTables = Resources.FindObjectsOfTypeAll<InventoryLootTable>();
            int injected = 0;

            foreach (var req in _requests)
            {
                // Fixed-array family (Il2Cpp.LootTable) — needs a Category to know which array.
                if (req.Category.HasValue)
                {
                    foreach (var lt in fixedTables)
                    {
                        if (lt == null || !NameMatches(req.TableNames, lt.name)) continue;
                        long key = Key(lt.GetInstanceID(), req.Hash);
                        if (_done.Contains(key)) continue;
                        if (AppendToCategory(lt, req.Category.Value, req.Item, req.Weight))
                        { _done.Add(key); injected++; }
                    }
                }

                // List family (InventoryLootTable) — requires an explicit name filter (else far too broad).
                if (req.TableNames != null)
                {
                    foreach (var ilt in listTables)
                    {
                        if (ilt == null || !NameMatches(req.TableNames, ilt.name)) continue;
                        long key = Key(ilt.GetInstanceID(), req.Hash);
                        if (_done.Contains(key)) continue;
                        if (AppendToList(ilt, req.Item, req.Weight))
                        { _done.Add(key); injected++; }
                    }
                }
            }

            if (injected > 0)
                MelonLogger.Msg($"[POGContentLib] Loot injected into {injected} table(s).");
        }

        private static long Key(int instanceId, uint hash) => ((long)instanceId << 32) ^ hash;

        // --- Il2Cpp.LootTable: fixed Il2CppReferenceArray → array-realloc ---

        private static bool AppendToCategory(LootTable table, LootCategory cat, InventoryItem item, int weight)
        {
            try
            {
                switch (cat)
                {
                    case LootCategory.Treasure: table.m_treasureItems = Grow(table.m_treasureItems, item, weight); break;
                    case LootCategory.Food:     table.m_foodItems     = Grow(table.m_foodItems, item, weight); break;
                    case LootCategory.Useful:   table.m_usefulItems   = Grow(table.m_usefulItems, item, weight); break;
                    case LootCategory.Big:      table.m_bigItems      = Grow(table.m_bigItems, item, weight); break;
                    case LootCategory.Shop:     table.m_shopItems     = Grow(table.m_shopItems, item, weight); break;
                    default: return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[POGContentLib] LootTable append failed ({table.name}/{cat}): {ex.Message}");
                return false;
            }
        }

        private static Il2CppReferenceArray<InventoryItem> Grow(Il2CppReferenceArray<InventoryItem> src, InventoryItem item, int weight)
        {
            int oldLen = src != null ? src.Length : 0;
            var dst = new Il2CppReferenceArray<InventoryItem>(oldLen + weight);
            for (int i = 0; i < oldLen; i++) dst[i] = src[i];
            for (int i = 0; i < weight; i++) dst[oldLen + i] = item;
            return dst;
        }

        // --- Il2CppInventory.InventoryLootTable: growable List → List.Add ---

        private static bool AppendToList(InventoryLootTable table, InventoryItem item, int weight)
        {
            try
            {
                var items = table.Items;
                if (items == null) return false;
                for (int i = 0; i < weight; i++) items.Add(item);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[POGContentLib] InventoryLootTable append failed ({table.name}): {ex.Message}");
                return false;
            }
        }
    }
}
