using System;

namespace POGContentLib.Core
{
    /// <summary>
    /// Public multiplayer-parity facade (Content.Parity). A content pack — or a UI mod like POGConfig —
    /// subscribes to <see cref="OnMismatch"/> to react to a content-set difference with the host before
    /// the (opaque) ForceSamePrefabs join failure. Detection is the Lib's job and always runs; this API
    /// is just how consumers observe it.
    /// </summary>
    public sealed class ParityApi
    {
        /// <summary>Raised on a joining client when its content set doesn't match the host's.</summary>
        public event Action<ParityReport> OnMismatch
        {
            add { ParityService.OnParityMismatch += value; }
            remove { ParityService.OnParityMismatch -= value; }
        }

        /// <summary>This peer's currently-registered content, as advertised to others.</summary>
        public ParityManifest LocalManifest => ParityService.BuildLocalManifest();

        /// <summary>Force a client-side parity check now (normally runs automatically at session start).</summary>
        public ParityReport CheckNow() => ParityService.CheckAsClient();
    }
}
