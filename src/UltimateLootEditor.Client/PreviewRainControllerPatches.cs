using HarmonyLib;
using UnityEngine;

namespace ULE.SpawnEditor
{
    [HarmonyPatch(typeof(RainController), "method_10")]
    internal static class PreviewRainControllerSetWettingPatch
    {
        private static void Prefix(ref float wetting)
        {
            PresetPreviewBridge.TryPreservePreviewRainWetting(ref wetting);
        }
    }

    [HarmonyPatch(typeof(RainController), "method_11")]
    internal static class PreviewRainControllerUpdateWettingPatch
    {
        private static void Prefix(ref float wetting, ref float t01)
        {
            if (PresetPreviewBridge.TryPreservePreviewRainWetting(ref wetting))
            {
                t01 = 1f;
            }
        }
    }

    [HarmonyPatch(typeof(RainController), "method_13")]
    internal static class PreviewRainControllerOpaquenessPatch
    {
        private static void Prefix(ref float opaqueness)
        {
            PresetPreviewBridge.TryPreservePreviewRainOpaqueness(ref opaqueness);
        }
    }
}
