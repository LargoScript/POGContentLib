using HarmonyLib;
using Il2Cpp;
using Il2CppGame.Gameplay;

namespace POGContentLib.Core
{
    /// <summary>
    /// When a content pack reskins with the SpeakingStone mesh, the donor's SpeakingStone
    /// component can run before we strip it (deferred Destroy timing). These prefixes stop
    /// SpeakingStone.Start/SetEnabled from executing on a mod item. Living in core means
    /// content packs no longer have to ship this cleanup themselves (a DX fix over the old mod).
    /// </summary>
    internal static class SpeakingStoneGuard
    {
        internal static bool IsOnModItem(SpeakingStone stone)
        {
            if (stone == null) return false;
            var item = stone.GetComponentInParent<InventoryItem>();
            return item != null && CoreServices.IsModItem(item);
        }
    }

    [HarmonyPatch(typeof(SpeakingStone), "Start")]
    internal static class Patch_SpeakingStone_Start
    {
        static bool Prefix(SpeakingStone __instance) => !SpeakingStoneGuard.IsOnModItem(__instance);
    }

    [HarmonyPatch(typeof(SpeakingStone), "SetEnabled")]
    internal static class Patch_SpeakingStone_SetEnabled
    {
        static bool Prefix(SpeakingStone __instance) => !SpeakingStoneGuard.IsOnModItem(__instance);
    }
}
