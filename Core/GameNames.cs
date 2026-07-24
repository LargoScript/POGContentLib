namespace POGContentLib.Core
{
    /// <summary>
    /// Single source of truth for every GAME-FACING string literal the framework binds to —
    /// prefab names, shader properties, component/VFX names, and patched method names. Game
    /// updates that rename assets/members break exactly these strings; keeping them in one file
    /// makes a version bump a one-file edit instead of a hunt through the code (anti-hardcode).
    /// The <see cref="CompatibilityProbe"/> checks the members here exist on the current game build.
    /// </summary>
    public static class GameNames
    {
        /// <summary>Vanilla shell prefabs cloned as item templates (ShellFactory).</summary>
        public static class Shells
        {
            public const string Diamond = "Item_Diamond";   // default: InventoryItem+NetworkObject+Rigidbody, no VoIP/VFX
            public const string Coin = "Item_Coin";
            public const string GemEmerald = "Item_Gem_Emerald";
        }

        /// <summary>HDRP material color properties used when tinting a reskin (ItemVisuals).</summary>
        public static class Shader
        {
            public const string BaseColor = "_BaseColor"; // HDRP/Lit primary
            public const string Color = "_Color";         // legacy fallback
        }

        /// <summary>Component type names stripped when a SpeakingStone mesh is used as a reskin donor.</summary>
        public static class VoiceComponents
        {
            public const string SpeakingStone = "SpeakingStone";
            public const string VoiceProximity = "VoiceProximity";
        }

        /// <summary>SpeakingStone VFX/outline object-name fragments hidden on reskin (ItemVisuals).</summary>
        public static readonly string[] SpeakingStoneVfx =
        {
            "VFX_SpeakingStone", "SpeakingStoneOutline", "SpeakingStoneHolder", "SpeakingStoneParticles",
        };

        /// <summary>Our own injected visual child name (not a game asset — stable).</summary>
        public const string ModVisualChild = "ModItemVisual";

        /// <summary>
        /// Game methods the framework Harmony-patches, by name. Used inside [HarmonyPatch] attributes,
        /// so these MUST stay <c>const</c>. A rename here (or upstream) makes PatchAll throw — the
        /// probe flags it up-front so the failure is diagnosable, not a mid-game surprise.
        /// </summary>
        public static class Methods
        {
            public const string LocalizationManager_GetTranslation = "GetTranslation";
            public const string SpeakingStone_Start = "Start";
            public const string SpeakingStone_SetEnabled = "SetEnabled";
            public const string InventoryItem_OnNetworkSpawn = "OnNetworkSpawn";
            public const string InventoryItem_Interact = "Interact";
            public const string ItemContainer_FromContainerData = "FromContainerData";
            // NetworkManager Start*/Shutdown are referenced via nameof() (compile-checked) at their call sites.
        }
    }
}
