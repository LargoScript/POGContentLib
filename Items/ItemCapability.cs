using Il2Cpp;
using Il2CppItems;
using MelonLoader;
using POGContentLib.Core;
using UnityEngine;

namespace POGContentLib.Items
{
    /// <summary>
    /// A declarative item capability (EXPERIMENTAL, v0.2). In this game an "effect" (eat / weapon /
    /// throw / …) is NOT a subclass of InventoryItem — it is a sibling <c>ActiveItem_*</c> /
    /// <c>PassiveItem_*</c> component on the same GameObject (see _research/ITEM_ANATOMY.md §2). This
    /// API lets a content pack DECLARE such a capability on a <see cref="ModItemDefinition"/>; the Lib
    /// attaches the component and sets the SCALAR fields it knows are safe.
    ///
    /// RUNTIME-TODO (Milestone 0): a bare shell has none of the serialized references these components
    /// normally ship with (VFX, sounds, curves, and NetworkVariables that init at spawn). The scalar
    /// effect logic should work; missing refs may warn or no-op. Attaching NetworkBehaviour components
    /// also changes the NetworkObject's behaviour list — every peer builds identically (same def, same
    /// order) so it should stay in sync under ForceSamePrefabs, but confirm in the 2-player test.
    /// </summary>
    public abstract class ItemCapability
    {
        /// <summary>Component type name, for logs.</summary>
        public abstract string Name { get; }

        /// <summary>Attach and configure the capability on the item. Called host-and-client at build.</summary>
        internal abstract void Attach(InventoryItem item);

