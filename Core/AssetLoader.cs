using System.Collections.Generic;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;

namespace POGContentLib.Core
{
    /// <summary>
    /// Asset loader. AssetBundle (Unity 2022.3.62f2 / HDRP 14) + loose PNG→Sprite.
    /// The full bundle pipeline (meshes/prefabs) is enabled after the Milestone 0.2 smoke test.
    /// </summary>
    public class AssetLoader
    {
        private readonly Dictionary<string, AssetBundle> _bundles = new();

        /// <summary>Load (and cache) an AssetBundle from a file.</summary>
        public AssetBundle LoadBundle(string path)
        {
            if (_bundles.TryGetValue(path, out var cached)) return cached;
            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                MelonLogger.Error($"[POGContentLib] Failed to load AssetBundle: {path}");
                return null;
            }
            _bundles[path] = bundle;
            MelonLogger.Msg($"[POGContentLib] Loaded AssetBundle: {path}");
            return bundle;
        }

        /// <summary>Load an asset of type T (via Il2Cpp type, avoiding interop generic pitfalls).</summary>
        public T LoadAsset<T>(AssetBundle bundle, string assetName) where T : Object
        {
            if (bundle == null) return null;
            var obj = bundle.LoadAsset(assetName, Il2CppType.Of<T>());
            var cast = obj != null ? obj.TryCast<T>() : null;
            if (cast == null) MelonLogger.Warning($"[POGContentLib] Asset '{assetName}' missing/wrong type in bundle.");
            return cast;
        }

        /// <summary>Loose PNG → Sprite (no Unity Editor). Keep DontUnloadUnusedAsset so it survives.</summary>
        public static Sprite LoadPngAsSprite(string path)
        {
            byte[] data = System.IO.File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            ImageConversion.LoadImage(tex, data);
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        public void UnloadAll()
        {
            foreach (var b in _bundles.Values) if (b != null) b.Unload(false);
            _bundles.Clear();
        }
    }
}
