using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace ULE.SpawnEditor
{
    [HarmonyPatch]
    internal static class PreviewCameraImagePreCullPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return CameraImagePatchTargets.Find("SetupPreCull", typeof(Camera));
        }

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

    [HarmonyPatch]
    internal static class PreviewCameraImagePreRenderPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return CameraImagePatchTargets.Find("SetupRenderSettings", typeof(Camera));
        }

        private static bool Prefix(CameraImage __instance, Camera cam)
        {
            return !PresetPreviewBridge.ShouldBypassCameraImageEffects(__instance);
        }
    }

    [HarmonyPatch]
    internal static class PreviewCameraImageAmbientPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return CameraImagePatchTargets.Find("ForceInterfaceRenderSettings");
        }

        private static bool Prefix(CameraImage __instance)
        {
            return !PresetPreviewBridge.ShouldBypassCameraImageAmbientEffects(__instance);
        }
    }

    [HarmonyPatch]
    internal static class PreviewCameraImagePostRenderPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return CameraImagePatchTargets.Find("RestoreRenderSettings", typeof(Camera));
        }

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

    internal static class CameraImagePatchTargets
    {
        public static IEnumerable<MethodBase> Find(string methodName, params Type[] parameters)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var method = typeof(CameraImage).GetMethod(methodName, flags, null, parameters ?? Type.EmptyTypes, null);
            if (method != null)
            {
                yield return method;
            }
        }
    }
}
