using HarmonyLib;
using MelonLoader;
using Unity.Netcode;

namespace POGContentLib.Core
{
    /// <summary>
    /// NGO lifecycle Harmony hooks. A prefix on Start* is the only correct window to register
    /// mod prefabs (ForceSamePrefabs=true). A postfix on Shutdown resets session-scoped state
    /// so a lobby rejoin re-registers content.
    /// </summary>
    internal static class NetworkLifecycleHooks
    {
        internal static void OnBeforeSessionStart()
        {
            if (!CoreServices.Ready) return;
            CoreServices.Network.RegisterAll();
            PluginRegistry.Dispatch(PluginStage.BeforeSessionStart);
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
        static void Prefix() => NetworkLifecycleHooks.OnBeforeSessionStart();
    }

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.StartServer))]
    internal static class Patch_StartServer
    {
        static void Prefix() => NetworkLifecycleHooks.OnBeforeSessionStart();
    }

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.StartClient))]
    internal static class Patch_StartClient
    {
        static void Prefix() => NetworkLifecycleHooks.OnBeforeSessionStart();
    }

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.Shutdown))]
    internal static class Patch_Shutdown
    {
        static void Postfix() => NetworkLifecycleHooks.OnSessionStop();
    }
}
