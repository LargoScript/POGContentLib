# POGContentLib — Roadmap

Modular community content framework for **Pit of Goblin** (Unity IL2CPP, MelonLoader 0.7.2,
Unity Netcode for GameObjects). One package, modular inside: a stable `Core` plus per-domain
modules (`Items`, `Mobs`, `Levels`, `Effects`) each with its own versioned data contract.

Design rationale and the reverse-engineering that backs it: see
the content-framework research notes
(§2.5 lists the statically-confirmed facts this roadmap relies on).

---

## Milestone 0 — De-risk (requires launching the game; done by hand)

These cannot be verified without running the game in two instances — the framework is coded
to the statically-confirmed facts, but these three must be validated before trusting them:

- [ ] **0.1 ForceSamePrefabs** — already resolved statically to `TRUE`
      (NetworkConfig+0x6C). Confirm at runtime via a config log on session start.
- [ ] **0.2 AssetBundle round-trip smoke test** — build an `HDRP/Lit` cube in Unity
      2022.3.62f2, load it via `AssetLoader.LoadBundle`, spawn it in-game. Round-trip is not
      yet proven anywhere in this project; this gates the full bundle visual pipeline.
- [ ] **0.3 Two-player registration test** — host + client, confirm a deterministic-hash mod
      prefab registers on both peers and the client sees the real mod item (not the shell),
      and that a vanilla client is rejected at connect (expected, given ForceSamePrefabs=true).

---

## v0.1 — Core + Items (current)

Framework skeleton compiles clean; correctness of the runtime paths pending Milestone 0.

- [x] Bevy-style plugin graph (`IPOGPlugin` / `PluginGraph` / `PluginRegistry`) driven by real
      MelonLoader + NGO lifecycle stages (Boot / SceneLoaded / BeforeSessionStart / SessionStop).
- [x] `ContentRegistry` — deterministic `GlobalObjectIdHash` = MD5(modId:contentId), def-by-hash.
- [x] `ShellFactory` — clone a safe vanilla shell (Diamond default), assign the hash, strip
      the self-spawn chain; prefers clean asset instances over dirty live ones.
- [x] `NetworkRegistrar` + `NetworkLifecycleHooks` — register prefabs via a Harmony prefix on
      `StartHost/StartClient/StartServer` (the only legal window), post-verify via
      `NetworkPrefabOverrideLinks`, re-salt on hash collision, reset on `Shutdown`.
- [x] `LootInjector` — both table families (fixed `Il2Cpp.LootTable` array-realloc **and**
      `Il2CppInventory.InventoryLootTable` List.Add), targetable by category and/or table name
      (biomes, ore veins, shop pools, gambling, reward chests); host-side, zero consumer patching.
- [x] Full item identity: `ItemType` (slot routing) + `MapIcon` + `FoodValue`/`ShopCost` —
      driven by the item-system map (the item-system research notes).
- [x] `SaveGuard` — Harmony finalizer on `ItemContainer.FromContainerData` (the one save path
      that throws on an unknown hash → turns "lose all reward chests" into "lose one").
- [x] `Items` module — `ModItemDefinition`, host-authoritative use flow, `ModItemHandle`
      (hash-resolved state), Interact/OnNetworkSpawn Harmony patches.
- [ ] **Port POGCustomStones** onto POGContentLib as the reference content pack (validates the API).
- [ ] Run Milestone 0; fix whatever the two-player test reveals.

## v0.2 — Items hardened + assets

- [ ] Enable the full AssetBundle visual pipeline after 0.2 (mesh/prefab, HDRP shaders in bundle).
- [ ] Loose-file tier polish: PNG icons, OGG audio, OBJ meshes (no Unity Editor needed).
- [ ] `HostBridge` client→host use requests via `CustomMessagingManager` named messages
      (so non-host players can trigger item effects).
