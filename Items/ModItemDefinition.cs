using Il2Cpp;
using POGContentLib.Core;
using UnityEngine;

namespace POGContentLib.Items
{
    /// <summary>Item visual kind.</summary>
    public enum ItemVisualKind
    {
        None,
        GameMesh,     // clone a mesh child from an existing game prefab (reskin, no bundle)
        Png,          // loose PNG icon → Sprite
        BundlePrefab, // prefab/mesh from an AssetBundle
    }

    /// <summary>Visual source descriptor (optional).</summary>
    public sealed class ItemVisual
    {
        public ItemVisualKind Kind = ItemVisualKind.None;
        public string Path;                 // path to a PNG or a .bundle
        public string AssetName;            // asset name inside the bundle (for BundlePrefab)
        public string SourcePrefabName;     // donor game prefab (for GameMesh)
        public string ChildName;            // mesh child name to clone (for GameMesh)
        public bool StripSpeakingStone = true; // when donor is SpeakingStone, strip voice/VFX

        // — Placement of the custom visual inside the item (BundlePrefab) —
        public Vector3 LocalOffset = Vector3.zero;
        public Vector3 LocalEuler = Vector3.zero;
        public Vector3 LocalScale = Vector3.one;
        /// <summary>Re-point materials that lost their shader in the bundle onto the game's HDRP/Lit.</summary>
        public bool RepairShaders = true;

        /// <summary>Reskin by cloning a mesh child from an existing game prefab (no AssetBundle).</summary>
        public static ItemVisual GameMesh(string sourcePrefabName, string childName)
            => new ItemVisual { Kind = ItemVisualKind.GameMesh, SourcePrefabName = sourcePrefabName, ChildName = childName };

        public static ItemVisual Png(string path) => new ItemVisual { Kind = ItemVisualKind.Png, Path = path };

        /// <summary>
        /// Custom mesh/prefab from an AssetBundle — your own model WITH its own visual effects
        /// (particles, lights, trails, animation all ride along inside the prefab). Custom C#
        /// MonoBehaviour scripts do NOT survive under IL2CPP; use capabilities/use handlers for logic.
        /// Build the bundle with the game's Unity version (2022.3.62f2, HDRP 14).
        /// </summary>
        public static ItemVisual Bundle(string bundlePath, string assetName)
            => new ItemVisual { Kind = ItemVisualKind.BundlePrefab, Path = bundlePath, AssetName = assetName };

        /// <summary>Position/rotate/scale the custom visual inside the item (fluent).</summary>
        public ItemVisual At(Vector3 offset, Vector3 euler = default, float scale = 1f)
        {
            LocalOffset = offset;
            LocalEuler = euler;
            LocalScale = new Vector3(scale, scale, scale);
            return this;
        }
    }

    /// <summary>
    /// Natural-drop placement. There is no rarity/weight field in the game — bias is emergent:
    /// weight = how many copies are appended (uniform selection turns that into weighting), and
    /// value fields (GoldValue/FoodValue/ShopCost) decide the value-budget match. See
    /// _research/ITEM_SYSTEM.md §5.
    ///
    /// Two table families (§5.6):
    ///  • Il2Cpp.LootTable — fixed arrays, 5 categories (biome loot, secret rooms, LootSpawner).
    ///    Target via Category (all tables) optionally restricted by name.
    ///  • Il2CppInventory.InventoryLootTable — growable List (ore veins, shop pools, gambling,
    ///    reward chests, dungeon-piece overrides). Target by explicit table name(s).
    /// </summary>
    public sealed class LootPlacement
    {
        /// <summary>LootTable array to append to (null = don't touch fixed-array tables).</summary>
        public LootCategory? Category;
        /// <summary>Restrict to these table names (both families). Null = all LootTables of Category.
        /// Required to inject into InventoryLootTable pools (ore/shop/gambling/reward).</summary>
        public string[] TableNames;
        /// <summary>Copies appended (uniform draw → this is the weight).</summary>
        public int Weight = 1;

        /// <summary>Append into the given category of ALL loaded Il2Cpp.LootTables (broad, e.g. all biomes).</summary>
        public static LootPlacement In(LootCategory category) => new LootPlacement { Category = category };

        /// <summary>Target named tables (e.g. "LootTable_Destructible_Ore_Diamond", "GamblingWheelSurprise",
        /// "DT_BonusChest_Tier1"). InventoryLootTable uses List.Add; a LootTable also needs a Category.</summary>
        public static LootPlacement InTables(params string[] names) => new LootPlacement { TableNames = names };

        /// <summary>Restrict a category placement to specific named LootTables (e.g. one biome).</summary>
        public LootPlacement RestrictTo(params string[] names) { TableNames = names; return this; }
        public LootPlacement WithWeight(int w) { Weight = w; return this; }
    }

