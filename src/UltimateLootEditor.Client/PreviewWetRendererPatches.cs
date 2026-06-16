using HarmonyLib;
using UnityEngine;

namespace ULE.SpawnEditor
{
    [HarmonyPatch(typeof(WetRenderer), "ManualOnRenderObject")]
    internal static class PreviewWetRendererCameraPatch
    {
        private static void Prefix(Camera currentCamera)
        {
            PresetPreviewBridge.EnsureBattleCameraUsedForWorldOverlays(currentCamera);
        }
    }
}
