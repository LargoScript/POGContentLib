using System;
using System.Reflection;
using System.Text;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace POGContentLib.Core
{
    /// <summary>
    /// Runtime inspector for a vanilla item's VISUAL setup — lights, pulse/flicker, particles and
    /// emissive materials. Those values live in the prefab's SERIALIZED data, so they cannot be read
    /// from type metadata by any static tool; the only way to learn the real numbers (a stone's light
    /// colour, intensity, range, flicker strength…) is to inspect a live instance in-game.
    ///
    /// Use it to capture reference numbers for <c>GlowCapability</c>: run the game once, read the
    /// block it logs, then feed those values into a content pack. Read-only — it never mutates.
    /// </summary>
    public static class VisualProbe
    {
        /// <summary>HDRP stores real photometric intensity here; read reflectively (no HDRP reference).</summary>
        private const string HdLightDataType = "Il2CppUnityEngine.Rendering.HighDefinition.HDAdditionalLightData";

        /// <summary>
        /// Inspect a loaded item prefab by name (e.g. "Item_SpeakingStone") and log its visual setup.
        /// Returns false when the prefab is not in memory yet (try again after another scene load).
        /// </summary>
        public static bool ProbeItemPrefab(string prefabName)
        {
            var item = GameAssets.FindItemPrefab(prefabName);
            if (item == null)
            {
                MelonLogger.Warning($"[POGContentLib.Probe] Prefab not loaded (yet): {prefabName}");
                return false;
            }
            ProbeGameObject(item.gameObject, prefabName);
            return true;
        }

        /// <summary>Inspect any GameObject hierarchy and log every visual driver found on it.</summary>
        public static void ProbeGameObject(GameObject root, string label)
        {
            if (root == null) return;
            var sb = new StringBuilder();
            sb.Append($"[POGContentLib.Probe] VISUAL REPORT for '{label}'");

            DescribeHierarchy(root, sb);
            DescribeLights(root, sb);
            DescribeFlickers(root, sb);
            DescribeParticles(root, sb);
            DescribeEmissiveMaterials(root, sb);

            MelonLogger.Msg(sb.ToString());
        }

        private static void DescribeHierarchy(GameObject root, StringBuilder sb)
        {
            sb.Append("\n  -- child objects --");
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.gameObject == root) continue;
                sb.Append($"\n    {Path(t, root.transform)}{(t.gameObject.activeSelf ? "" : "  [inactive]")}");
            }
        }

        private static void DescribeLights(GameObject root, StringBuilder sb)
        {
            var lights = root.GetComponentsInChildren<Light>(true);
            sb.Append($"\n  -- lights ({lights.Length}) --");
            foreach (var l in lights)
            {
                if (l == null) continue;
                sb.Append($"\n    {Path(l.transform, root.transform)}: type={l.type} color=RGBA({l.color.r:0.###},{l.color.g:0.###}," +
                          $"{l.color.b:0.###},{l.color.a:0.###}) intensity={l.intensity:0.###} range={l.range:0.###} " +
                          $"shadows={l.shadows} enabled={l.enabled}");
                AppendHdrpIntensity(l, sb);
            }
        }

        /// <summary>HDRP's real intensity/unit lives on HDAdditionalLightData — reflect, don't reference.</summary>
        private static void AppendHdrpIntensity(Light l, StringBuilder sb)
        {
            try
            {
                var comps = l.gameObject.GetComponents<Component>();
                foreach (var c in comps)
                {
                    if (c == null) continue;
                    var t = c.GetType();
                    if (t.FullName != HdLightDataType) continue;
                    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    string Val(string n)
                    {
                        var p = t.GetProperty(n, F);
                        if (p != null && p.CanRead) return Convert.ToString(p.GetValue(c));
                        var f = t.GetField(n, F);
                        return f != null ? Convert.ToString(f.GetValue(c)) : "?";
                    }
                    sb.Append($"\n      HDRP: intensity={Val("intensity")} unit={Val("lightUnit")} range={Val("range")}");
                }
            }
            catch (Exception ex)
            {
                sb.Append($"\n      HDRP probe failed: {ex.GetType().Name}");
            }
        }

        private static void DescribeFlickers(GameObject root, StringBuilder sb)
        {
            var flickers = root.GetComponentsInChildren<LightFlicker>(true);
            sb.Append($"\n  -- LightFlicker ({flickers.Length}) --");
            foreach (var f in flickers)
            {
                if (f == null) continue;
                sb.Append($"\n    {Path(f.transform, root.transform)}: strength={f.m_strength:0.###} duration={f.m_duration:0.###} " +
                          $"vibrato={f.m_vibrato} randomness={f.m_randomness:0.###} hasLight={(f.m_light != null)}");
            }
        }

        private static void DescribeParticles(GameObject root, StringBuilder sb)
        {
            // MainModule's start* accessors are not projected onto the interop struct, so report the
            // system itself plus its renderer material — enough to identify which VFX drives a glow.
            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            sb.Append($"\n  -- particle systems ({systems.Length}) --");
            foreach (var ps in systems)
            {
                if (ps == null) continue;
                try
                {
                    var renderer = ps.GetComponent<Renderer>();
                    string mat = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "(no material)";
                    sb.Append($"\n    {Path(ps.transform, root.transform)}: playing={ps.isPlaying} material={mat}");
                }
                catch (Exception ex)
                {
                    sb.Append($"\n    {Path(ps.transform, root.transform)}: (read failed: {ex.GetType().Name})");
                }
            }
        }

        /// <summary>Emissive materials are the other half of a "glow" (next to the Light itself).</summary>
        private static void DescribeEmissiveMaterials(GameObject root, StringBuilder sb)
        {
            sb.Append("\n  -- emissive materials --");
            bool any = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                Material[] mats;
                try { mats = r.sharedMaterials; } catch { continue; }
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    try
                    {
                        if (!m.HasProperty(GameNames.Shader.EmissiveColor)) continue;
                        var e = m.GetColor(GameNames.Shader.EmissiveColor);
                        if (e.r <= 0f && e.g <= 0f && e.b <= 0f) continue;
                        any = true;
                        sb.Append($"\n    {Path(r.transform, root.transform)} [{m.name}]: emissive=RGBA({e.r:0.###},{e.g:0.###},{e.b:0.###},{e.a:0.###}) shader={m.shader?.name}");
                    }
                    catch { /* property access on odd shaders */ }
                }
            }
            if (!any) sb.Append("\n    (none with a non-black emissive colour)");
        }

        private static string Path(Transform t, Transform root)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null && p != root; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
    }
}
