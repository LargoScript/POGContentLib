using Il2Cpp;
using MelonLoader;
using Unity.Netcode;

namespace POGContentLib.Core
{
    /// <summary>
    /// Static holder for shared core services. Required because the Harmony patches
    /// (Start*, FromContainerData) are static and must reach the registry/injector
    /// without a DI graph. Initialized once in Plugin.OnInitializeMelon before Boot.
    /// </summary>
    public static class CoreServices
    {
        public static ContentRegistry Content { get; private set; }
        public static ShellFactory Shells { get; private set; }
        public static NetworkRegistrar Network { get; private set; }
        public static AssetLoader Assets { get; private set; }
        public static HostBridge Bridge { get; private set; }
        public static LootInjector Loot { get; private set; }

        private static bool _init;

        public static void Init()
        {
            if (_init) return;
            _init = true;

            Content = new ContentRegistry();
            Shells = new ShellFactory(Content);
            Network = new NetworkRegistrar(Content);
            Assets = new AssetLoader();
            Bridge = new HostBridge();
            Loot = new LootInjector(Content);

            MelonLogger.Msg("[POGContentLib] Core services initialized.");
        }

        public static bool Ready => _init;

        /// <summary>Whether an InventoryItem is a registered mod item (by its NetworkObject hash).</summary>
        public static bool IsModItem(InventoryItem item)
        {
            if (!_init || item == null) return false;
            var netObj = item.GetComponent<NetworkObject>();
            return netObj != null && Content.GetDefinitionByHash(netObj.GlobalObjectIdHash) != null;
        }
    }
}
