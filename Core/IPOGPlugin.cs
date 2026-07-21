using System;

namespace POGContentLib.Core
{
    /// <summary>
    /// Lifecycle stages bound to real MelonLoader + NGO events, not a one-shot init.
    /// A stage may run MORE THAN ONCE (e.g. BeforeSessionStart fires on every
    /// StartHost/StartClient, including lobby rejoin), so systems must be idempotent.
    /// </summary>
    public enum PluginStage
    {
        /// <summary>OnInitializeMelon: register services, use-handlers, data contracts.
        /// Il2Cpp is ready, but the game scene is not loaded yet — no game prefabs in memory.</summary>
        Boot,

        /// <summary>OnSceneWasLoaded: game shell prefabs are now in memory — build templates.
        /// Idempotent: only build what has not been built yet.</summary>
        SceneLoaded,

        /// <summary>Harmony prefix before NetworkManager.StartHost/StartClient/StartServer:
        /// the ONLY correct window to register network prefabs (ForceSamePrefabs=true forbids
        /// AddNetworkPrefab after networking starts) and to inject content into loot tables.</summary>
        BeforeSessionStart,

        /// <summary>NetworkManager.Shutdown: NGO runtime prefabs are session-scoped and dropped
        /// on shutdown — reset flags so registration re-runs on the next session start.</summary>
        SessionStop
    }

    /// <summary>
    /// A modular POGContentLib plugin (Bevy-style Plugin). Each domain (Items, Mobs,
    /// Levels…) implements it and contributes its systems to the graph per stage.
    /// </summary>
    public interface IPOGPlugin
    {
        /// <summary>Unique plugin name (e.g. "Items").</summary>
        string Name { get; }

        /// <summary>Semantic version of the plugin.</summary>
        string Version { get; }

        /// <summary>
        /// Registers systems and resources into the graph. Called once during Boot.
        /// Must NOT touch runtime state here — only graph.AddSystem/AddResource.
        /// </summary>
        void Build(PluginGraph graph);
    }
}
