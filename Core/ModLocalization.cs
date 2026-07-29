using System.Collections.Generic;
using HarmonyLib;
using Il2CppI2.Loc;

namespace POGContentLib.Core
{
    /// <summary>
    /// Tooltip/name fallback for mod content. The game uses I2 Localization; rather than
    /// registering real I2 terms, we intercept LocalizationManager.GetTranslation and return
    /// our own strings for keys the content pack registered.
    /// </summary>
    public static class ModLocalization
    {
        private static readonly Dictionary<string, string> Terms = new();

        /// <summary>Register a term → value (e.g. a tooltip key and its text).</summary>
        public static void Register(string key, string value)
        {
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                Terms[key] = value;
        }

        public static bool TryGet(string key, out string value) => Terms.TryGetValue(key, out value);
    }

    [HarmonyPatch(typeof(LocalizationManager), GameNames.Methods.LocalizationManager_GetTranslation)]
    internal static class Patch_GetTranslation
    {
        // The injected parameter name must match the GAME's parameter EXACTLY, including case:
        // GetTranslation(string Term, ...) — capital T. A mismatch is not a compile error, it is a
        // Harmony "IL Compile Error" at patch time that aborts the WHOLE PatchAll batch.
        static void Postfix(string Term, ref string __result)
        {
            if (string.IsNullOrEmpty(Term)) return;
            if (ModLocalization.TryGet(Term, out var value) && !string.IsNullOrEmpty(value))
                __result = value;
        }
    }
}