    /// <summary>Mod item data contract (versioned — data outlives code).</summary>
    public sealed class ModItemDefinition : IContentDefinition, IContentVersion
    {
        // — Identity —
        public string ModId { get; set; }
        public string ContentId { get; set; }
        /// <summary>Data-contract version advertised in the multiplayer parity manifest (default "1").
        /// Bump when the item's network-visible shape changes so peers can flag a version mismatch.</summary>
        public string ContentVersion { get; set; } = "1";

        // — Shell / visual —
        /// <summary>Vanilla donor shell (Diamond by default — safe).</summary>
        public ShellFactory.ItemShellKind ShellKind { get; set; } = ShellFactory.ItemShellKind.Diamond;
        public ItemVisual Visual { get; set; }
        /// <summary>Direct icon override (takes precedence over IconSpriteName).</summary>
        public Sprite Icon { get; set; }
        /// <summary>Name of an existing game Sprite to use as the icon (resolved at build).</summary>
        public string IconSpriteName { get; set; }
        /// <summary>Optional jewel/mesh tint (HDRP _BaseColor).</summary>
        public Color? Tint { get; set; }

        // — Text / localization —
        public string DisplayName { get; set; }
        public string DisplayDescription { get; set; }
        /// <summary>I2 localization key for the name (falls back to DisplayName).</summary>
        public string TooltipKey { get; set; }
        /// <summary>I2 localization key for the description (falls back to DisplayDescription).</summary>
        public string TooltipDescriptionKey { get; set; }

        // — Classification (ITEM_SYSTEM.md §2). ItemType gates the inventory slot; MapIcon is the
        //   minimap marker — a SEPARATE field. Set these or the item mis-routes / has no map marker. —
        /// <summary>Inventory slot filter ([Flags]). Item=generic, Treasure=gems, Food, Weapon, Key.</summary>
        public ItemType ItemType { get; set; } = ItemType.Item;
        /// <summary>Minimap marker. Default/Food/Treasure/… (nested enum PassiveItem_Map.MapIconType).</summary>
        public PassiveItem_Map.MapIconType MapIcon { get; set; } = PassiveItem_Map.MapIconType.Default;

        // — Numeric properties. Also the drop-weight knobs in the value-budget loot spawner (§5). —
        /// <summary>Loot budget / treasure value (matched against LootValueTable_Gold levels).</summary>
        public int GoldValue { get; set; } = 50;
        /// <summary>Satiety when eaten (0-5). Real eating needs an eat capability too (v2).</summary>
        public int FoodValue { get; set; } = 0;
        /// <summary>Shop purchase price (decoupled from GoldValue). 0 = not priced.</summary>
        public int ShopCost { get; set; } = 0;

        // — Handling / "weight" (ITEM_ANATOMY.md §1.4 — there is NO weight field; these are the
        //   real proxies, all plain fields on InventoryItem, so setting them is statically sound). —
        /// <summary>Big item: takes the hands/large slot instead of a normal inventory slot (m_isBigItem).</summary>
        public bool Big { get; set; } = false;
        /// <summary>Stamina drained when this item is picked up — the closest thing to weight (m_staminaPenaltyOnPickup).</summary>
        public float PickupStaminaPenalty { get; set; } = 0f;
        /// <summary>Movement multiplier while held (1 = no slow-down; &lt;1 = heavier feel) (m_movementFactorWhenHeld).</summary>
        public float MovementFactorWhenHeld { get; set; } = 1f;

        // — Usage —
        public bool Consumable { get; set; } = true;
        public int MaxUses { get; set; } = 1;
        /// <summary>Id of a registered use handler (RegisterUseHandler).</summary>
        public string UseHandlerId { get; set; }

        /// <summary>
        /// Another registered content item to leave behind when this one is used up — a spent husk, an
        /// empty bottle, a broken tool. Accepts "contentId" (same mod) or "modId:contentId".
        ///
        /// Uses the game's OWN mechanism (<c>InventoryItem.m_spawnOnDestroy</c>), so the replacement is
        /// spawned and replicated by the game rather than by us: it appears for every player, survives
        /// saving, and needs no RPC of our own.
        /// </summary>
        public string SpawnOnConsume { get; set; }

        // — Capabilities (EXPERIMENTAL, v0.2) — declarative sibling components (eat/weapon/throw).
        //   See ItemCapability. Attached at template build; validate on-item in Milestone 0. —
        public System.Collections.Generic.List<ItemCapability> Capabilities { get; } =
            new System.Collections.Generic.List<ItemCapability>();

        // — Natural drops (optional) —
        public LootPlacement Loot { get; set; }
    }
}
