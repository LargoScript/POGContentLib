using System;
using MelonLoader;
using Unity.Netcode;
using UnityEngine;

namespace POGContentLib.Core
{
    /// <summary>
    /// Registers mod prefabs with NGO. Called BEFORE the session starts (ForceSamePrefabs=true
    /// forbids AddNetworkPrefab afterwards). Does not trust AddNetworkPrefab's void return:
    /// verifies registration via NetworkPrefabOverrideLinks and re-salts the hash on collision.
    /// Session-scoped: reset on Shutdown so it re-registers on the next session start.
    /// </summary>
    public class NetworkRegistrar
    {
        private readonly ContentRegistry _registry;
        private bool _registered;

        public NetworkRegistrar(ContentRegistry registry) { _registry = registry; }

        /// <summary>Reset the flag (on session Shutdown).</summary>
        public void Reset()
        {
            if (!_registered) return;
            _registered = false;
            MelonLogger.Msg("[POGContentLib] Network registration reset (session stopped).");
        }

        /// <summary>Register all mod prefabs. Idempotent within one session.</summary>
        public void RegisterAll()
        {
            if (_registered) return;
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.NetworkConfig == null)
            {
                MelonLogger.Warning("[POGContentLib] RegisterAll: NetworkManager/Config not ready.");
                return;
            }

            int ok = 0, fail = 0;
            foreach (var prefab in _registry.AllPrefabs)
            {
                if (prefab == null || prefab.gameObject == null) { fail++; continue; }
                if (TryRegisterOne(nm, prefab)) ok++; else fail++;
            }

            _registered = true;
            MelonLogger.Msg($"[POGContentLib] Network prefabs registered: {ok} ok, {fail} failed.");
        }

        private bool TryRegisterOne(NetworkManager nm, NetworkObject prefab)
        {
            uint hash = prefab.GlobalObjectIdHash;

            // 1) Resolve a hash collision BEFORE registering (a duplicate => LogWarning+ignore in NGO).
            if (IsHashTaken(nm, hash))
            {
                var def = _registry.GetDefinitionByHash(hash);
                uint resolved = hash;
                for (int salt = 1; salt <= 8 && IsHashTaken(nm, resolved); salt++)
                {
                    if (def != null) resolved = ContentRegistry.ComputeHash(def.ModId, def.ContentId, salt);
                    else resolved = unchecked(resolved * 2654435761u + 1u); // fallback mix
                }
                if (IsHashTaken(nm, resolved))
                {
                    MelonLogger.Error($"[POGContentLib] Could not find free hash for {prefab.name} (base {hash:X8}).");
                    return false;
                }
                if (resolved != hash)
                {
                    MelonLogger.Warning($"[POGContentLib] Hash collision {hash:X8} -> re-salted {resolved:X8} for {prefab.name}.");
                    prefab.GlobalObjectIdHash = resolved;
                    _registry.Rekey(hash, resolved);
                    hash = resolved;
                }
            }

            // 2) Register through the official path.
            try { nm.AddNetworkPrefab(prefab.gameObject); }
            catch (Exception ex)
            {
                MelonLogger.Error($"[POGContentLib] AddNetworkPrefab threw for {prefab.name}: {ex.Message}");
                return false;
            }

            // 3) Post-verify (do not trust void/log): is the hash actually in the table.
            if (!IsHashTaken(nm, hash))
            {
                MelonLogger.Error($"[POGContentLib] Post-verify FAILED: {prefab.name} ({hash:X8}) not registered.");
                return false;
            }
            return true;
        }

        /// <summary>Whether a GlobalObjectIdHash is already present in NetworkPrefabOverrideLinks.</summary>
        private static bool IsHashTaken(NetworkManager nm, uint hash)
        {
            try
            {
                var links = nm.NetworkConfig.Prefabs?.NetworkPrefabOverrideLinks;
                if (links == null) return false;
                return links.ContainsKey(hash);
            }
            catch
            {
                return false; // if the dict access failed, don't block; rely on AddNetworkPrefab
            }
        }
    }
}
