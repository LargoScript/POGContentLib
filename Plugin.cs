using System;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using POGContentLib.Core;
using POGContentLib.Items;

[assembly: MelonInfo(typeof(POGContentLib.Plugin), "POGContentLib", "0.1.0", "POG Community")]
[assembly: MelonGame(null, "Pit of Goblin")]
// Load earlier than content mods so their OnInitializeMelon can call Content.*.
[assembly: MelonPriority(-1000)]

namespace POGContentLib
{
    /// <summary>
    /// POGContentLib entry point. This is a MelonMod (NOT a MelonPlugin): working with Il2Cpp
    /// types, NetworkManager and component injection is only possible after the game has
    /// initialized — i.e. in OnInitializeMelon and later events, not in loader pre-init.
    ///
    /// Drives PluginRegistry stages from real events:
    ///   OnInitializeMelon        -> Boot
    ///   OnSceneWasLoaded         -> SceneLoaded
    ///   (Harmony on Start*)      -> BeforeSessionStart   [in NetworkLifecycleHooks]
    ///   (Harmony on Shutdown)    -> SessionStop          [in NetworkLifecycleHooks]
    /// </summary>
    public class Plugin : MelonMod
    {
        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("[POGContentLib] Initializing (MelonMod)...");

            // 1) Injected Il2Cpp types — BEFORE any AddComponent<T>.
            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<ModItemHandle>();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[POGContentLib] ClassInjector failed: {ex.Message}");
            }

            // 2) Shared core services (reachable from the static Harmony patches).
            CoreServices.Init();

            // 3) Built-in domain plugins (each is a separate module).
            PluginRegistry.Add(new ItemsPlugin());
            // TODO(v2): PluginRegistry.Add(new MobsPlugin());
            // TODO(v3): PluginRegistry.Add(new LevelsPlugin());

            // 4) Build the graph and run Boot (register services/handlers).
            PluginRegistry.Boot();

            // 5) Harmony: NGO lifecycle hooks (Start*/Shutdown) + the reward-chest save guard.
            //    Use the melon's built-in HarmonyInstance.
            try
            {
                HarmonyInstance.PatchAll(typeof(Plugin).Assembly);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[POGContentLib] Harmony PatchAll failed: {ex.Message}");
            }

            MelonLogger.Msg("[POGContentLib] Boot done. Waiting for scene + session.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            // Game shell prefabs are now in memory — build any templates not built yet.
            PluginRegistry.Dispatch(PluginStage.SceneLoaded);
        }
    }
}
