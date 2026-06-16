using HarmonyLib;
using UnityEngine;

namespace ULE.SpawnEditor
{
    [HarmonyPatch(typeof(ShadowMaskExtractor), "smethod_0")]
    internal static class PreviewShadowMaskExtractorPatch
    {
        private static bool Prefix(Camera currentCamera, ref bool __result)
        {
            if (!PresetPreviewBridge.ShouldTreatCameraAsBattleWorldCamera(currentCamera))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }
}
