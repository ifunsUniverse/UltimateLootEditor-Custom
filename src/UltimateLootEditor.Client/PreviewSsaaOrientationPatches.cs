using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ULE.SpawnEditor
{
    [HarmonyPatch]
    internal static class PreviewSsaaImplFinalBlitOrientationPatch
    {
        private const BindingFlags AnyBinding = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("SSAAImpl");
            return type?.GetMethod(
                "OnRenderImage",
                AnyBinding,
                null,
                new[] { typeof(RenderTexture), typeof(RenderTexture) },
                null);
        }

        private static void Prefix(object __instance, ref bool __state)
        {
            __state = false;

            if (!PresetPreviewBridge.ShouldInvertBattleCameraUpscalerFinalFlip(__instance))
            {
                return;
            }

            __state = TryFlipBoolField(__instance, "FlippedV");
        }

        private static void Postfix(object __instance, bool __state)
        {
            if (__state)
            {
                TryFlipBoolField(__instance, "FlippedV");
            }
        }

        private static bool TryFlipBoolField(object instance, string fieldName)
        {
            if (instance == null)
            {
                return false;
            }

            try
            {
                var field = instance.GetType().GetField(fieldName, AnyBinding);
                if (field == null || field.FieldType != typeof(bool))
                {
                    return false;
                }

                field.SetValue(instance, !(bool)field.GetValue(instance));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
