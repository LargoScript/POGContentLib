using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using MelonLoader;
using Unity.Netcode;

namespace POGContentLib.Core
{
    /// <summary>
    /// Client→host bridge over NGO CustomMessagingManager (named messages) — because new RPCs
    /// are impossible under IL2CPP. This is a v2 scaffold (client-driven effects); v1 runs use
    /// host-side directly. The delegate hook is runtime-verified (Milestone), so Hook() is
    /// failure-tolerant and does not block the rest.
    /// </summary>
    public class HostBridge
    {
        public const string MessageName = "POGContentLib.HostBridge";
        private readonly Dictionary<string, Action<ulong, string>> _handlers = new();
        private bool _hooked;

        public void RegisterHandler(string action, Action<ulong, string> handler)
        {
            if (!string.IsNullOrEmpty(action) && handler != null) _handlers[action] = handler;
        }

        /// <summary>Hook the named-message handler (best-effort; safe when there is no network).</summary>
        public void Hook()
        {
            if (_hooked) return;
            var nm = NetworkManager.Singleton;
            var cmm = nm != null ? nm.CustomMessagingManager : null;
            if (cmm == null) return;
            try
            {
                // Il2Cpp delegates can't be built from a method group — convert a managed delegate.
                var del = DelegateSupport.ConvertDelegate<CustomMessagingManager.HandleNamedMessageDelegate>(
                    (Action<ulong, FastBufferReader>)OnMessage);
                cmm.RegisterNamedMessageHandler(MessageName, del);
                _hooked = true;
                MelonLogger.Msg("[POGContentLib] HostBridge hooked.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[POGContentLib] HostBridge hook failed (v2 feature): {ex.Message}");
            }
        }

        public void Unhook()
        {
            if (!_hooked) return;
            var cmm = NetworkManager.Singleton?.CustomMessagingManager;
            try { cmm?.UnregisterNamedMessageHandler(MessageName); } catch { }
            _hooked = false;
        }

        /// <summary>Client→host.</summary>
        public void SendToServer(string action, string payload)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsConnectedClient) return;
            var writer = new FastBufferWriter(1024, Unity.Collections.Allocator.Temp);
            try
            {
                writer.WriteValueSafe(action);
                writer.WriteValueSafe(payload);
                nm.CustomMessagingManager.SendNamedMessage(
                    MessageName, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
            }
            finally { writer.Dispose(); }
        }

        /// <summary>Host→specific client.</summary>
        public void SendToClient(ulong clientId, string action, string payload)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            var writer = new FastBufferWriter(1024, Unity.Collections.Allocator.Temp);
            try
            {
                writer.WriteValueSafe(action);
                writer.WriteValueSafe(payload);
                nm.CustomMessagingManager.SendNamedMessage(
                    MessageName, clientId, writer, NetworkDelivery.Reliable);
            }
            finally { writer.Dispose(); }
        }

        private void OnMessage(ulong senderId, FastBufferReader reader)
        {
            string action, payload;
            reader.ReadValueSafe(out action);
            reader.ReadValueSafe(out payload);
            if (_handlers.TryGetValue(action, out var handler)) handler(senderId, payload);
            else MelonLogger.Warning($"[POGContentLib] HostBridge: no handler for '{action}'.");
        }
    }
}
