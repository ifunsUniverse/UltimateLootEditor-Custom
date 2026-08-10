using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ULE.SpawnEditor
{
    [HarmonyPatch]
    internal static class PreviewRainControllerSetWettingPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return RainControllerPatchTargets.Find("SetWetting", typeof(float));
        }

        private static void Prefix(ref float wetting)
        {
            PresetPreviewBridge.TryPreservePreviewRainWetting(ref wetting);
        }
    }

    [HarmonyPatch]
    internal static class PreviewRainControllerUpdateWettingPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return RainControllerPatchTargets.Find("SetWetting", typeof(float), typeof(float), typeof(Vector2), typeof(float));
        }

        private static void Prefix(ref float wetting, ref float t01)
        {
            if (PresetPreviewBridge.TryPreservePreviewRainWetting(ref wetting))
            {
                t01 = 1f;
            }
        }
    }

    [HarmonyPatch]
    internal static class PreviewRainControllerOpaquenessPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return RainControllerPatchTargets.Find("SetOpaqueness", typeof(float));
        }

        private static void Prefix(ref float opaqueness)
        {
            PresetPreviewBridge.TryPreservePreviewRainOpaqueness(ref opaqueness);
        }
    }

    internal static class RainControllerPatchTargets
    {
        public static IEnumerable<MethodBase> Find(string methodName, params Type[] parameters)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var method = typeof(RainController).GetMethod(methodName, flags, null, parameters ?? Type.EmptyTypes, null);
            if (method != null)
            {
                yield return method;
            }
        }
    }
}
