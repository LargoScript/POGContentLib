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
- [~] **Minimal parity layer (multiplayer PRECONDITION, promoted from v0.2) — SCAFFOLDED, compiles.**
      Because `ForceSamePrefabs=TRUE`, mismatched content packs make co-op joins fail with an opaque
      "connection failed". The Lib does this self-sufficiently (no other mod required):
    - [x] `ParityManifest` — deterministic, order-independent snapshot of registered content
          (`modId:contentId@version` + an 8-hex token); versioned wire format; pure logic, no game dep.
    - [x] `ParityService` — host advertises the manifest, a joining client reads and compares the
          host's; a mismatch is logged AND raised via the public `Content.Parity.OnMismatch` event
          (`ParityReport`: Missing / Extra / VersionMismatch). Fallback is a plain MelonLogger line.
    - [x] Channel = **Steam lobby metadata** (`NetworkHandler.Singleton.Lobby.SetData/GetData`, key
          `pog_parity`), read *before* connecting so it touches no NGO state. The NGO
          ConnectionApproval/`DisconnectReason` path is intentionally NOT used (vanilla already sets
          `ConnectionApproval=TRUE`; wrapping the game's own approval callback is riskier).
    - [x] `ParitySteamBridge` binds the Facepunch interop **reflectively** (guarded, degrades to one
          log line); `CompatibilityProbe` now name-checks those members too.
    - [ ] **RUNTIME-TODO (Milestone 0):** confirm `NetworkHandler.Singleton.Lobby` is populated and
          host-written metadata is visible to the joiner at the Start*/join moment we hook.
      Rich presentation is the optional POGConfig UI in v0.2. Validate in the 2-player test.
- [ ] Run Milestone 0; fix whatever the two-player test reveals.

## v0.2 — Items hardened + assets

- [~] **AssetBundle visual pipeline — IMPLEMENTED, unproven in-game.** `ItemVisual.Bundle(path, asset)`
      instantiates a custom prefab as the item's visual (with placement via `.At(offset, euler, scale)`)
      and disables the shell's renderers. Engine-side effects ride along inside the prefab (particles,
      lights, trails, animation); custom C# MonoBehaviours cannot survive IL2CPP and are dropped by
      Unity — logic belongs in capabilities/use handlers. Materials that lost their shader are repaired
      onto `HDRP/Lit` (same-named properties survive the swap). Modder guide: `CUSTOM_ASSETS.md`.
      Gated on the Milestone 0.2 round-trip test.
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
- [~] **Item capabilities — SCAFFOLDED, compiles.** Declarative `ItemCapability` on a definition
      attaches the sibling `ActiveItem_*` component and sets its scalar fields (item anatomy §2).
    - [x] `EatCapability` (health/stamina/satiety), `MeleeWeaponCapability` (durability + hit),
          `ThrowableCapability` (health/bounce); attached in `ItemsPlugin.ApplyCapabilities`, guarded.
    - [x] Handling / "weight" — `Big` / `PickupStaminaPenalty` / `MovementFactorWhenHeld` (plain
          `InventoryItem` fields; statically sound, not a component). There is no real weight field.
    - [ ] **RUNTIME-TODO (Milestone 0):** a bare shell lacks the components' serialized refs (VFX/
          sounds/curves) and their spawn-time NetworkVariables (durability/ammo/health seed); confirm
          the scalar effects fire and the added NetworkBehaviours stay in sync across peers.
    - [x] `GlowCapability` — glow/pulse the way vanilla does it (item anatomy §9): a child `Light`
          (colour / intensity / range), optional emissive material tint, optional pulse via the game's
          own `LightFlicker` (strength / duration / vibrato / randomness).
- [x] **Open visual-effect system (no hardcoded effects)** — `ItemCapability.Attach` is public, so a
      third-party mod implements effects through the *same* contract the built-ins use (no privileged
      core). `ItemVisualEffect` base gives `CreateEffectChild` (reused on rebuild, never stacks) and
      `GetVisualRoot` (follows a custom mesh). `ParticleEffect` covers the no-code cases: clone a
      vanilla VFX (`FromGameObject`) or instantiate your own particle prefab (`FromBundle`), with
      placement and recolour. `Content.Diagnostics.ListVfx(filter)` — or a `vfx:<filter>` line in
      `UserData/pog_probe.txt` — finds vanilla effects to reuse. Guide: `VISUAL_EFFECTS.md`.
    - [ ] Torch/light + weapon NetworkVariable seeding via an `OnNetworkSpawn` step (needs a donor
          prefab for the light/VFX refs) — v2.
- [x] **`VisualProbe` + `Content.Diagnostics`** — prefab-serialized visual data (light colour/
      intensity/range, flicker settings, emissive materials) is invisible to static analysis, so this
      logs it from a live instance. Drive it from code (`Content.Diagnostics.ProbeItem("Item_Speaking
      Stone")`) or with no code at all via `UserData/pog_probe.txt` (one prefab name per line). Use it
      to capture real numbers to feed into `GlowCapability`. Read-only; also reports HDRP intensity.
- [ ] Per-item state persistence: slot-keyed companion file (JsonUtility drops unknown fields,
      so mod state cannot live in the vanilla save).
- [ ] **Rich parity UI (optional, soft-dep)** — POGConfig (if installed) subscribes to the Lib's
      `OnParityMismatch` event and renders a native panel (installed packs, add/update/remove,
      shown *before* joining). Presentation only; detection stays in the Lib. NOT a separate mod.
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
