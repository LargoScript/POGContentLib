# POGContentLib — Runtime Test Guide (Milestone 0)

The framework is coded to statically-confirmed facts. These checks validate the runtime paths that
**cannot** be proven without launching the game — most importantly the `ForceSamePrefabs=true`
behaviour and the new multiplayer content-parity layer. Do them in order; each says exactly which
log line means pass or fail.

## Setup

Deployed to `Mods/` (already done by the build step):

| File | Role |
|------|------|
| `POGContentLib.dll` | the framework (loads first, `MelonPriority(-1000)`) |
| `POGCustomStones.dll` | reference content pack — registers `POGCustomStones:InvisibilityStone` and `POGCustomStones:TeleportStone` |

Logs: **`MelonLoader/Latest.log`** (and `MelonLoader/Logs/` for older runs). Every framework line is
prefixed `[POGContentLib]`; the pack uses `[CustomStones]`.

Two-player tests need **two machines** (Steam is single-instance per account). Use the two PCs on the
tailnet (`DESKTOP-RAED99F` ⇄ `danibani`); copy the same `Mods/` files to both, then vary them per test.

---

## T0 — Boot & compatibility probe (single instance)

1. Launch the game once, reach the main menu, quit.
2. In `Latest.log` look for:
   - `[POGContentLib] Initializing (MelonMod)...`
   - `[POGContentLib] Compatibility OK (N game members resolved).`
   - `POG Custom Stones 2.0.0 loaded (on POGContentLib).`

**PASS:** "Compatibility OK". **FAIL / ACTION:** `COMPATIBILITY: x/N ... NOT FOUND` lists the exact
missing member — the game updated; fix that entry in `Core/GameNames.cs`. Note whether any missing
line is a `Il2CppSteamworks.Data.Lobby` / `Il2CppGame.Networking.NetworkHandler` member — that only
disables lobby-metadata parity (T3), not the rest.

---

## T1 — Deterministic hash registration (host side)

1. Host a game (single player is fine to start).
2. On session start expect:
   - `[POGContentLib] Registered definition: POGCustomStones:InvisibilityStone (hash=XXXXXXXX)`
   - `[POGContentLib] Registered definition: POGCustomStones:TeleportStone (hash=XXXXXXXX)`
   - `[POGContentLib] Network prefabs registered: 2 ok, 0 failed.`

**PASS:** `2 ok, 0 failed`. **FAIL:** `Post-verify FAILED` (prefab not in `NetworkPrefabOverrideLinks`)
or `AddNetworkPrefab threw` — the registration window / write path is wrong; capture the full line.
A `Hash collision ... re-salted` line is not a failure (the collision handler worked).

*Give yourself the stones to test them:* spawn/obtain the two stones and use them — Invisibility should
make enemies ignore you for ~15 s (`[CustomStones] Invisibility 15s.`), Teleport should move you to the
bucket, twice (`[CustomStones] Teleported to bucket.`).

---

## T2 — Two-player registration (the ForceSamePrefabs core test)

Both peers have **identical** `Mods/` (Lib + Stones).

1. PC-A hosts, PC-B joins.
2. On the client (PC-B) expect the same registration lines as T1, and the client must see the **real
   mod item** (tinted SpeakingStone mesh + correct name), **not** a plain diamond shell.
3. Have the host drop a stone where the client can pick it up; confirm the client sees it correctly and
   using it triggers the effect (effects run host-side).

**PASS:** client registers 2 prefabs, sees the real items, effects fire. **FAIL:** client sees a plain
diamond (hash mismatch between peers) or the join drops — capture both logs.

---

## T3 — Multiplayer content parity (new layer)

The point: under `ForceSamePrefabs=true` a mismatched pack set makes joins fail opaquely. The Lib should
detect and surface it **before** that.

### T3a — Matched (both peers have Stones)
Host, then join. Expect on the **host**:
`[POGContentLib] Parity advertised: 2 item(s), token=XXXXXXXX.`
and on the **client**:
`[POGContentLib] Parity OK (token=XXXXXXXX matches host).`

**PASS:** both lines appear and tokens match.
**FAIL / RUNTIME-TODO:** `Parity not advertised (no lobby yet ...)` or `Parity/Steam bridge unavailable`
means the lobby handle isn't populated at the Start* moment we hook — this is the documented Milestone-0
timing unknown. Record where in the connect flow the lobby actually becomes available (this tells us
whether to move the hook, e.g. onto `NetworkHandler.CreateLobbyAsync` / the join path).

### T3b — Mismatched (host has Stones, client does NOT)
Remove `POGCustomStones.dll` from the **client's** `Mods/` (keep `POGContentLib.dll`). Host with Stones,
client joins. Expect on the **client**:
```
[POGContentLib] Content mismatch with host:
  MISSING (install/enable to join): POGCustomStones:InvisibilityStone@1, POGCustomStones:TeleportStone@1
```
(The `[POGContentLib] Content mismatch ...` warning is emitted by `RaiseMismatch` and is the proof —
`Content.Parity.OnMismatch` is raised at the same point for any subscriber, e.g. POGConfig's future UI.)

**PASS:** the mismatch line lists exactly the two missing ids. Also note what the vanilla NGO layer does
(does the join get rejected, and with what message) — that's the fallback the parity UI improves on.

### T3c — Reverse (client has Stones, host does NOT)
Swap: host has only the Lib, client has Lib + Stones. Expect on the client either
`EXTRA (host does not have these): ...` (if the host still advertises an empty manifest) or
`... (host advertised no parity data ...)`.

### T3d — Vanilla host (no mods at all)
Client (Lib + Stones) joins a completely vanilla host. Expect the client to note the host advertised no
parity data and flag its content as extra. Confirm this does not crash the client.

---

## T3e — Capture real glow/pulse values (VisualProbe)

Prefab-serialized visuals (light colour, intensity, range, flicker settings, emissive materials)
cannot be read by any static tool — this captures them from a live instance so they can be fed into a
`GlowCapability`. **No code needed.**

1. Create `UserData/pog_probe.txt` next to the game with one prefab name per line:
   ```
   Item_SpeakingStone
   Item_GlowingOrb
   Item_Torch
   ```
2. Launch the game and load into the lobby (prefabs load progressively; the probe retries on every
   scene load until each one resolves).
3. In `Latest.log` find the block:
   `[POGContentLib.Probe] VISUAL REPORT for 'Item_SpeakingStone'`

It lists child objects, every `Light` (type / colour RGBA / intensity / range / shadows, plus the
HDRP intensity and unit where present), every `LightFlicker` (strength / duration / vibrato /
randomness), particle systems, and emissive materials.

**PASS:** a report appears for each requested prefab. **FAIL:** `Prefab not loaded (yet)` on every
scene means the name is wrong or that item never loads in that scene — try the exact GameObject name.

**Send me the report block** — those are the numbers that turn `GlowCapability` defaults into an
accurate copy of the stone's look (and tell us the HDRP intensity offset, if any).

---

## T4 — AssetBundle round-trip (gates the visual pipeline, v0.2)

Only when a bundle is available: build an `HDRP/Lit` cube in Unity **2022.3.62f2**, load via
`AssetLoader.LoadBundle`, spawn it. **PASS:** the cube renders in-game with its HDRP material intact.
This is independent of T1–T3 and can be deferred.

---

## Reporting back

For any FAIL, the useful artifacts are: the full `[POGContentLib]` / `[CustomStones]` block from
`Latest.log`, which test number, and (for T2/T3) both peers' logs. That pins whether the issue is
registration timing, hash parity, or the lobby-metadata handle.
