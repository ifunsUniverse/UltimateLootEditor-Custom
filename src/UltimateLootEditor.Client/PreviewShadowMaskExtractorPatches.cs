using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ULE.SpawnEditor
{
    [HarmonyPatch]
    internal static class PreviewShadowMaskExtractorPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var parameters = new[] { typeof(Camera) };

            var method = typeof(ShadowMaskExtractor).GetMethod("IsCorrect", flags, null, parameters, null);
            if (method != null)
            {
                yield return method;
            }
        }

        private static bool Prefix(Camera currentCamera, ref bool __result)
        {
            if (!PresetPreviewBridge.ShouldTreatCameraAsBattleWorldCamera(currentCamera))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }
}
