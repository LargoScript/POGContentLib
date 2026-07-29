using Il2Cpp;
using UnityEngine;

namespace POGContentLib.Core
{
    /// <summary>
    /// Lookups for existing game assets by name. Lets content packs reskin with vanilla
    /// meshes/sprites without shipping an AssetBundle (the "reskin" tier).
    /// </summary>
    public static class GameAssets
    {
        /// <summary>Find a loaded InventoryItem prefab by GameObject name.</summary>
        public static InventoryItem FindItemPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            var all = Resources.FindObjectsOfTypeAll<InventoryItem>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].gameObject.name == prefabName) return all[i];
            return null;
        }

        /// <summary>
        /// Find any loaded GameObject by exact name — for donors that are not items, such as the
        /// standalone VFX prefabs a ParticleEffect clones from. Prefers a prefab (no scene) over a
        /// live scene instance, so cloning does not pick up runtime state.
        /// </summary>
        public static GameObject FindGameObject(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return null;
            GameObject sceneHit = null;
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < all.Length; i++)
            {
                var go = all[i];
                if (go == null || go.name != objectName) continue;
                if (!go.scene.IsValid()) return go; // asset/prefab — the clean choice
                sceneHit ??= go;
            }
            return sceneHit;
        }

        /// <summary>Find a loaded Sprite by name (e.g. an inventory icon).</summary>
        public static Sprite FindSprite(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;
            var all = Resources.FindObjectsOfTypeAll<Sprite>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name == spriteName) return all[i];
            return null;
        }
    }
}
