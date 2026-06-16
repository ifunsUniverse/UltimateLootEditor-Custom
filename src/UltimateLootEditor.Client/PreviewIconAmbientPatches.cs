using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.UI;
using HarmonyLib;

namespace ULE.SpawnEditor
{
    [HarmonyPatch]
    internal static class PreviewIconAmbientResetPatches
    {
        private const BindingFlags AnyBinding = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var concreteIconResetOwners = new[]
            {
                typeof(GClass925).BaseType,
                typeof(GClass926).BaseType,
                typeof(GClass927).BaseType
            };

            foreach (var resetOwner in concreteIconResetOwners)
            {
                var resetMethod = GetResetMethod(resetOwner, "Struct115");
                if (resetMethod != null)
                {
                    yield return resetMethod;
                }
            }

            var cameraImageResetMethod = GetResetMethod(typeof(CameraImage), "Struct1174");
            if (cameraImageResetMethod != null)
            {
                yield return cameraImageResetMethod;
            }
        }

        private static MethodBase GetResetMethod(Type resetOwner, string nestedTypeName)
        {
            if (resetOwner == null)
            {
                return null;
            }

            var nestedType = resetOwner.GetNestedType(nestedTypeName, AnyBinding);
            if (nestedType == null)
            {
                return null;
            }

            if (nestedType.ContainsGenericParameters && resetOwner.IsConstructedGenericType)
            {
                try
                {
                    nestedType = nestedType.MakeGenericType(resetOwner.GetGenericArguments());
                }
                catch
                {
                    return null;
                }
            }

            if (nestedType.ContainsGenericParameters)
            {
                return null;
            }

            return nestedType.GetMethod("Reset", AnyBinding);
        }

        private static bool Prefix()
        {
            return !PresetPreviewBridge.ShouldBypassAmbientResetDuringPreview;
        }
    }
}
