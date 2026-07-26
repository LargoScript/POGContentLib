using System;
using MelonLoader;

namespace POGContentLib.Core
{
    /// <summary>
    /// Multiplayer content-parity orchestration. Because ForceSamePrefabs=true, a client whose
    /// content set differs from the host's fails to join with an opaque "connection failed". This
    /// self-sufficient layer (no other mod required) turns that into a diagnosable, presentable
    /// event: the host advertises its manifest on the lobby, a joining client reads and compares it,
    /// and any mismatch is logged AND raised via <see cref="OnParityMismatch"/> (which POGConfig, if
    /// installed, subscribes to for a rich pre-join UI — that presenter is optional, this is not).
    ///
    /// Detection channel: Steam lobby metadata (safe, read before connecting, touches no NGO state).
    /// The alternative NGO ConnectionApproval/DisconnectReason channel is intentionally NOT used —
    /// vanilla already sets ConnectionApproval=true, and wrapping the game's own approval callback is
    /// riskier than reading lobby metadata. If a future build drops lobby metadata we revisit that.
    /// </summary>
    public static class ParityService
    {
        /// <summary>Raised on a joining client when its content set doesn't match the host's.</summary>
        public static event Action<ParityReport> OnParityMismatch;

        /// <summary>Snapshot of this peer's currently-registered content.</summary>
        public static ParityManifest BuildLocalManifest()
            => ParityManifest.FromRegistry(CoreServices.Ready ? CoreServices.Content : null);

        /// <summary>Host role: publish our manifest onto the lobby so joiners can compare.</summary>
        public static void AdvertiseAsHost()
        {
            if (!CoreServices.Ready) return;
            var manifest = BuildLocalManifest();
            bool ok = ParitySteamBridge.TrySetLobbyData(GameNames.Steam.ParityMetadataKey, manifest.Serialize());
            if (ok)
                MelonLogger.Msg($"[POGContentLib] Parity advertised: {manifest.Entries.Count} item(s), token={manifest.Token}.");
            else
                MelonLogger.Msg("[POGContentLib] Parity not advertised (no lobby yet or Steam bridge unavailable).");
        }

        /// <summary>
        /// Client role: read the host's manifest from the lobby and compare. Logs the result and, on a
        /// mismatch, raises <see cref="OnParityMismatch"/>. Returns the report (null if core not ready).
        /// </summary>
        public static ParityReport CheckAsClient()
        {
            if (!CoreServices.Ready) return null;
            var local = BuildLocalManifest();
            string raw = ParitySteamBridge.TryGetLobbyData(GameNames.Steam.ParityMetadataKey);

            // No lobby / bridge down: cannot compare. Only note it if we actually carry content
            // (a vanilla-content session has nothing to mismatch on).
            if (raw == null)
            {
                if (local.Entries.Count > 0)
                    MelonLogger.Msg("[POGContentLib] Parity: could not read host manifest (no lobby/bridge). " +
                                    "Relying on the game's own connect-time prefab check.");
                return null;
            }

            // Empty string = key unset: host is on vanilla or has no POGContentLib. If we have content,
            // the prefab sets differ → surface all our content as "extra" (host lacks it).
            if (raw.Length == 0)
            {
                if (local.Entries.Count == 0)
                {
                    MelonLogger.Msg("[POGContentLib] Parity OK (no content on either side).");
                    return new ParityReport();
                }
                var noneReport = local.CompareToHost(new ParityManifest(Array.Empty<ParityEntry>()));
                noneReport.RemoteHadNoData = true;
                RaiseMismatch(noneReport);
                return noneReport;
            }

            var host = ParityManifest.Deserialize(raw);
            var report = local.CompareToHost(host);
            if (report.IsMatch)
                MelonLogger.Msg($"[POGContentLib] Parity OK (token={report.LocalToken} matches host).");
            else
                RaiseMismatch(report);
            return report;
        }

        private static void RaiseMismatch(ParityReport report)
        {
            MelonLogger.Warning($"[POGContentLib] {report.Describe()}");
            try { OnParityMismatch?.Invoke(report); }
            catch (Exception ex) { MelonLogger.Error($"[POGContentLib] OnParityMismatch handler threw: {ex.Message}"); }
        }
    }
}
