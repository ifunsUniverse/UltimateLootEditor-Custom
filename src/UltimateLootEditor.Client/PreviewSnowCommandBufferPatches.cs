using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace ULE.SpawnEditor
{
    [HarmonyPatch(typeof(CommandBufferManager), "UpdateOnPreCullRender")]
    internal static class PreviewSnowCommandBufferCameraPatch
    {
        private static bool Prefix(CommandBufferManager __instance, ref CommandBuffer buffer, ref bool __result)
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

            buffer = CommandBufferManager.FindOrCreate(camera, __instance.CameraEvent, __instance.BufferName);
            __instance.Cameras.Add(camera, buffer);
            var ssaaByCamera = AccessTools.Field(__instance.GetType(), "SSAAs")?.GetValue(__instance) as System.Collections.IDictionary;
            ssaaByCamera?.Add(camera, null);
            __result = true;
            return false;
        }
    }
}
