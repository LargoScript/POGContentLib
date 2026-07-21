using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace POGContentLib.Core
{
    /// <summary>
    /// Save/load safety. Confirmed by decompilation (CONTENT_FRAMEWORK_RESEARCH.md §2.5):
    ///   • StorageVessel.LoadGame silently skips an unknown ObjectHash — storage/inventory
    ///     are safe, no guard needed.
    ///   • ItemContainer.FromContainerData (reward chests) THROWS NRE on an unknown hash, and
    ///     LootRewardSpawner.LoadRewardChests calls it — one bad hash aborts restoration of
    ///     ALL reward chests (vanilla ones too).
    ///
    /// This finalizer is a safety net: it catches the exception in FromContainerData, logs it,
    /// and returns control (one chest is lost instead of all). Full fix (pre-validate hashes +
    /// a companion mod-state file) is runtime-Milestone work.
    /// </summary>
    [HarmonyPatch(typeof(ItemContainer), nameof(ItemContainer.FromContainerData))]
    internal static class Patch_FromContainerData
    {
        static Exception Finalizer(Exception __exception)
        {
            if (__exception == null) return null;
            MelonLogger.Warning(
                "[POGContentLib] SaveGuard: FromContainerData threw (likely unknown item hash — " +
                $"missing content pack?). Skipping this reward chest. {__exception.GetType().Name}");
            return null; // swallow: don't let the exception abort LoadRewardChests entirely
        }
    }
}
