using System;
using System.Reflection;
using System.Text;
using Il2Cpp;
using Il2CppGame.Gameplay;
using Il2CppGame.Spawning;
using Il2CppI2.Loc;
using Il2CppInventory;
using Il2CppItems;
using MelonLoader;
using Unity.Netcode;

namespace POGContentLib.Core
{
    /// <summary>
    /// Load-time sanity check. IL2CPP mods break when a game update renames/removes a member the
    /// mod binds to by name; this reflects over the current interop and turns a silent mid-game
    /// NRE (or a PatchAll throw) into one clear, actionable log line naming exactly what is missing.
    /// Run it early in OnInitializeMelon, BEFORE Harmony PatchAll.
    /// </summary>
    public static class CompatibilityProbe
    {
        private const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        // The load-bearing game members the framework accesses by name or Harmony-patches.
        private static readonly (Type type, string[] members)[] Expected =
        {
            (typeof(InventoryItem), new[]
            {
                "m_itemID", "m_itemType", "m_mapIconType", "m_goldValue", "m_foodValue", "m_shopCost",
                "m_doConsume", "m_isConsumed", "m_itemIcon", "m_spawnOnDestroy", "m_spawnAmount", "m_spawnChance",
                "m_isBigItem", "m_staminaPenaltyOnPickup", "m_movementFactorWhenHeld", // handling / "weight"
                GameNames.Methods.InventoryItem_Interact, GameNames.Methods.InventoryItem_OnNetworkSpawn,
                "Entity", "IsInHands", "ConsumeItem_Owner",
            }),
            // Capability components (EXPERIMENTAL attach API) — scalar fields the Lib sets.
            (typeof(ActiveItem_Eat), new[]
                { "m_healthOnEating", "m_damageOnEating", "m_staminaRefill", "m_minStaminaBonus", "m_maxStaminaBonus", "m_stomachFullTime" }),
            (typeof(ActiveItem_MeleeWeapon), new[]
                { "m_maxDurability", "m_durabilityLossOnEntityHit", "m_durabilityLossOnDestructibleHit", "m_meleeHitCooldown" }),
            (typeof(ActiveItem_Throwable), new[] { "m_healthMax", "m_healthMin", "m_bounceFactor" }),
            (typeof(LootTable), new[] { "m_treasureItems", "m_foodItems", "m_usefulItems", "m_bigItems", "m_shopItems" }),
            (typeof(InventoryLootTable), new[] { "Items" }),
            (typeof(ItemContainer), new[] { GameNames.Methods.ItemContainer_FromContainerData }),
            (typeof(ShopLocation), new[] { "m_lootTable", "m_itemHolder", "m_priceTagText" }),
            (typeof(SpeakingStone), new[] { GameNames.Methods.SpeakingStone_Start, GameNames.Methods.SpeakingStone_SetEnabled }),
            (typeof(LocalizationManager), new[] { GameNames.Methods.LocalizationManager_GetTranslation }),
            (typeof(NetworkManager), new[]
            {
                "AddNetworkPrefab", nameof(NetworkManager.StartHost), nameof(NetworkManager.StartClient),
                nameof(NetworkManager.StartServer), nameof(NetworkManager.Shutdown), "NetworkConfig", "CustomMessagingManager",
            }),
            (typeof(NetworkObject), new[] { "GlobalObjectIdHash" }),
            (typeof(NetworkConfig), new[] { "ForceSamePrefabs", "Prefabs" }),
        };

        // Parity-layer members bound REFLECTIVELY (Facepunch interop is not referenced at compile
        // time), so they are resolved here by full type name rather than typeof(). A miss only
        // disables lobby-metadata parity detection — the game's own connect check still applies.
        private static readonly (string typeName, string[] members)[] ExpectedByName =
        {
            (GameNames.Steam.SteamManagerTypeFullName, new[] { GameNames.Steam.SteamManager_Lobby }),
            (GameNames.Steam.LobbyTypeFullName, new[]
                { GameNames.Steam.Lobby_SetData, GameNames.Steam.Lobby_GetData }),
        };

        /// <summary>Check all expected members; log a clear warning listing any that are missing.</summary>
        public static void Check()
        {
            var missing = new StringBuilder();
            int miss = 0, total = 0;
            foreach (var (type, members) in Expected)
            {
                foreach (var m in members)
                {
                    total++;
                    try
                    {
                        if (type.GetMember(m, Flags).Length == 0)
                        {
                            miss++;
                            missing.Append($"\n  - {type.Name}.{m}");
                        }
                    }
                    catch (Exception ex)
                    {
                        miss++;
                        missing.Append($"\n  - {type.Name}.{m} (probe error: {ex.GetType().Name})");
                    }
                }
            }

            // Reflective (by-name) members — Steam/parity bindings not referenced at compile time.
            foreach (var (typeName, members) in ExpectedByName)
            {
                Type type = FindType(typeName);
                foreach (var m in members)
                {
                    total++;
                    if (type == null || type.GetMember(m, Flags).Length == 0)
                    {
                        miss++;
                        missing.Append($"\n  - {typeName}.{m}{(type == null ? " (type not found)" : "")}");
                    }
                }
            }

            if (miss == 0)
            {
                MelonLogger.Msg($"[POGContentLib] Compatibility OK ({total} game members resolved).");
            }
            else
            {
                MelonLogger.Warning(
                    $"[POGContentLib] COMPATIBILITY: {miss}/{total} expected game members NOT FOUND — the game may " +
                    $"have updated. The framework may misbehave; update GameNames/bindings for this version. Missing:{missing}");
            }
        }

        /// <summary>Find a loaded type by full name across all assemblies (for by-name interop bindings).</summary>
        private static Type FindType(string fullName)
        {
            var direct = Type.GetType(fullName, throwOnError: false);
            if (direct != null) return direct;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { var t = asm.GetType(fullName, throwOnError: false); if (t != null) return t; }
                catch { /* dynamic/reflection-only assemblies can throw */ }
            }
            return null;
        }
    }
}
