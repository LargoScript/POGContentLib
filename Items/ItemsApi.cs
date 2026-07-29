using MelonLoader;
using POGContentLib.Core;

namespace POGContentLib.Items
{
    /// <summary>
    /// Public item-registration facade for content packs.
    /// A content mod calls Content.Items.Register(...) / RegisterUseHandler(...) in its own
    /// OnInitializeMelon (which runs AFTER POGContentLib thanks to MelonPriority).
    /// </summary>
    public sealed class ItemsApi
    {
        internal ItemsPlugin Plugin;

        private bool Guard()
        {
            if (Plugin != null) return true;
            MelonLogger.Error("[POGContentLib] Content.Items used before POGContentLib booted.");
            return false;
        }

        public void Register(ModItemDefinition definition)
        {
            if (Guard()) Plugin.RegisterItem(definition);
        }

        public void RegisterUseHandler(string handlerId, ModItemUseHandler handler)
        {
            if (Guard()) Plugin.RegisterUseHandler(handlerId, handler);
        }

        public void Register(ModItemDefinition definition, string handlerId, ModItemUseHandler handler)
        {
            Register(definition);
            RegisterUseHandler(handlerId, handler);
        }
    }

    /// <summary>Root facade for the domain APIs.</summary>
    public static class Content
    {
        public static ItemsApi Items { get; } = new ItemsApi();
        /// <summary>Shop slots (experimental, v0.2). See ShopsApi.</summary>
        public static ShopsApi Shops { get; } = new ShopsApi();
        /// <summary>Multiplayer content-parity events (ForceSamePrefabs). See ParityApi.</summary>
        public static ParityApi Parity { get; } = new ParityApi();
        /// <summary>Runtime inspection of vanilla visuals (lights/pulse/emissive). See DiagnosticsApi.</summary>
        public static DiagnosticsApi Diagnostics { get; } = new DiagnosticsApi();
        // TODO(v2): public static MobsApi Mobs { get; }
        // TODO(v3): public static LevelsApi Levels { get; }
    }
}
