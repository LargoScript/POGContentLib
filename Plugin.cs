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

            // 2) Compatibility probe — verify the game members we bind to exist on this build,
            //    BEFORE PatchAll (so a game update surfaces as a clear log line, not a silent break).
            CompatibilityProbe.Check();

            // 3) Shared core services (reachable from the static Harmony patches).
            CoreServices.Init();

            // 4) Built-in domain plugins (each is a separate module).
            PluginRegistry.Add(new ItemsPlugin());
            // TODO(v2): PluginRegistry.Add(new MobsPlugin());
            // TODO(v3): PluginRegistry.Add(new LevelsPlugin());

            // 5) Build the graph and run Boot (register services/handlers).
            PluginRegistry.Boot();

            // 6) Harmony: NGO lifecycle hooks (Start*/Shutdown) + the reward-chest save guard.
            //    Patch each class SEPARATELY: PatchAll aborts the whole batch on the first failure,
            //    which once cost us every patch in the assembly because of one bad parameter name.
            //    Isolating them means a single incompatible patch degrades that one feature only.
            PatchEachClassIndependently();

            MelonLogger.Msg("[POGContentLib] Boot done. Waiting for scene + session.");
        }

        /// <summary>
        /// Apply every [HarmonyPatch] class in this assembly one at a time, so a patch that the
        /// current game build rejects cannot take the others down with it. Logs exactly which class
        /// failed and why — the actionable form of "the game updated".
        /// </summary>
        private void PatchEachClassIndependently()
        {
            int ok = 0, failed = 0;
            foreach (var type in typeof(Plugin).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                try
                {
                    new PatchClassProcessor(HarmonyInstance, type).Patch();
                    ok++;
                }
                catch (Exception ex)
                {
                    failed++;
                    MelonLogger.Error($"[POGContentLib] Harmony patch FAILED for {type.Name}: " +
                                      $"{ex.InnerException?.Message ?? ex.Message}");
                }
            }

            if (failed == 0) MelonLogger.Msg($"[POGContentLib] Harmony: {ok} patch class(es) applied.");
            else MelonLogger.Warning($"[POGContentLib] Harmony: {ok} applied, {failed} FAILED (those features are off; the rest work).");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            // Game shell prefabs are now in memory — build any templates not built yet.
            PluginRegistry.Dispatch(PluginStage.SceneLoaded);
        }
    }
}
