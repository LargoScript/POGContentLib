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
        /// Reskin by REPLACING the mesh on the shell's own renderers, rather than adding a visual of
        /// our own. This matters more than it sounds: a game item ships two visual variants —
        /// <c>PickupItem</c> (lying in the world) and <c>HeldItem</c> (in a character's hands, seen in
        /// third person) — and the game swaps them, hides them in the inventory, and re-enables their
        /// renderers from its own <c>m_renderers</c> list.
        ///
        /// The previous approach parented an extra "ModItemVisual" object to the item root and
        /// disabled everything else. The game promptly re-enabled its own renderers (so a diamond
        /// showed in hand) while our extra object, unknown to any of that logic, floated above it and
        /// stayed visible inside the backpack. Swapping the mesh in place means every one of those
        /// behaviours keeps working, because the objects doing them are still the game's own.
        /// </summary>
        public static void ReplaceMeshes(GameObject target, GameObject visualSource, string childName)
        {
            if (target == null || visualSource == null || string.IsNullOrEmpty(childName)) return;

            // Locate the donor mesh (a MeshFilter under the named child).
            MeshFilter donorFilter = null;
            MeshRenderer donorRenderer = null;
            foreach (var mf in visualSource.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                if (!mf.gameObject.name.Contains(childName) || mf.gameObject.name.Contains("Outline")) continue;
                donorFilter = mf;
                donorRenderer = mf.GetComponent<MeshRenderer>();
                break;
            }

            if (donorFilter == null)
            {
                var names = new System.Text.StringBuilder();
                foreach (var mf in visualSource.GetComponentsInChildren<MeshFilter>(true))
                    if (mf != null) names.Append("\n    ").Append(mf.gameObject.name);
                MelonLogger.Warning(
                    $"[POGContentLib] Reskin FAILED: no mesh child matching '{childName}' in " +
                    $"'{visualSource.name}' — the item keeps its shell mesh. Meshes available:{names}");
                return;
            }

            // Swap into EVERY mesh the shell has, so the world view and the in-hand view match.
            int swapped = 0;
            foreach (var mf in target.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                if (mf.gameObject.name.Contains("Outline")) continue;   // outline meshes track the shape

                mf.sharedMesh = donorFilter.sharedMesh;
                var r = mf.GetComponent<MeshRenderer>();
                if (r != null && donorRenderer != null) r.sharedMaterials = donorRenderer.sharedMaterials;
                swapped++;
            }

            // The item caches mesh bounds for tooltip placement and the inventory silhouette.
            try { target.GetComponent<InventoryItem>()?.RefreshMeshInfo(); }
            catch (Exception ex) { MelonLogger.Warning($"[POGContentLib] RefreshMeshInfo failed: {ex.Message}"); }

            MelonLogger.Msg($"[POGContentLib] Reskin: '{childName}' mesh applied to {swapped} renderer(s) on {target.name}.");
        }

        /// <summary>
        /// Legacy path: parent a cloned mesh child under the item. Kept for visuals that genuinely need
        /// an extra object (a bundle prefab with its own hierarchy); prefer <see cref="ReplaceMeshes"/>
        /// for a plain reskin, since only that participates in the game's own show/hide logic.
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
            if (child == null)
            {
                // Silence here meant "the item just looks like its shell" with no clue why — that is
                // how two mod stones shipped as plain diamonds. Name the miss and list the options.
                var names = new System.Text.StringBuilder();
                for (int i = 0; i < transforms.Length && i < 40; i++)
                    if (transforms[i] != null && transforms[i] != visualSource.transform)
                        names.Append("\n    ").Append(transforms[i].name);
                MelonLogger.Warning(
                    $"[POGContentLib] Reskin FAILED: no child matching '{childName}' in " +
                    $"'{visualSource.name}' — the item will keep its shell mesh. Available children:{names}");
                return;
            }

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

        /// <summary>
        /// Tint the item's mesh renderers via HDRP _BaseColor (fallback _Color). Applies to every mesh
        /// variant — the world one and the in-hand one — so the item does not change colour when
        /// picked up. Outlines and particle renderers are left alone.
        /// </summary>
        public static void ApplyTint(GameObject root, Color tint, string childHint)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                if (r.gameObject.name.Contains("Outline")) continue;

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
