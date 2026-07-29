using System;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace POGContentLib.Core
{
    /// <summary>
    /// Reskin helpers: transplant a mesh child from a vanilla prefab, tint the jewel/mesh via
    /// HDRP material properties, and strip SpeakingStone voice/VFX (a common donor mesh).
    /// This is the no-AssetBundle visual tier.
    /// </summary>
    public static class ItemVisuals
    {
        /// <summary>
        /// Clone a named mesh child from a source prefab onto the target and disable the
        /// target's other renderers (so the reskin replaces the shell's look).
        /// </summary>
        public static void AttachGameMesh(GameObject target, GameObject visualSource, string childName)
        {
            if (target == null || visualSource == null || string.IsNullOrEmpty(childName)) return;

            Transform child = null;
            var transforms = visualSource.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null) continue;
                if (t.name.Contains(childName) && !t.name.Contains("Outline")) { child = t; break; }
            }
            if (child == null) return;

            var existing = target.transform.Find(GameNames.ModVisualChild);
            if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);

            var visualGo = UnityEngine.Object.Instantiate(child.gameObject, target.transform);
            visualGo.name = GameNames.ModVisualChild;
            visualGo.SetActive(true);

            foreach (var r in target.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (r.transform.IsChildOf(visualGo.transform)) continue;
                if (r.gameObject.name == GameNames.ModVisualChild) continue;
                r.enabled = false;
            }
        }

        /// <summary>
        /// Instantiate a custom prefab/mesh loaded from an AssetBundle as the item's visual, replacing
        /// the shell's own renderers. The prefab keeps everything Unity itself can serialize —
        /// MeshRenderer, ParticleSystem, Light, TrailRenderer, Animator — so custom visual EFFECTS
        /// come along with the mesh. What it CANNOT carry is custom C# MonoBehaviour scripts: under
        /// IL2CPP those types do not exist in the runtime, so Unity drops them on load (use the Lib's
        /// capabilities / use handlers for behaviour instead).
        /// </summary>
        public static GameObject AttachBundlePrefab(GameObject target, GameObject sourcePrefab,
                                                    Vector3 localOffset, Vector3 localEuler, Vector3 localScale)
        {
            if (target == null || sourcePrefab == null) return null;

            var existing = target.transform.Find(GameNames.ModVisualChild);
            if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);

            var visualGo = UnityEngine.Object.Instantiate(sourcePrefab, target.transform);
            visualGo.name = GameNames.ModVisualChild;
            visualGo.transform.localPosition = localOffset;
            visualGo.transform.localEulerAngles = localEuler;
            visualGo.transform.localScale = localScale;
            visualGo.SetActive(true);

            // Hide the shell's own look so only the custom mesh shows.
            foreach (var r in target.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (r.transform.IsChildOf(visualGo.transform)) continue;
                r.enabled = false;
            }
            return visualGo;
        }

        /// <summary>
        /// Repair materials whose shader did not survive the bundle (Unity substitutes the magenta
        /// error shader). Re-pointing them at the game's own HDRP/Lit keeps every property whose name
        /// matches — base colour, maps, smoothness — because Unity preserves same-named properties
        /// across a shader swap. Returns how many materials were repaired.
        /// </summary>
        public static int RepairBundleShaders(GameObject root)
        {
            if (root == null) return 0;
            var replacement = Shader.Find(GameNames.Shader.HdrpLit);
            if (replacement == null)
            {
                MelonLogger.Warning($"[POGContentLib] Cannot repair bundle shaders: '{GameNames.Shader.HdrpLit}' not found.");
                return 0;
            }

            int fixedCount = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    bool broken = m.shader == null || m.shader.name.Contains(GameNames.Shader.ErrorShader);
                    if (!broken) continue;
                    m.shader = replacement;
                    fixedCount++;
                }
                r.materials = mats;
            }
            if (fixedCount > 0)
                MelonLogger.Msg($"[POGContentLib] Repaired {fixedCount} bundle material(s) onto {GameNames.Shader.HdrpLit}. " +
                                "Build the bundle against the game's Unity/HDRP version to avoid this.");
            return fixedCount;
        }

        /// <summary>Tint the visible reskin renderers via HDRP _BaseColor (fallback _Color).</summary>
        public static void ApplyTint(GameObject root, Color tint, string childHint)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled) continue;
                string n = r.gameObject.name;
                if (!n.Contains(GameNames.ModVisualChild) && !n.Contains(childHint ?? "")
                    && !n.Contains("Jewel") && !n.Contains("Coin") && !n.Contains("Diamond"))
                    continue;

                var mats = r.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null) continue;
                    var copy = new Material(mats[m]);
                    if (copy.HasProperty(GameNames.Shader.BaseColor)) copy.SetColor(GameNames.Shader.BaseColor, tint);
                    else if (copy.HasProperty(GameNames.Shader.Color)) copy.SetColor(GameNames.Shader.Color, tint);
                    mats[m] = copy;
                }
                r.materials = mats;
            }
        }

        /// <summary>Destroy SpeakingStone/voice components dragged in with a cloned SpeakingStone mesh.</summary>
        public static void StripVoiceComponents(GameObject go)
        {
            var behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var mb = behaviours[i];
                if (mb == null) continue;
                string tn = mb.GetIl2CppType().Name;
                if (tn == GameNames.VoiceComponents.SpeakingStone || tn.Contains(GameNames.VoiceComponents.VoiceProximity))
                    UnityEngine.Object.Destroy(mb);
            }
        }

        /// <summary>Hide SpeakingStone VFX/outline objects carried by the donor mesh.</summary>
        public static void HideSpeakingStoneVfx(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                string n = r.gameObject.name;
                for (int k = 0; k < GameNames.SpeakingStoneVfx.Length; k++)
                {
                    if (n.IndexOf(GameNames.SpeakingStoneVfx[k], StringComparison.OrdinalIgnoreCase) >= 0)
                    { r.gameObject.SetActive(false); break; }
                }
            }
        }
    }
}
