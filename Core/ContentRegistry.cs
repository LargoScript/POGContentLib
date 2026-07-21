using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using MelonLoader;
using Unity.Netcode;

namespace POGContentLib.Core
{
    /// <summary>Base contract for any content definition (item, mob, level…).</summary>
    public interface IContentDefinition
    {
        string ModId { get; }
        string ContentId { get; }
    }

    /// <summary>
    /// Central registry of definitions and built prefabs, shared across all domains.
    /// The identity key is a deterministic GlobalObjectIdHash (MD5(modId:contentId)),
    /// identical on every peer — which is exactly what ForceSamePrefabs=true requires.
    /// </summary>
    public class ContentRegistry
    {
        private readonly Dictionary<string, IContentDefinition> _defsByFullId = new();
        private readonly Dictionary<uint, IContentDefinition> _defsByHash = new();
        private readonly Dictionary<uint, NetworkObject> _prefabsByHash = new();

        /// <summary>Full id "modId:contentId".</summary>
        public static string FullId(string modId, string contentId) => $"{modId}:{contentId}";

        /// <summary>Deterministic GlobalObjectIdHash from a string (LethalLib pattern).</summary>
        public static uint ComputeHash(string modId, string contentId, int salt = 0)
        {
            string input = salt == 0 ? FullId(modId, contentId) : $"{modId}:{contentId}#{salt}";
            using var md5 = MD5.Create();
            byte[] h = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToUInt32(h, 0);
        }

        /// <summary>Register a definition (idempotent; a duplicate logs a warning).</summary>
        public bool RegisterDefinition(IContentDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.ModId) || string.IsNullOrEmpty(def.ContentId))
            {
                MelonLogger.Error("[POGContentLib] Invalid content definition (empty ModId/ContentId).");
                return false;
            }
            string full = FullId(def.ModId, def.ContentId);
            if (_defsByFullId.ContainsKey(full))
            {
                MelonLogger.Warning($"[POGContentLib] Duplicate content id ignored: {full}");
                return false;
            }
            uint hash = ComputeHash(def.ModId, def.ContentId);
            _defsByFullId[full] = def;
            _defsByHash[hash] = def;
            MelonLogger.Msg($"[POGContentLib] Registered definition: {full} (hash={hash:X8})");
            return true;
        }

        /// <summary>Bind a built prefab to content (keyed by the NetworkObject's actual hash).</summary>
        public void RegisterPrefab(NetworkObject prefab)
        {
            if (prefab == null) return;
            _prefabsByHash[prefab.GlobalObjectIdHash] = prefab;
        }

        public IContentDefinition GetDefinition(string modId, string contentId)
            => _defsByFullId.TryGetValue(FullId(modId, contentId), out var d) ? d : null;

        public IContentDefinition GetDefinitionByHash(uint hash)
            => _defsByHash.TryGetValue(hash, out var d) ? d : null;

        public NetworkObject GetPrefabByHash(uint hash)
            => _prefabsByHash.TryGetValue(hash, out var p) ? p : null;

        public IEnumerable<IContentDefinition> AllDefinitions => _defsByFullId.Values;
        public IEnumerable<NetworkObject> AllPrefabs => _prefabsByHash.Values;

        /// <summary>Re-key a prefab's hash in the registry (after a collision re-salt).</summary>
        internal void Rekey(uint oldHash, uint newHash)
        {
            if (_defsByHash.TryGetValue(oldHash, out var def))
            {
                _defsByHash.Remove(oldHash);
                _defsByHash[newHash] = def;
            }
            if (_prefabsByHash.TryGetValue(oldHash, out var pf))
            {
                _prefabsByHash.Remove(oldHash);
                _prefabsByHash[newHash] = pf;
            }
        }
    }
}
