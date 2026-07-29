# Custom meshes and visual effects

**Short answer: yes.** You can ship your own model with your own visual effects and use it as a mod
item — through an AssetBundle. There is one hard limit worth knowing up front, because it decides how
you design the asset.

## The one hard limit: no custom C# scripts

The game is **IL2CPP**, which means C# is compiled ahead of time into native code. A `MonoBehaviour`
script you wrote in the Unity Editor **does not exist** in the shipped runtime, so when Unity loads
your bundle it silently drops that component. No error — the object just arrives without it.

What this means in practice:

| Rides along inside your prefab | Does NOT survive |
|-------------------------------|------------------|
| Meshes, materials, textures | Your own `MonoBehaviour` scripts |
| **Particle Systems** (Shuriken) | Anything driven by your own script |
| **Lights** (incl. HDRP light data) | Custom ScriptableObjects of your own types |
| Trail / Line renderers | |
| Animator + Animation clips | |
| Audio sources & clips | |

So **visual effects are fine** — particles, glow, lights, trails, animation are all engine-side data,
not scripts. You only need to move *logic* out of the bundle and into your mod's C# (a use handler or
a capability in POGContentLib).

## Building the bundle

1. Unity **2022.3.62f2** with **HDRP 14** — the game's exact version. A different version is the
   usual cause of magenta materials or a bundle that refuses to load.
2. Build your item as a **prefab**: mesh + materials + any particle systems / lights / animation.
   Do not attach your own scripts.
3. Assign an AssetBundle name to the prefab and build for **StandaloneWindows64**.
4. Ship the `.bundle` file with your mod.

## Using it

```csharp
Content.Items.Register(new ModItemDefinition
{
    ModId = "MyPack", ContentId = "CrystalLantern",
    Visual = ItemVisual
        .Bundle("Mods/MyPack/crystal.bundle", "CrystalLantern")
        .At(Vector3.zero, Vector3.zero, scale: 1f),   // place it inside the item

    // Effects your prefab does NOT already contain can be added by the Lib:
    Capabilities =
    {
        new GlowCapability { Colour = Color.cyan, Intensity = 2f, Pulse = true },
    },
});
```

The Lib instantiates your prefab as the item's visual and disables the shell's own renderers, so only
your model shows. Everything else about the item — icon, price, loot placement, use handler — works
exactly as with a reskin.

### Shader repair

If a material comes through with the magenta error shader (a version mismatch), the Lib re-points it
at the game's own `HDRP/Lit`. Unity keeps every property whose name matches, so base colour and maps
usually survive. It logs when it does this — treat that log line as "rebuild the bundle against the
right Unity version", not as a fix.

Set `RepairShaders = false` on the `ItemVisual` if you would rather see the failure untouched.

## Without the Unity Editor

If you do not want to open Unity at all:

- **PNG icon** — `ItemVisual.Png("Mods/MyPack/icon.png")` works today, no editor needed.
- **Reskin a vanilla mesh** — `ItemVisual.GameMesh("Item_SpeakingStone", "SpeakingStone_01")` clones
  an existing game mesh; combine with `Tint` and a `GlowCapability` to make it look different.
- Loose OBJ mesh loading is on the roadmap, not implemented.

## Copying a vanilla look

To match how a vanilla item glows or pulses, capture its real values first — they live in prefab
serialized data and cannot be read from the game's code. Put the prefab name in
`UserData/pog_probe.txt` (one per line), run the game, and read the `VISUAL REPORT` block in
`MelonLoader/Latest.log`: light colour/intensity/range, flicker settings, emissive materials. Then set
those numbers on your `GlowCapability`. See `RUNTIME_TESTS.md` → T3e.

## Status

The bundle path is **implemented but not yet proven in-game** — the AssetBundle round-trip is
Milestone 0.2 in `ROADMAP.md`. Expect to iterate on scale/orientation the first time, and please
report what you find.

## Multiplayer note

Because the game runs with `ForceSamePrefabs = true`, **every player must have the same content
packs installed** — your bundle included. Mismatches make joins fail; the Lib's parity layer detects
this and reports what is missing (see `ROADMAP.md`).
