using HarmonyLib;
using UnityEngine;

namespace ULE.SpawnEditor
{
    [HarmonyPatch(typeof(OnRenderObjectManager), "OnRenderObject")]
    internal static class PreviewOnRenderObjectManagerPatch
    {
        private static void Prefix()
        {
            PresetPreviewBridge.EnsureBattleCameraUsedForWorldOverlays(Camera.current);
        }
    }
}
