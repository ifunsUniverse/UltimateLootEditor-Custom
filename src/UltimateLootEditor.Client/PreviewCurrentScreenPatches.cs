using EFT.UI.Screens;
using HarmonyLib;

namespace ULE.SpawnEditor
{
    [HarmonyPatch(typeof(CurrentScreenSingletonClass), "set_CurrentBaseScreenController")]
    internal static class PreviewCurrentScreenPatch
    {
        private static void Prefix(ref GInterface495<EEftScreenType> value)
        {
            if (PresetPreviewBridge.TryOverrideCurrentScreenController(value, out var replacement))
            {
                value = replacement;
            }
        }
    }
}
