using System;
using Il2Cpp;
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
