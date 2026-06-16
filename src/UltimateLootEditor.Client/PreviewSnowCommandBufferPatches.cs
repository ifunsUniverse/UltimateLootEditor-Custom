using HarmonyLib;
using UnityEngine.Rendering;

namespace ULE.SpawnEditor
{
    [HarmonyPatch(typeof(GClass1001), "UpdateOnPreCullRender")]
    internal static class PreviewSnowCommandBufferCameraPatch
    {
        private static bool Prefix(GClass1001 __instance, ref CommandBuffer buffer, ref bool __result)
        {
            if (!PresetPreviewBridge.TryGetSnowCommandBufferCameraOverride(out var camera) &&
                !PresetPreviewBridge.TryGetManualSnowRenderCamera(out camera))
            {
                return true;
            }

            if (__instance.Cameras.TryGetValue(camera, out buffer))
            {
                __result = true;
                return false;
            }

            buffer = GClass1001.FindOrCreate(camera, __instance.CameraEvent, __instance.BufferName);
            __instance.method_0(camera, buffer);
            __result = true;
            return false;
        }
    }
}
