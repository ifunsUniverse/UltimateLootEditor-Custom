using System;
using System.Reflection;
using Comfort.Common;
using Diz.LanguageExtensions;
using EFT;
using EFT.CameraControl;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.WeaponModding;
using HarmonyLib;
using UnityEngine;

namespace ULE.SpawnEditor
{
    [HarmonyPatch(typeof(WeaponModdingScreen), "Show", new[] { typeof(Item), typeof(InventoryController), typeof(CompoundItem[]) })]
    internal static class PreviewWeaponModdingScreenShowPatch
    {
        private static void Postfix(WeaponModdingScreen __instance)
        {
            PresetPreviewBridge.HandleWeaponModdingScreenShown(__instance);
        }
    }

    [HarmonyPatch]
    internal static class PreviewWeaponModdingScreenClosePatch
    {
        private static MethodBase TargetMethod()
        {
            return typeof(ItemObserveScreen<WeaponModdingScreen.GClass3922, WeaponModdingScreen>).GetMethod(
                "Close",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static void Postfix(object __instance)
        {
            PresetPreviewBridge.HandleWeaponModdingScreenClosed(__instance as WeaponModdingScreen);
        }
    }

    [HarmonyPatch(typeof(EditBuildScreen), "Show", new[] { typeof(Item), typeof(Item), typeof(InventoryController), typeof(ISession) })]
    internal static class PreviewEditBuildScreenShowPatch
    {
        private static void Prefix()
        {
            PresetPreviewBridge.MarkEditOpenTiming("EditBuildScreen.Show.begin");
        }

        private static void Postfix(EditBuildScreen __instance)
        {
            PresetPreviewBridge.HandleEditBuildScreenShown(__instance);
            PresetPreviewBridge.MarkEditOpenTiming("EditBuildScreen.Show.end");
        }
    }

    [HarmonyPatch]
    internal static class PreviewEditBuildScreenClosePatch
    {
        private static MethodBase TargetMethod()
        {
            return typeof(ItemObserveScreen<EditBuildScreen.GClass3881, EditBuildScreen>).GetMethod(
                "Close",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static void Postfix(object __instance)
        {
            PresetPreviewBridge.HandleEditBuildScreenClosed(__instance as EditBuildScreen);
        }
    }

    [HarmonyPatch(typeof(EditBuildScreen), nameof(EditBuildScreen.CreateBuildManipulation))]
    internal static class PreviewEditBuildCreateManipulationPatch
    {
        private static bool Prefix(EditBuildScreen __instance, ref GClass3467 __result, out long __state)
        {
            __state = PresetPreviewBridge.BeginEditOpenTimingStage("EditBuildScreen.CreateBuildManipulation");
            if (PresetPreviewBridge.TryCreateFastEditBuildManipulation(__instance, out var manipulation))
            {
                __result = manipulation;
                PresetPreviewBridge.MarkEditOpenTiming("EditBuildScreen.CreateBuildManipulation.fastPath");
                return false;
            }

            PresetPreviewBridge.MarkEditOpenTiming("EditBuildScreen.CreateBuildManipulation.defaultPath");
            PresetPreviewBridge.TryReuseCachedEditBuildTrader(__instance);
            return true;
        }

        private static void Postfix(EditBuildScreen __instance, long __state)
        {
            PresetPreviewBridge.EndEditOpenTimingStage("EditBuildScreen.CreateBuildManipulation", __state);
            PresetPreviewBridge.TryCaptureCachedEditBuildTrader(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponPreview), nameof(WeaponPreview.SetupItemPreview))]
    internal static class PreviewWeaponPreviewSetupItemPatch
    {
        private static bool Prefix(
            WeaponPreview __instance,
            Item item,
            ref Action onLoadingStart,
            ref Action onLoadingFinished,
            Callback onFinished,
            bool setAsClosest,
            Vector3? initialRotation,
            ref bool enableWeaponLights,
            out long __state)
        {
            __state = PresetPreviewBridge.BeginEditOpenTimingStage("WeaponPreview.SetupItemPreview");
            onLoadingStart = PresetPreviewBridge.WrapEditOpenTimingAction(onLoadingStart, "WeaponPreview.onLoadingStart");
            onLoadingFinished = PresetPreviewBridge.WrapEditOpenTimingAction(onLoadingFinished, "WeaponPreview.onLoadingFinished");

            if (PresetPreviewBridge.ShouldDisableWeaponPreviewItemLights)
            {
                enableWeaponLights = false;
            }

            if (item == null)
            {
                PresetPreviewBridge.CancelAsyncWeaponPreviewSetup(__instance);
                return true;
            }

            if (PresetPreviewBridge.TrySetupAsyncWeaponPreview(
                    __instance,
                    item,
                    onLoadingStart,
                    onLoadingFinished,
                    onFinished,
                    setAsClosest,
                    initialRotation,
                    enableWeaponLights))
            {
                return false;
            }

            return true;
        }

        private static void Postfix(long __state)
        {
            PresetPreviewBridge.EndEditOpenTimingStage("WeaponPreview.SetupItemPreview", __state);
        }
    }

    [HarmonyPatch(typeof(PoolManagerClass), nameof(PoolManagerClass.CreateCleanLootPrefab), new[] { typeof(Item), typeof(ECameraType), typeof(IPlayer) })]
    internal static class PreviewPoolManagerCreateCleanLootPrefabTimingPatch
    {
        private static void Prefix(Item item, out long __state)
        {
            __state = PresetPreviewBridge.BeginEditOpenTimingStage($"PoolManager.CreateCleanLootPrefab({DescribeItem(item)})");
        }

        private static void Postfix(GameObject __result, long __state)
        {
            PresetPreviewBridge.EndEditOpenTimingStage(
                $"PoolManager.CreateCleanLootPrefab(result={(__result != null ? __result.name : "<null>")})",
                __state);
        }

        private static string DescribeItem(Item item)
        {
            if (item == null)
            {
                return "<null>";
            }

            try
            {
                return item.TemplateId.ToString();
            }
            catch
            {
                return item.GetType().Name;
            }
        }
    }

    [HarmonyPatch]
    internal static class PreviewEditBuildRefreshWeaponTimingPatch
    {
        private static MethodBase TargetMethod()
        {
            return typeof(ItemObserveScreen<EditBuildScreen.GClass3881, EditBuildScreen>).GetMethod(
                "RefreshWeapon",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static void Prefix(out long __state)
        {
            __state = PresetPreviewBridge.BeginEditOpenTimingStage("ItemObserveScreen.RefreshWeapon");
        }

        private static void Postfix(long __state)
        {
            PresetPreviewBridge.EndEditOpenTimingStage("ItemObserveScreen.RefreshWeapon", __state);
        }
    }

    [HarmonyPatch]
    internal static class PreviewEditBuildSlotIconTimingPatch
    {
        private static MethodBase TargetMethod()
        {
            return typeof(ItemObserveScreen<EditBuildScreen.GClass3881, EditBuildScreen>).GetMethod(
                "method_6",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(CompoundItem) },
                null);
        }

        private static void Prefix(CompoundItem weapon, out long __state)
        {
            __state = PresetPreviewBridge.BeginEditOpenTimingStage($"ItemObserveScreen.method_6.slotIcons({DescribeItem(weapon)})");
        }

        private static void Postfix(long __state)
        {
            PresetPreviewBridge.EndEditOpenTimingStage("ItemObserveScreen.method_6.slotIcons", __state);
            PresetPreviewBridge.CompleteEditOpenTiming("slot icons built");
        }

        private static string DescribeItem(Item item)
        {
            if (item == null)
            {
                return "<null>";
            }

            try
            {
                return item.TemplateId.ToString();
            }
            catch
            {
                return item.GetType().Name;
            }
        }
    }
}
