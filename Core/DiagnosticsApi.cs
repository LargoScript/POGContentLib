using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace POGContentLib.Core
{
    /// <summary>
    /// Diagnostics facade (Content.Diagnostics). Prefab-serialized visual data (light colour and
    /// intensity, flicker settings, emissive materials) cannot be read by any static tool, so this
    /// exposes the runtime <see cref="VisualProbe"/> — the way to capture the real numbers to feed
    /// into a <c>GlowCapability</c>.
    ///
    /// Two ways to use it:
    ///  1. From a content pack: <c>Content.Diagnostics.ProbeItem("Item_SpeakingStone")</c>.
    ///  2. Without writing code: create <c>UserData/pog_probe.txt</c> next to the game, one prefab
    ///     name per line. Each is probed once, on scene load, as soon as it is in memory.
    /// </summary>
    public sealed class DiagnosticsApi
    {
        /// <summary>Probe file: one prefab name per line ('#' starts a comment).</summary>
        public const string ProbeListFile = "UserData/pog_probe.txt";

        private readonly HashSet<string> _pending = new HashSet<string>();
        private readonly HashSet<string> _done = new HashSet<string>();
        private bool _listLoaded;

        /// <summary>
        /// Probe a loaded item prefab now and log its visual setup. If it is not in memory yet, it is
        /// queued and retried on later scene loads. Returns true if it was probed immediately.
        /// </summary>
        public bool ProbeItem(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return false;
            if (_done.Contains(prefabName)) return true;
            if (VisualProbe.ProbeItemPrefab(prefabName))
            {
                _done.Add(prefabName);
                _pending.Remove(prefabName);
                return true;
            }
            _pending.Add(prefabName);
            return false;
        }

        /// <summary>Probe any live GameObject hierarchy (no queueing).</summary>
        public void ProbeObject(GameObject go, string label) => VisualProbe.ProbeGameObject(go, label);

        /// <summary>Retry queued probes; called by the Items plugin on every scene load.</summary>
        internal void RetryPending()
        {
            LoadProbeListOnce();
            if (_pending.Count == 0) return;
            foreach (var name in new List<string>(_pending))
            {
                if (!VisualProbe.ProbeItemPrefab(name)) continue;
                _done.Add(name);
                _pending.Remove(name);
            }
        }

        /// <summary>Read the optional probe list file once (no-code path for capturing values).</summary>
        private void LoadProbeListOnce()
        {
            if (_listLoaded) return;
            _listLoaded = true;
            try
            {
                if (!System.IO.File.Exists(ProbeListFile)) return;
                foreach (var raw in System.IO.File.ReadAllLines(ProbeListFile))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    if (!_done.Contains(line)) _pending.Add(line);
                }
                MelonLogger.Msg($"[POGContentLib.Probe] Probe list loaded from {ProbeListFile} ({_pending.Count} entry/entries).");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[POGContentLib.Probe] Could not read {ProbeListFile}: {ex.Message}");
            }
        }
    }
}
