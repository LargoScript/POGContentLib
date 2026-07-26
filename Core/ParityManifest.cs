using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace POGContentLib.Core
{
    /// <summary>One registered content item as seen by the parity layer: its full id + version.</summary>
    public sealed class ParityEntry
    {
        public string FullId { get; }
        public string Version { get; }

        public ParityEntry(string fullId, string version)
        {
            FullId = fullId;
            Version = string.IsNullOrEmpty(version) ? "1" : version;
        }

        public override string ToString() => $"{FullId}@{Version}";
    }

    /// <summary>
    /// Result of comparing THIS peer's content set against the host's (from the joining client's POV).
    /// Under ForceSamePrefabs=true every peer must have the identical prefab set, so any of these
    /// three buckets being non-empty means the join would fail with an opaque "connection failed".
    /// </summary>
    public sealed class ParityReport
    {
        /// <summary>Host advertises it, this peer lacks it → must install/enable.</summary>
        public List<ParityEntry> Missing { get; } = new List<ParityEntry>();
        /// <summary>This peer has it, host lacks it → must remove/disable (or host must add it).</summary>
        public List<ParityEntry> Extra { get; } = new List<ParityEntry>();
        /// <summary>Same id, different version → update to the host's version.</summary>
        public List<(ParityEntry local, ParityEntry remote)> VersionMismatch { get; } = new List<(ParityEntry, ParityEntry)>();

        public string LocalToken { get; internal set; }
        public string RemoteToken { get; internal set; }
        /// <summary>True when the host advertised no parity data at all (vanilla or Lib-less host).</summary>
        public bool RemoteHadNoData { get; internal set; }

        public bool IsMatch => Missing.Count == 0 && Extra.Count == 0 && VersionMismatch.Count == 0;

        /// <summary>Human-readable multi-line summary (used for logs and by any UI presenter).</summary>
        public string Describe()
        {
            if (IsMatch) return "Content parity OK (matches host).";
            var sb = new StringBuilder();
            sb.Append("Content mismatch with host");
            if (RemoteHadNoData) sb.Append(" (host advertised no parity data — vanilla or POGContentLib-less host)");
            sb.Append(':');
            if (Missing.Count > 0)
                sb.Append("\n  MISSING (install/enable to join): ").Append(string.Join(", ", Missing));
            if (Extra.Count > 0)
                sb.Append("\n  EXTRA (host does not have these): ").Append(string.Join(", ", Extra));
            if (VersionMismatch.Count > 0)
                sb.Append("\n  VERSION (update to host): ")
                  .Append(string.Join(", ", VersionMismatch.Select(v => $"{v.local.FullId} {v.local.Version}->{v.remote.Version}")));
            return sb.ToString();
        }
    }

    /// <summary>
    /// A deterministic, order-independent snapshot of a peer's registered content, serialized into a
    /// single string for Steam lobby metadata. This is the DATA CONTRACT of the multiplayer parity
    /// check: format-versioned (leading "1|") so it can evolve without breaking older/newer peers.
    /// Pure logic — no game or Steam dependency; validated in isolation.
    /// </summary>
    public sealed class ParityManifest
    {
        /// <summary>Wire-format version of the manifest string itself (not the content versions).</summary>
        public const int FormatVersion = 1;

        public IReadOnlyList<ParityEntry> Entries { get; }
        /// <summary>Short fingerprint (8 hex) over the canonical entry list — for a quick equal/not-equal.</summary>
        public string Token { get; }

        public ParityManifest(IEnumerable<ParityEntry> entries)
        {
            // Canonical order: sort by full id so the token is independent of registration order.
            Entries = (entries ?? Enumerable.Empty<ParityEntry>())
                .Where(e => e != null && !string.IsNullOrEmpty(e.FullId))
                .OrderBy(e => e.FullId, StringComparer.Ordinal)
                .ToList();
            Token = ComputeToken(Entries);
        }

        private static string ComputeToken(IReadOnlyList<ParityEntry> entries)
        {
            string canonical = string.Join(";", entries.Select(e => e.ToString()));
            using (var md5 = MD5.Create())
            {
                byte[] h = md5.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                return BitConverter.ToString(h, 0, 4).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>Build the manifest from every registered definition (item/mob/level…).</summary>
        public static ParityManifest FromRegistry(ContentRegistry registry)
        {
            var list = new List<ParityEntry>();
            if (registry != null)
            {
                foreach (var def in registry.AllDefinitions)
                {
                    string version = (def as IContentVersion)?.ContentVersion ?? "1";
                    list.Add(new ParityEntry(ContentRegistry.FullId(def.ModId, def.ContentId), version));
                }
            }
            return new ParityManifest(list);
        }

        /// <summary>Serialize to a single metadata string: "1|&lt;token&gt;|id@ver;id@ver;...".</summary>
        public string Serialize()
        {
            string body = string.Join(";", Entries.Select(e => e.ToString()));
            return $"{FormatVersion}|{Token}|{body}";
        }

        /// <summary>Parse a metadata string back into a manifest. Returns an empty manifest on garbage.</summary>
        public static ParityManifest Deserialize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return new ParityManifest(Enumerable.Empty<ParityEntry>());
            // Split into at most 3 parts: format | token | body (body may itself contain no '|').
            int p1 = raw.IndexOf('|');
            if (p1 < 0) return new ParityManifest(Enumerable.Empty<ParityEntry>());
            int p2 = raw.IndexOf('|', p1 + 1);
            if (p2 < 0) return new ParityManifest(Enumerable.Empty<ParityEntry>());
            string body = raw.Substring(p2 + 1);

            var entries = new List<ParityEntry>();
            foreach (var part in body.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int at = part.LastIndexOf('@');
                if (at <= 0) { entries.Add(new ParityEntry(part, "1")); continue; }
                entries.Add(new ParityEntry(part.Substring(0, at), part.Substring(at + 1)));
            }
            return new ParityManifest(entries);
        }

        /// <summary>
        /// Compare this (the joining client's) manifest against the host's. Host is authoritative:
        /// Missing = host has it and we don't; Extra = we have it and the host doesn't.
        /// </summary>
        public ParityReport CompareToHost(ParityManifest host)
        {
            var report = new ParityReport { LocalToken = Token, RemoteToken = host?.Token };
            if (host == null) { report.RemoteHadNoData = true; return report; }

            var localMap = new Dictionary<string, ParityEntry>(StringComparer.Ordinal);
            foreach (var e in Entries) localMap[e.FullId] = e;
            var hostMap = new Dictionary<string, ParityEntry>(StringComparer.Ordinal);
            foreach (var e in host.Entries) hostMap[e.FullId] = e;

            foreach (var h in host.Entries)
            {
                if (!localMap.TryGetValue(h.FullId, out var l)) report.Missing.Add(h);
                else if (l.Version != h.Version) report.VersionMismatch.Add((l, h));
            }
            foreach (var l in Entries)
                if (!hostMap.ContainsKey(l.FullId)) report.Extra.Add(l);

            return report;
        }
    }
}
