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
        static void Postfix(string term, ref string __result)
        {
            if (string.IsNullOrEmpty(term)) return;
            if (ModLocalization.TryGet(term, out var value) && !string.IsNullOrEmpty(value))
                __result = value;
        }
    }
}