        /// <summary>Get the capability component, adding it if absent.</summary>
        protected static T AddOrGet<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }
    }

    /// <summary>Edible: restores health/stamina when eaten (component <c>ActiveItem_Eat</c>).</summary>
    public sealed class EatCapability : ItemCapability
    {
        public float HealthOnEating = 0f;
        public float DamageOnEating = 0f;
        public float StaminaRefill = 0f;
        public float MinStaminaBonus = 0f;
        public float MaxStaminaBonus = 0f;
        public float StomachFullTime = 0f;
        public bool UseStaminaMin = false;

        public override string Name => "ActiveItem_Eat";

        internal override void Attach(InventoryItem item)
        {
            var eat = AddOrGet<ActiveItem_Eat>(item.gameObject);
            eat.m_healthOnEating = HealthOnEating;
            eat.m_damageOnEating = DamageOnEating;
            eat.m_staminaRefill = StaminaRefill;
            eat.m_minStaminaBonus = MinStaminaBonus;
            eat.m_maxStaminaBonus = MaxStaminaBonus;
            eat.m_stomachFullTime = StomachFullTime;
            eat.m_useStaminaMin = UseStaminaMin;
            // m_eatVfx (VFX) is left null — a bare shell has none; EatVfxRpc may no-op (RUNTIME-TODO).
        }
    }

    /// <summary>Melee weapon: damages on hit and loses durability (component <c>ActiveItem_MeleeWeapon</c>).</summary>
    public sealed class MeleeWeaponCapability : ItemCapability
    {
        public int MaxDurability = 100;
        public int DurabilityLossOnEntityHit = 1;
        public int DurabilityLossOnDestructibleHit = 1;
        public float HitCooldown = 0.5f;

        public override string Name => "ActiveItem_MeleeWeapon";

        internal override void Attach(InventoryItem item)
        {
            var w = AddOrGet<ActiveItem_MeleeWeapon>(item.gameObject);
            w.m_maxDurability = MaxDurability;
            w.m_durabilityLossOnEntityHit = DurabilityLossOnEntityHit;
            w.m_durabilityLossOnDestructibleHit = DurabilityLossOnDestructibleHit;
            w.m_meleeHitCooldown = HitCooldown;
            // m_currentDurability is a NetworkVariable<int> that initializes at spawn; seeding it to
            // MaxDurability belongs in an OnNetworkSpawn step, not here (RUNTIME-TODO). Hit VFX/sounds
            // are also unset on a bare shell.
        }
    }

    /// <summary>
    /// Glow / pulse (EXPERIMENTAL, v0.2). A glowing, pulsing item — the look the SpeakingStone has —
    /// is NOT a field on InventoryItem: it is a child <c>Light</c> plus emissive materials, animated
    /// by a pulse driver (item anatomy §9). This capability rebuilds that setup declaratively:
    ///
    ///   • a child GameObject with a <c>Light</c> (Colour / Intensity / Range) — the light half;
    ///   • an optional emissive tint on the item's renderers — the material half;
    ///   • an optional pulse via the game's own <c>LightFlicker</c> (DOTween: Strength / Duration /
    ///     Vibrato / Randomness), the same component the game uses for flickering lights.
    ///
    /// The DEFAULTS here are deliberately neutral, NOT copied from any vanilla item: the real numbers
    /// live in prefab serialized data and can only be read at runtime. Use <c>VisualProbe</c>
    /// (Content.Diagnostics.ProbeItem("Item_SpeakingStone")) to capture the actual values in-game and
    /// then set them explicitly here.
    ///
    /// RUNTIME-TODO (Milestone 0): under HDRP, <c>Light.intensity</c> is not the photometric intensity
    /// (HDAdditionalLightData owns lumens/lux), so a light created this way may read dimmer/brighter
    /// than a vanilla one — the probe reports both so the offset can be calibrated.
    /// </summary>
    public sealed class GlowCapability : ItemCapability
    {
        /// <summary>Light colour.</summary>
        public Color Colour = Color.white;
        /// <summary>Light intensity (see the HDRP note above).</summary>
        public float Intensity = 1f;
        /// <summary>Light range in metres.</summary>
        public float Range = 4f;
        /// <summary>Local offset of the light inside the item.</summary>
        public Vector3 LocalOffset = Vector3.zero;

        /// <summary>Also tint the item's materials emissive (the "the mesh itself glows" half).</summary>
        public bool EmissiveMaterial = false;
        /// <summary>Emissive colour; falls back to <see cref="Colour"/> when unset.</summary>
        public Color? EmissiveColour = null;

        /// <summary>Attach the game's LightFlicker so the light pulses.</summary>
        public bool Pulse = false;
        /// <summary>Pulse amplitude (LightFlicker.m_strength).</summary>
        public float PulseStrength = 1f;
        /// <summary>Pulse period in seconds (LightFlicker.m_duration).</summary>
        public float PulseDuration = 1f;
        /// <summary>Oscillations per pulse (LightFlicker.m_vibrato).</summary>
        public int PulseVibrato = 10;
        /// <summary>Pulse jitter 0..1 (LightFlicker.m_randomness).</summary>
        public float PulseRandomness = 0f;

        public override string Name => "Glow (Light + LightFlicker)";

        internal override void Attach(InventoryItem item)
        {
            var go = item.gameObject;

            // Reuse our own light child if the item is rebuilt, so we never stack duplicates.
            var existing = go.transform.Find(GameNames.ModObjects.GlowLight);
            GameObject lightGo = existing != null
                ? existing.gameObject
                : new GameObject(GameNames.ModObjects.GlowLight);
            if (existing == null)
            {
                lightGo.transform.SetParent(go.transform, false);
                lightGo.transform.localPosition = LocalOffset;
            }

            var light = AddOrGet<Light>(lightGo);
            light.type = LightType.Point;
            light.color = Colour;
            light.intensity = Intensity;
            light.range = Range;

            if (Pulse)
            {
                var flicker = AddOrGet<LightFlicker>(lightGo);
                flicker.m_light = light;
                flicker.m_strength = PulseStrength;
                flicker.m_duration = PulseDuration;
                flicker.m_vibrato = PulseVibrato;
                flicker.m_randomness = PulseRandomness;
            }

            if (EmissiveMaterial)
                ApplyEmissive(go, EmissiveColour ?? Colour);
        }

        /// <summary>Set an emissive colour on the item's visible renderers (material copies, HDRP first).</summary>
        private static void ApplyEmissive(GameObject root, Color colour)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var copy = new Material(mats[i]);
                    if (copy.HasProperty(GameNames.Shader.EmissiveColor))
                        copy.SetColor(GameNames.Shader.EmissiveColor, colour);
                    else if (copy.HasProperty(GameNames.Shader.EmissionColor))
                    {
                        copy.SetColor(GameNames.Shader.EmissionColor, colour);
                        copy.EnableKeyword(GameNames.Shader.EmissionKeyword);
                    }
                    else continue;
                    mats[i] = copy;
                }
                r.materials = mats;
            }
        }
    }

    /// <summary>Throwable: can be thrown for damage, has its own "health" (component <c>ActiveItem_Throwable</c>).</summary>
    public sealed class ThrowableCapability : ItemCapability
    {
        public int HealthMax = 1;
        public int HealthMin = 1;
        public float BounceFactor = 0f;

        public override string Name => "ActiveItem_Throwable";

        internal override void Attach(InventoryItem item)
        {
            var t = AddOrGet<ActiveItem_Throwable>(item.gameObject);
            t.m_healthMax = HealthMax;
            t.m_healthMin = HealthMin;
            t.m_bounceFactor = BounceFactor;
            // m_damageType, hit VFX/sounds and the current-health NetworkVariable are left at defaults.
        }
    }
}
