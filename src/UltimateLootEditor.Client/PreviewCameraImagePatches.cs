using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace ULE.SpawnEditor
{
    [HarmonyPatch(typeof(CameraImage), "method_2")]
    internal static class PreviewCameraImagePreCullPatch
    {
        private static bool Prefix(CameraImage __instance, Camera cam)
        {
            if (!PresetPreviewBridge.ShouldBypassCameraImageEffects(__instance))
            {
                return true;
            }

            Shader.DisableKeyword("WeaponPreview");
            return false;
        }
    }

    [HarmonyPatch(typeof(CameraImage), "method_1")]
    internal static class PreviewCameraImagePreRenderPatch
    {
        private static bool Prefix(CameraImage __instance, Camera cam)
        {
            return !PresetPreviewBridge.ShouldBypassCameraImageEffects(__instance);
        }
    }

    [HarmonyPatch(typeof(CameraImage), "method_3")]
    internal static class PreviewCameraImageAmbientPatch
    {
        private static bool Prefix(CameraImage __instance)
        {
            return !PresetPreviewBridge.ShouldBypassCameraImageAmbientEffects(__instance);
        }
    }

    [HarmonyPatch(typeof(CameraImage), "method_4")]
    internal static class PreviewCameraImagePostRenderPatch
    {
        private static bool Prefix(CameraImage __instance, Camera cam)
        {
            if (!PresetPreviewBridge.ShouldBypassCameraImageEffects(__instance))
            {
                return true;
            }

            Shader.DisableKeyword("WeaponPreview");
            return false;
        }
    }
}