- [ ] **Shop slots & vendors** — a slot is NOT a hard-wired list entry. Each `ShopLocation` is an
      independent placed component = a *position* (`m_itemHolder`) + a price tag (`m_currentPrice`)
      + one item (fixed `m_networkPrefab` **or** a random draw from `m_lootTable`). So a slot can be
      positioned anywhere by cloning/placing a `ShopLocation`. Planned:
    - [ ] **Enumerate every vendor & slot** and capture their ids/references: hub/starting shops
          (~55 in the lobby) plus the *level* merchants — random-stock vendors that spawn
          probabilistically per run (entrance-cave / dungeon-event merchants; a mini-location that
          may or may not appear). Their stock is a random `InventoryLootTable`, so **new items can
          already be injected** into those pools via `LootPlacement.InTables(...)`.
    - [~] **`ShopPlacement` API — scaffolded (`Content.Shops.PlaceRandomSlot`), EXPERIMENTAL.**
          Clones a live `ShopLocation`, builds an `InventoryLootTable` pool from registered mod
          items, sets `m_lootTable`, assigns a deterministic hash, positions it and host-spawns.
          Price comes from each pool item's `ShopCost` (auto → `m_currentPrice` → label). Compiles;
          two runtime unknowns remain (RUNTIME-TODO in `ShopFactory.cs`): registration timing under
          `ForceSamePrefabs=TRUE`, and whether the cloned scene-`NetworkObject` + `m_priceTagText`
          replicate/rebind. Validate in a 2-player game. Injecting into EXISTING random pools
          (Curiosa/gambling) already works today via `LootPlacement.InTables`.
    - [ ] Confirm the "one `ShopLocation` = one slot, no count field" finding in-game (the item is
          bound to the place + gets a price tag — user's model; matches the static decompilation).
      (Shop/vendor internals are mapped in the project's item-system research notes, §7–§8.)
- [ ] **Item capabilities**: helper to attach `ActiveItem_*`/`PassiveItem_*` (eat/melee/throw/
      torch/glow) to a custom item, beyond the generic use-handler (ITEM_SYSTEM.md §3).
- [ ] Per-item state persistence: slot-keyed companion file (JsonUtility drops unknown fields,
      so mod state cannot live in the vanilla save).
- [ ] Parity handshake: advertise `contentId@version` via Steam lobby metadata (Facepunch);
      surface "missing content pack X" instead of a silent connect rejection
      (ConnectionApproval is `TRUE` in vanilla — an approval callback already exists).
- [ ] Retire legacy **POGCustomItems** to `archive/` once Items reaches parity
      (POGSpawner must be re-pointed at POGContentLib first).

## v0.3 — Mobs module

Same shape as Items (shell-clone + deterministic hash), confirmed feasible in the research.

- [ ] `ModMobDefinition` { shell, visuals, stats, dangerLevel, maxAlive, behavior }.
- [ ] Shell clones from `SkullEntity`/`RatEntity` (cheapest) / `SkeletonEntity` (humanoid).
- [ ] Spawn-table injection into `EnemyLootTable` arrays (fixed → array-realloc) and/or
      `SetEnemyLootTable`/`SetEnemyConfig` before `StartGame` on the host.
- [ ] Managed FSM API layered on the stock brain (`FSM.AddState/AddTransition(Func<bool>)`).
- [ ] Danger-budget balance via `m_dangerLevel` / `m_maxSpawnAmount` (no director patches).

## v0.4 — Levels module (tier a+)

Dungeon geometry is seed-local (no per-object network spawn), so custom piece-based dungeons
need no NGO scene-hash patching.

- [ ] `ModLevelDefinition` via runtime `DungeonDescriptor` + community `DungeonPiece` prefabs.
- [ ] Inject descriptors into `LevelInfoData.m_dungeons` rotation.
- [ ] Resurrect `UIDebugDungeonVisit` (or clone its flow) as a deterministic-seed test launcher.
- [ ] Consider splitting Levels into its own package if it grows an editor SDK.

## Later / stretch

- [ ] `Effects` module.
- [ ] Whole custom **scenes** from AssetBundles (tier b) — needs symmetric NGO
      `NetworkSceneManager` hash handling (`VerifySceneBeforeLoading` is a public settable field).
- [ ] Codeless content packs (manifest + bundle, folder auto-discovery) — the ecosystem-growth
      step; restrict bundles to vanilla-component prefabs under IL2CPP.
- [ ] POGConfig integration: in-game "installed content packs" panel + parity-mismatch UX (soft dep).
- [ ] Thunderstore release: framework package + content packs as pinned dependents.

---

## Known limitations (current)

- Runtime correctness of network registration, save behavior and bundle round-trip rests on
  static decompilation until Milestone 0 is run in-game.
- Use effects are host-only for now (client→host RPC is v0.2).
- Loot injection targets `LootTable` category arrays; `InventoryLootTable` (per-chest/enemy)
  targeting is a later, more selective feature.
- Custom visuals are icon/tint level until the AssetBundle smoke test (0.2) passes.
