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
        private readonly HashSet<string> _vfxFilters = new HashSet<string>();
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

        /// <summary>
        /// List loaded particle-system objects whose name contains <paramref name="filter"/>
        /// (case-insensitive; empty = all). This is how you find a vanilla effect to reuse — search
        /// "lightning", "frost", "ice", "smoke", "spark" — and then feed the names straight into
        /// <c>ParticleEffect.FromGameObject(id, prefabName, childName)</c>. Returns the names it logged.
        /// </summary>
        public List<string> ListVfx(string filter = "", int limit = 200)
        {
            var found = new List<string>();
            try
            {
                var systems = Resources.FindObjectsOfTypeAll<ParticleSystem>();
                foreach (var ps in systems)
                {
                    if (ps == null) continue;
                    string name = ps.gameObject.name;
                    if (filter.Length > 0 && name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                    // Report the owning prefab root too — that is what FromGameObject needs.
                    var root = ps.transform;
                    while (root.parent != null) root = root.parent;
                    string entry = root.name == name ? name : $"{root.name} / {name}";
                    if (!found.Contains(entry)) found.Add(entry);
                    if (found.Count >= limit) break;
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[POGContentLib.Probe] ListVfx failed: {ex.Message}");
                return found;
            }

            var sb = new System.Text.StringBuilder(
                $"[POGContentLib.Probe] VFX matching '{filter}' ({found.Count}) — use as ParticleEffect.FromGameObject(id, ROOT, CHILD):");
            foreach (var f in found) sb.Append("\n    ").Append(f);
            MelonLogger.Msg(sb.ToString());
            return found;
        }

        /// <summary>Retry queued probes; called by the Items plugin on every scene load.</summary>
        internal void RetryPending()
        {
            LoadProbeListOnce();

            // "vfx:<filter>" lines list matching effects once the scene has loaded them.
            if (_vfxFilters.Count > 0)
            {
                foreach (var filter in new List<string>(_vfxFilters))
                {
                    var hits = ListVfx(filter);
                    if (hits.Count > 0) _vfxFilters.Remove(filter);
                }
            }

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
                    if (line.StartsWith("vfx:", System.StringComparison.OrdinalIgnoreCase))
                    {
                        _vfxFilters.Add(line.Substring(4).Trim());
                        continue;
                    }
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
