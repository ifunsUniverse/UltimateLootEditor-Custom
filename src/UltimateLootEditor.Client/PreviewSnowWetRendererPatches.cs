using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.UI.Screens;
using HarmonyLib;
using UnityEngine;

namespace ULE.SpawnEditor
{
    [HarmonyPatch]
    internal static class PreviewSnowWetRendererPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return SnowWetRendererPatchTargets.Find("OnScreenChanged", typeof(EEftScreenType));
        }

        private static void Prefix(ref EEftScreenType screenType)
        {
            if (PresetPreviewBridge.IsPreviewTransitionActiveOrOpen)
            {
                screenType = EEftScreenType.BattleUI;
            }
        }
    }

    [HarmonyPatch(typeof(SnowWetRenderer), "method_2")]
    internal static class PreviewSnowWetRendererStatePatch
    {
        private static void Prefix(SnowWetRenderer __instance)
        {
            if (!PresetPreviewBridge.IsPreviewTransitionActiveOrOpen)
            {
                return;
            }

            if (PresetPreviewBridge.TryGetCapturedSnowState(
                    __instance,
                    out var winterShow,
                    out var enabled,
                    out var wetting,
                    out var opaqueness,
                    out var springSnowFactor,
                    out var stormEnabled))
            {
                __instance.enabled = enabled;
                __instance.WinterShow = winterShow;
                __instance.Wetting = wetting;
                __instance.Opaqueness = opaqueness;
                __instance.SpringSnowFactor = springSnowFactor;
                __instance.StormEnabled = stormEnabled;
            }
        }
    }

    [HarmonyPatch]
    internal static class PreviewSnowWetRendererCameraPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return SnowWetRendererPatchTargets.Find("OnPreCullCallback", typeof(Camera));
        }

        private static void Prefix(Camera currentCamera)
        {
            PresetPreviewBridge.RecordSnowPreCullCamera(currentCamera);
            PresetPreviewBridge.EnsureBattleCameraUsedForWorldOverlays(currentCamera);
            PresetPreviewBridge.BeginSnowCommandBufferCameraOverride(currentCamera);
        }

        private static void Postfix(Camera currentCamera)
        {
            PresetPreviewBridge.EndSnowCommandBufferCameraOverride(currentCamera);
        }
    }

    internal static class SnowWetRendererPatchTargets
    {
        public static IEnumerable<MethodBase> Find(string methodName, params Type[] parameters)
        {
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            var method = typeof(SnowWetRenderer).GetMethod(methodName, flags, null, parameters ?? Type.EmptyTypes, null);
            if (method != null)
            {
                yield return method;
            }
        }
    }
}
