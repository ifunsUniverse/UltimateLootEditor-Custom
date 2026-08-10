using EFT.UI.Screens;
using HarmonyLib;

namespace ULE.SpawnEditor
{
    [HarmonyPatch(typeof(EFT.UI.Screens.EftScreenManager), "set_CurrentBaseScreenController")]
    internal static class PreviewCurrentScreenPatch
    {
        private static void Prefix(ref EFT.UI.Screens.IBaseScreenController<EEftScreenType> value)
        {
            if (PresetPreviewBridge.TryOverrideCurrentScreenController(value, out var replacement))
            {
                value = replacement;
            }
        }
    }
}
