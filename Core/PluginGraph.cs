using System;
using System.Collections.Generic;
using MelonLoader;

namespace POGContentLib.Core
{
    /// <summary>
    /// Graph of plugin systems and resources (Bevy-style App/World).
    /// Systems are grouped by lifecycle stage; resources are a typed service locator
    /// shared between plugins.
    /// </summary>
    public class PluginGraph
    {
        private readonly Dictionary<PluginStage, List<NamedSystem>> _systems = new();
        private readonly Dictionary<Type, object> _resources = new();

        private struct NamedSystem
        {
            public string Owner;
            public Action Run;
        }

        public PluginGraph()
        {
            foreach (PluginStage stage in Enum.GetValues(typeof(PluginStage)))
                _systems[stage] = new List<NamedSystem>();
        }

        /// <summary>Add a system to a stage. owner = plugin name (for logs / fault isolation).</summary>
        public void AddSystem(PluginStage stage, string owner, Action system)
        {
            if (system == null) return;
            _systems[stage].Add(new NamedSystem { Owner = owner, Run = system });
        }

        /// <summary>Register a resource (singleton service) available to plugins.</summary>
        public void AddResource<T>(T resource) where T : class
        {
            _resources[typeof(T)] = resource;
        }

        /// <summary>Get a previously registered resource (or null).</summary>
        public T GetResource<T>() where T : class
        {
            return _resources.TryGetValue(typeof(T), out var r) ? r as T : null;
        }

        /// <summary>
        /// Run all systems of a stage. A single system failure is isolated (log + continue)
        /// so one broken content mod cannot take down the rest.
        /// </summary>
        public void RunStage(PluginStage stage)
        {
            if (!_systems.TryGetValue(stage, out var systems)) return;
            foreach (var sys in systems)
            {
                try { sys.Run(); }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[POGContentLib] {sys.Owner} threw in stage {stage}: {ex}");
                }
            }
        }
    }
}
