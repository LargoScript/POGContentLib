using Il2Cpp;
using Il2CppItems;
using MelonLoader;
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
