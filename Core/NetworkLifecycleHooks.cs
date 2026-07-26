using System;
using HarmonyLib;
using MelonLoader;
using Unity.Netcode;

namespace POGContentLib.Core
{
    /// <summary>Which side of the session is starting (drives the parity step).</summary>
    internal enum SessionRole { Host, Client }

    /// <summary>
    /// NGO lifecycle Harmony hooks. A prefix on Start* is the only correct window to register
    /// mod prefabs (ForceSamePrefabs=true). A postfix on Shutdown resets session-scoped state
    /// so a lobby rejoin re-registers content. The same window drives the multiplayer parity step:
    /// the host advertises its manifest, a joining client reads and compares the host's.
    /// </summary>
    internal static class NetworkLifecycleHooks
    {
        internal static void OnBeforeSessionStart(SessionRole role)
        {
            if (!CoreServices.Ready) return;
            CoreServices.Network.RegisterAll();
            PluginRegistry.Dispatch(PluginStage.BeforeSessionStart);

            // Multiplayer parity (ForceSamePrefabs): host advertises, client compares. Guarded so a
            // Steam/interop shape change degrades to a log line instead of breaking session start.
            try
            {
                if (role == SessionRole.Host) ParityService.AdvertiseAsHost();
                else ParityService.CheckAsClient();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[POGContentLib] Parity step failed ({role}): {ex.Message}");
            }
        }

        internal static void OnSessionStop()
        {
            if (!CoreServices.Ready) return;
            CoreServices.Network.Reset();
            PluginRegistry.Dispatch(PluginStage.SessionStop);
        }
    }

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.StartHost))]
    internal static class Patch_StartHost
    {
        static void Prefix() => NetworkLifecycleHooks.OnBeforeSessionStart(SessionRole.Host);
    }

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.StartServer))]
    internal static class Patch_StartServer
    {
        static void Prefix() => NetworkLifecycleHooks.OnBeforeSessionStart(SessionRole.Host);
    }

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.StartClient))]
    internal static class Patch_StartClient
    {
        static void Prefix() => NetworkLifecycleHooks.OnBeforeSessionStart(SessionRole.Client);
    }

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.Shutdown))]
    internal static class Patch_Shutdown
    {
        static void Postfix() => NetworkLifecycleHooks.OnSessionStop();
    }
}
