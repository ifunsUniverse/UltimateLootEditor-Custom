using HarmonyLib;
using WaterSSR;

namespace ULE.SpawnEditor
{
    [HarmonyPatch(typeof(WaterRendererv3), "DisableSnowMask")]
    internal static class PreviewWaterRendererPatch
    {
        private static void Prefix(ref bool disable)
        {
            PresetPreviewBridge.RecordSnowMaskDisableRequest(disable);

            if (PresetPreviewBridge.ShouldPreserveWinterSnowMaskDuringPreview)
            {
                disable = false;
            }
        }
    }
}
