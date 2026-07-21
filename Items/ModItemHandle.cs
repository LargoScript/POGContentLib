using System;
using Il2Cpp;
using UnityEngine;
using Unity.Netcode;

namespace POGContentLib.Items
{
    /// <summary>
    /// Injected per-instance state MonoBehaviour on a mod item. Managed fields do NOT survive
    /// Unity.Instantiate / network spawn, so we don't store the definition in a field — we
    /// resolve it lazily by NetworkObject.GlobalObjectIdHash via ContentRegistry (the single
    /// source of truth, identical on every peer).
    /// Per-item state (UsesRemaining) is local for now; persistence/sync is runtime-Milestone work.
    /// </summary>
    public class ModItemHandle : MonoBehaviour
    {
        public ModItemHandle(IntPtr ptr) : base(ptr) { }

        private bool _init;
        private int _usesRemaining;
        private bool _consumed;

        /// <summary>This item's identity hash (== GlobalObjectIdHash).</summary>
        public uint Hash { get; private set; }

        public int UsesRemaining { get { EnsureInit(); return _usesRemaining; } set { EnsureInit(); _usesRemaining = value; } }
        public bool IsConsumed { get { EnsureInit(); return _consumed; } set { EnsureInit(); _consumed = value; } }

        /// <summary>Lazily initialize state from the definition found by hash.</summary>
        private void EnsureInit()
        {
            if (_init) return;
            _init = true;

            var netObj = GetComponent<NetworkObject>();
            Hash = netObj != null ? netObj.GlobalObjectIdHash : 0u;

            var def = ItemsPlugin.ResolveDefinition(Hash);
            _usesRemaining = def?.MaxUses ?? 1;
            _consumed = false;
        }
    }
}
