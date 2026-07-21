# POGContentLib — modular content framework for Pit of Goblin

> **Version:** 0.1.0 (in development) · **Game:** Pit of Goblin · **Mod loader:** MelonLoader 0.7.2

One library with a modular core for adding **items, mobs, levels and effects** to the game via
community content packs. Targets **Unity IL2CPP + Unity Netcode for GameObjects** (host-authoritative).

## Architecture

```
POGContentLib.dll
├── Core/                     ← stable core (code seam: interfaces)
│   ├── IPOGPlugin, PluginRegistry, PluginGraph   — plugin graph (Bevy-style)
│   ├── ContentRegistry        — definitions registry, deterministic GlobalObjectIdHash
│   ├── ShellFactory           — clone a vanilla shell prefab + assign the hash
│   ├── NetworkRegistrar        — register NetworkPrefabs before the session starts
│   ├── LootInjector            — append items into vanilla loot tables (array-realloc)
│   ├── SaveGuard               — Harmony guard for the reward-chest load path
│   ├── AssetLoader             — AssetBundle + loose PNG → Sprite
│   └── HostBridge              — client→host via CustomMessagingManager (v2)
├── Items/                    ← domain module v1 (current)
├── Mobs/                     ← v3 (planned)
├── Levels/                   ← v4 (planned)
└── Effects/                  ← stretch
```

### Lifecycle stages

Stages are bound to real MelonLoader + NGO events (not a one-shot init) and may run more than
once, so systems are idempotent:

1. **Boot** (`OnInitializeMelon`) — register services, use-handlers, data contracts.
2. **SceneLoaded** (`OnSceneWasLoaded`) — build item templates once shell prefabs are in memory.
3. **BeforeSessionStart** (Harmony prefix on `StartHost`/`StartClient`/`StartServer`) — register
   network prefabs (the only legal window under `ForceSamePrefabs=true`) and inject loot host-side.
4. **SessionStop** (Harmony postfix on `Shutdown`) — reset session-scoped state so a lobby rejoin
   re-registers content.

## Usage (for content-pack authors)

```csharp
using POGContentLib.Core;   // LootCategory
using POGContentLib.Items;  // Content, ModItemDefinition, LootPlacement

public override void OnInitializeMelon()
{
    Content.Items.RegisterUseHandler("heal", (item, owner, ctx) =>
    {
        // ctx.IsHost is guaranteed true (host-authoritative use flow)
        // apply the effect here
        return true; // return false if the effect did not apply (use is not consumed)
    });

    Content.Items.Register(new ModItemDefinition
    {
        ModId = "MyMod",
        ContentId = "HealingBerry",
        ShellKind = ShellFactory.ItemShellKind.Diamond, // safe default shell
        ItemType = Il2Cpp.ItemType.Food,                // inventory slot routing (§2)
        MapIcon  = Il2Cpp.PassiveItem_Map.MapIconType.Food, // minimap marker (separate field)
        GoldValue = 40,   // loot budget
        FoodValue = 3,    // satiety
        ShopCost  = 8,    // shop price (decoupled from gold)
        Consumable = true,
        MaxUses = 1,
        UseHandlerId = "heal",
        // Natural drops (optional): appears in all Food loot; weight = copies appended.
        Loot = LootPlacement.In(LootCategory.Food).WithWeight(2),
    });
}
```

> Content mods load **after** POGContentLib (it uses `[MelonPriority(-1000)]`), so calling
> `Content.Items.*` from your `OnInitializeMelon` is safe. Templates build on scene load;
> spawning/consume happen host-side.

### Natural loot placement

Bias is emergent — there is **no rarity field** (see
[`../_research/ITEM_SYSTEM.md`](../_research/ITEM_SYSTEM.md) §5). Weight = copies appended;
value fields decide the value-budget match. Two table families are both supported:

```csharp
// Broad: appears in every biome's Treasure loot (fixed Il2Cpp.LootTable, array-realloc)
Loot = LootPlacement.In(LootCategory.Treasure)
// Restricted to specific biomes
Loot = LootPlacement.In(LootCategory.Treasure).RestrictTo("LootTable_Crypt", "LootTable_Mines")
// Named InventoryLootTable pools — ore veins, shops, gambling, reward chests (List.Add)
Loot = LootPlacement.InTables("LootTable_Destructible_Ore_Diamond")   // gem ore drop
Loot = LootPlacement.InTables("GamblingWheelSurprise")                 // gambling prize
```

Injection is host-side at scene load, before dungeon generation. **Do not** target reward-chest
tables (`DT_BonusChest_*`) casually — a removed mod then NREs on save restore; the framework's
`SaveGuard` catches it, but keeping mod items out of persisted reward chests is safer.

## Key technical decisions

- **Identity = GlobalObjectIdHash** (`MD5(modId:contentId)`), not a string — one deterministic
  value gives stable save + network identity, identical on every peer.
- **Host-authoritative** execution (a client→host bridge is planned for v2).
- **No new RPCs / NetworkVariables** — impossible under IL2CPP; sync goes through existing
  vanilla RPCs and `CustomMessagingManager` named messages.
- **Natural loot** by appending prefab refs into vanilla ScriptableObject loot arrays
  (fixed `Il2CppReferenceArray`, so array-realloc — no consumer patching).
- **Versioned data contracts** per domain, so Items can ship while Mobs/Levels are still in research.

## Multiplayer note

Vanilla `NetworkConfig.ForceSamePrefabs` is `TRUE`, so **every player in a lobby must have the
same content packs** — a vanilla client cannot join a modded host. This is by design, not a bug;
a parity handshake (advertising installed packs) is planned for v2.

## Open questions (Milestone 0) — require launching the game

1. ✅ `NetworkConfig.ForceSamePrefabs` — already resolved statically to **TRUE** → the
   "mods-on-all-peers + join gate" architecture (only a runtime log-confirmation remains).
2. ⚠️ AssetBundle round-trip smoke test (an `HDRP/Lit` cube built in Unity 2022.3.62f2) —
   gates the full visual bundle pipeline.
3. ⚠️ Two-player test: a deterministic-hash prefab registers on both peers and the client sees
   the real mod item (not the shell).

Details and the full plan are in [ROADMAP.md](ROADMAP.md).

## Dependencies

- MelonLoader 0.7.2+ (net6)
- Il2CppInterop
- Harmony
- Unity Netcode for GameObjects (NGO)
- Assembly-CSharp (the game)

## License

MIT — POG community
