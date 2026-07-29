# Visual effects — nothing is hardcoded

An item's look is built from **visual effects** you declare on its definition. The Lib ships a couple
of convenience effects (glow/pulse, particles), but they hold no privileged position: they implement
the same public contract your own effect would. Lightning crawling over the mesh, a frost aura, drifting
embers — all of it is yours to write, and it plugs in exactly like the built-ins.

There are three levels of effort. Pick the lowest one that does what you want.

---

## Level 1 — built-in glow / pulse (no assets, no code)

```csharp
Capabilities =
{
    new GlowCapability
    {
        Colour = new Color(0.55f, 0.88f, 1f),
        Intensity = 1.6f, Range = 3.5f,
        EmissiveMaterial = true,   // the mesh itself glows, not just the air around it
        Pulse = true,              // via the game's own LightFlicker
        PulseStrength = 0.8f, PulseDuration = 1.6f, PulseVibrato = 6, PulseRandomness = 0.15f,
    },
}
```

## Level 2 — reuse a vanilla particle effect (no assets, no code)

The game already contains lightning, fire, ice, smoke and dust effects. Clone one onto your item.

**Find one first.** Add a line to `UserData/pog_probe.txt`:

```
vfx:lightning
```

Run the game and read `MelonLoader/Latest.log`:

```
[POGContentLib.Probe] VFX matching 'lightning' (N) — use as ParticleEffect.FromGameObject(id, ROOT, CHILD):
    SomePrefab / VFX_Lightning_Small
```

Then use exactly those two names:

```csharp
Capabilities =
{
    ParticleEffect
        .FromGameObject("Sparks", "SomePrefab", "VFX_Lightning_Small")
        .At(Vector3.zero, scale: 0.5f)
        .Coloured(Color.cyan),          // optional recolour
}
```

`Content.Diagnostics.ListVfx("frost")` does the same from code. Try filters like `lightning`, `ice`,
`frost`, `smoke`, `spark`, `fire`, `dust`.

## Level 3 — your own effect

### 3a. Your own particle prefab from a bundle

```csharp
ParticleEffect.FromBundle("Frost", "Mods/MyPack/fx.bundle", "FrostAura").At(Vector3.zero, scale: 1f)
```

Particle systems, lights, trails and animation all survive the bundle. Custom C# scripts do **not**
(IL2CPP) — see [CUSTOM_ASSETS.md](CUSTOM_ASSETS.md).

### 3b. Your own effect class — full control

Subclass `ItemVisualEffect`. This is the same contract `GlowCapability` and `ParticleEffect` use;
there is no internal API they reach for that you cannot.

```csharp
public sealed class LightningEffect : ItemVisualEffect
{
    public Color Colour = Color.cyan;
    public float Radius = 0.25f;

    public override string Name => "Lightning";

    public override void Attach(InventoryItem item)
    {
        // A child object named "ModEffect_Lightning", reused on rebuild so effects never stack.
        var host = CreateEffectChild(item, "Lightning");

        // Parent to the visible model instead of the shell, so it follows a custom mesh:
        host.transform.SetParent(GetVisualRoot(item), false);

        // From here it is ordinary Unity: add a LineRenderer and animate it, instantiate a bundle
        // prefab, add Lights, clone a game VFX, attach a TrailRenderer — whatever you need.
        var line = host.AddComponent<LineRenderer>();
        line.positionCount = 8;
        line.widthMultiplier = 0.02f;
        line.startColor = line.endColor = Colour;
    }
}
```

Declare it like any other effect:

```csharp
Capabilities = { new LightningEffect { Colour = Color.magenta, Radius = 0.3f } }
```

**Animation note.** `Attach` runs once at build time, so continuous motion needs a driver. Options,
cheapest first: a particle system (self-animating), an `Animator` with a clip from your bundle, the
game's `LightFlicker` for light pulsing, or — if you truly need per-frame C# — a `MonoBehaviour` in
**your own mod** registered via `ClassInjector.RegisterTypeInIl2Cpp<T>()` before you add it. (Scripts
inside an AssetBundle can never work; scripts compiled into your mod can.)

---

## What the base class gives you

| Member | Purpose |
|--------|---------|
| `CreateEffectChild(item, name, offset)` | child object named `ModEffect_<name>`, reused on rebuild so duplicates never stack |
| `GetVisualRoot(item)` | the item's visible model (custom mesh or reskin) if present, else the item root |
| `AddOrGet<T>(go)` | add a component, or return the existing one |
| `Name` | shown in the build log |

## Rules that keep multiplayer working

- `Attach` runs on **every peer** at the same build point, in the order you declared the effects.
  Keep it **deterministic** — no randomness that differs per machine, no host-only branches.
- Each effect is wrapped in its own try/catch: one broken effect logs a warning instead of destroying
  the item.
- Effects are cosmetic. Gameplay belongs in a use handler or a gameplay capability, so a client that
  renders an effect differently can never desync the session.

## Status

The effect system compiles and is wired in, but like everything else at this stage it has not been
validated in a running game (Milestone 0). Expect to tune scale and placement on first sight.
