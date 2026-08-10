using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.UI;
using HarmonyLib;

namespace ULE.SpawnEditor
{
    [HarmonyPatch]
    internal static class PreviewIconAmbientResetPatches
    {
        private const BindingFlags AnyBinding = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static bool Prepare()
        {
            return TargetMethods().Any();
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var targets = new HashSet<MethodBase>();
            var concreteIconResetOwners = new[]
            {
                typeof(ClothingIconCreator).BaseType,
                typeof(ItemIconCreator).BaseType,
                typeof(EFT.PlayerIcons.PlayerIconCreator).BaseType
            };

            foreach (var resetOwner in concreteIconResetOwners)
            {
                var resetMethod = GetResetMethod(resetOwner, "IconRenderSettings", "Struct115");
                if (resetMethod != null)
                {
                    targets.Add(resetMethod);
                }
            }

            var cameraImageResetMethod = GetResetMethod(typeof(CameraImage), "LevelRenderSettings", "Struct1174");
            if (cameraImageResetMethod != null)
            {
                targets.Add(cameraImageResetMethod);
            }

            foreach (var target in targets)
            {
                yield return target;
            }
        }

        private static MethodBase GetResetMethod(Type resetOwner, params string[] nestedTypeNames)
        {
            if (resetOwner == null)
            {
                return null;
            }

            Type nestedType = null;
            foreach (var nestedTypeName in nestedTypeNames ?? Array.Empty<string>())
            {
                nestedType = resetOwner.GetNestedType(nestedTypeName, AnyBinding);
                if (nestedType != null)
                {
                    break;
                }
            }

            nestedType ??= resetOwner
                .GetNestedTypes(AnyBinding)
                .FirstOrDefault(type => type.GetMethod("Reset", AnyBinding, null, Type.EmptyTypes, null) != null);

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

            return nestedType.GetMethod("Reset", AnyBinding, null, Type.EmptyTypes, null);
        }

        private static bool Prefix()
        {
            return !PresetPreviewBridge.ShouldBypassAmbientResetDuringPreview;
        }
    }
}
