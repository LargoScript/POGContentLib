using HarmonyLib;
using Il2Cpp;
using POGContentLib.Core;
using UnityEngine;

namespace POGContentLib.Items
{
    /// <summary>
    /// Items-domain Harmony patches. Same pattern that proved workable on this engine
    /// (POGCustomItems): postfix on InventoryItem.OnNetworkSpawn/Interact.
    /// </summary>
    [HarmonyPatch(typeof(InventoryItem), GameNames.Methods.InventoryItem_OnNetworkSpawn)]
    internal static class Patch_Item_OnNetworkSpawn
    {
        static void Postfix(InventoryItem __instance)
        {
            if (__instance == null) return;
            var plugin = ItemsPlugin.Instance;
            if (plugin == null || !plugin.IsModItem(__instance)) return;

            // Ensure the state component exists on the spawned instance (managed state does not
            // survive Instantiate — the handle lazily resolves itself by hash).
            if (__instance.GetComponent<ModItemHandle>() == null)
                __instance.gameObject.AddComponent<ModItemHandle>();

            // Kick visual effects awake: they were built on the INACTIVE template, where
            // ParticleSystem.Play() silently no-ops — each live instance must restart them.
            ItemVisualEffect.RestartEffectsOn(__instance);
        }
    }

    /// <summary>
    /// The item's primary action — "use what I am holding" (left click). This is the correct hook:
    /// Interact is the PICKUP interaction, so patching that never fired the use flow (IsInHands is
    /// false while picking up, and using a held item does not call Interact at all).
    /// </summary>
    [HarmonyPatch(typeof(InventoryItem), GameNames.Methods.InventoryItem_StartPrimaryAction)]
    internal static class Patch_Item_StartPrimaryAction
    {
        static void Postfix(InventoryItem __instance)
        {
            if (__instance == null) return;
            var plugin = ItemsPlugin.Instance;
            if (plugin == null || !plugin.IsModItem(__instance)) return;

            plugin.TryUseItem(__instance, __instance.Entity);
        }
    }
}
