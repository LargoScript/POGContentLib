using System;
using System.Collections.Generic;
using MelonLoader;

namespace POGContentLib.Core
{
    /// <summary>
    /// Registry of domain plugins + stage driver. The entry point (Plugin.cs) calls Boot()
    /// once, then Dispatch(stage) from the relevant MelonLoader/NGO events.
    /// </summary>
    public static class PluginRegistry
    {
        private static readonly List<IPOGPlugin> _plugins = new();
        private static PluginGraph _graph;
        private static bool _booted;

        /// <summary>Add a plugin. Only before Boot().</summary>
        public static void Add(IPOGPlugin plugin)
        {
            if (plugin == null) return;
            if (_booted)
            {
                MelonLogger.Warning($"[POGContentLib] Cannot add plugin '{plugin.Name}' after Boot.");
                return;
            }
            _plugins.Add(plugin);
            MelonLogger.Msg($"[POGContentLib] Registered plugin: {plugin.Name} v{plugin.Version}");
        }

        /// <summary>Build the graph from all plugins and run the Boot stage.</summary>
        public static void Boot()
        {
            if (_booted) return;
            _booted = true;
            _graph = new PluginGraph();

            foreach (var plugin in _plugins)
            {
                try { plugin.Build(_graph); }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[POGContentLib] Plugin '{plugin.Name}' Build() threw: {ex}");
                }
            }

            _graph.RunStage(PluginStage.Boot);
            MelonLogger.Msg($"[POGContentLib] Boot complete ({_plugins.Count} plugin(s)).");
        }

        /// <summary>Run a lifecycle stage (SceneLoaded / BeforeSessionStart / SessionStop).</summary>
        public static void Dispatch(PluginStage stage)
        {
            if (!_booted || _graph == null) return;
            _graph.RunStage(stage);
        }

        /// <summary>The graph (for external resource access).</summary>
        public static PluginGraph Graph => _graph;
    }
}
