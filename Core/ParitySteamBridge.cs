using System;
using HarmonyLib;
using Il2CppGame.Platform;
using MelonLoader;

namespace POGContentLib.Core
{
    /// <summary>
    /// Access to the current Steam lobby's metadata — the channel the parity layer uses to advertise
    /// and compare content sets before a join.
    ///
    /// Getting the lobby is not obvious: <c>SteamRuntimeManager.Lobby</c> is an INSTANCE property on a
    /// plain (non-Unity) class with no singleton accessor, so there is nothing to look it up from
    /// (runtime-verified: reading it statically threw "Non-static method requires a target", and it is
    /// not a MonoBehaviour, so FindObjectOfType cannot help either). Instead we capture the instance
    /// from the game's own lobby callbacks via Harmony and read the live property off it afterwards.
    /// </summary>
    internal static class ParitySteamBridge
    {
        private static SteamRuntimeManager _manager;
        private static bool _warned;

        /// <summary>Remember the manager instance handed to us by a game callback.</summary>
        internal static void CaptureManager(SteamRuntimeManager manager)
        {
            if (manager == null) return;
            _manager = manager;
        }

        /// <summary>Whether a lobby is currently available to read/write.</summary>
        internal static bool HasLobby => _manager != null && _manager.Lobby.HasValue;

        /// <summary>Write a key/value onto the current lobby. False when there is no lobby yet.</summary>
        public static bool TrySetLobbyData(string key, string value)
        {
            try
            {
                if (_manager == null) { WarnOnce("no SteamRuntimeManager captured yet (no lobby callback has fired)."); return false; }
                var lobby = _manager.Lobby;
                if (!lobby.HasValue) return false;
                lobby.Value.SetData(key, value);
                return true;
            }
            catch (Exception ex)
            {
                WarnOnce($"TrySetLobbyData failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>Read a key from the current lobby. Null when there is no lobby; "" when unset.</summary>
        public static string TryGetLobbyData(string key)
        {
            try
            {
                if (_manager == null) { WarnOnce("no SteamRuntimeManager captured yet (no lobby callback has fired)."); return null; }
                var lobby = _manager.Lobby;
                if (!lobby.HasValue) return null;
                return lobby.Value.GetData(key);
            }
            catch (Exception ex)
            {
                WarnOnce($"TryGetLobbyData failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static void WarnOnce(string msg)
        {
            if (_warned) return;
            _warned = true;
            MelonLogger.Warning($"[POGContentLib] Parity/Steam bridge unavailable — {msg} " +
                "Parity detection via lobby metadata is off this session (the game's own connect-time " +
                "prefab check still applies).");
        }
    }

    /// <summary>
    /// Capture the SteamRuntimeManager instance from the game's lobby callbacks. Patching several of
    /// them covers both roles: the host gets OnSteamLobbyGameCreated, a joiner gets OnSteamLobbyEntered,
    /// and OnSteamLobbyDataChanged catches any later change. Postfixes only — we never alter behaviour.
    /// </summary>
    [HarmonyPatch(typeof(SteamRuntimeManager), GameNames.Steam.OnSteamLobbyEntered)]
    internal static class Patch_SteamLobbyEntered
    {
        static void Postfix(SteamRuntimeManager __instance) => ParitySteamBridge.CaptureManager(__instance);
    }

    [HarmonyPatch(typeof(SteamRuntimeManager), GameNames.Steam.OnSteamLobbyGameCreated)]
    internal static class Patch_SteamLobbyGameCreated
    {
        static void Postfix(SteamRuntimeManager __instance) => ParitySteamBridge.CaptureManager(__instance);
    }

    [HarmonyPatch(typeof(SteamRuntimeManager), GameNames.Steam.OnSteamLobbyDataChanged)]
    internal static class Patch_SteamLobbyDataChanged
    {
        static void Postfix(SteamRuntimeManager __instance) => ParitySteamBridge.CaptureManager(__instance);
    }
}
