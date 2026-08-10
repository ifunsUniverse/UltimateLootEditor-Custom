using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.LanguageExtensions;
using EFT;
using EFT.AssetsManager;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Screens;
using EFT.UI.WeaponModding;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using WaterSSR;

namespace ULE.SpawnEditor
{
    internal static class PresetPreviewBridge
    {
        private static bool ForcePreviewIntoRenderTexture => false;
        private static bool EnablePreviewDiagnostics => false;
        private static bool EnableEditOpenTimingDiagnostics => false;
        private static bool ForceBattleUiShellDuringEditBuild => false;
        private static bool SuppressWeaponModdingScreenBackdrops => false;
        private static bool ForceManualSnowRenderDuringEditBuild => false;
        private static bool ForceSnowCommandBufferCameraOverride => false;
        private static bool ForceMicroSplatTerrainSeasonDuringPreview => false;
        private static bool ForceSeasonMaterialsPreviewFixDuringEditBuild => true;
        private static bool SuppressEditBuildGeniusCameraDuringPreview => false;
        private static bool SuppressEditBuildPreviewLightsDuringRaid => true;
        private static bool BrightenPreviewRenderersWithoutPreviewLights => false;
        private static bool UsePreviewCameraAmbientFillWithoutPreviewLights => true;
        private static bool ForceWeaponPreviewShaderKeywordDuringLightFreePreview => false;
        private static bool PreserveBattleCameraEffectsDuringPreview => true;
        private static bool SuppressCameraBackedEditBuildBackdrops => true;
        private static bool SuppressBattleCameraSupersamplingDuringPreview => false;
        private static bool StabilizeBattleCameraTransformDuringPreview => true;
        private static bool EnableAsyncEditBuildPreviewSetup => true;
        private const float PreviewLightFreeColorBoost = 1.35f;
        private const float PreviewLightFreeEmissionBoost = 0.18f;
        private static readonly Color PreviewLightFreeAmbientFill = new Color(1.45f, 1.45f, 1.45f, 1f);
        private const BindingFlags AnyBinding = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private const int PostOpenSetupPassCount = 3;
        private const float PostOpenSetupInterval = 0.04f;
        private const float RuntimeAttachmentPoolInstallDelay = 0.35f;
        private const string ItemTypeName = "EFT.InventoryLogic.Item, Assembly-CSharp";
        private const string CompoundItemTypeName = "EFT.InventoryLogic.CompoundItem, Assembly-CSharp";
        private const string InventoryControllerTypeName = "EFT.InventoryLogic.InventoryController, Assembly-CSharp";
        private const string MongoIdTypeName = "EFT.MongoID, Assembly-CSharp";
        private const string ItemFactoryTypeName = "EFT.ItemFactory, Assembly-CSharp";
        private const string FlatItemsDataTypeName = "JsonType.FlatItem, Assembly-CSharp";
        private const string UnparsedDataTypeName = "UnparsedData, Assembly-CSharp";
        private const string UtilityApplicationTypeName = "UtilityApplication, Assembly-CSharp";
        private const string CanvasTypeName = "UnityEngine.Canvas, UnityEngine.UIModule";
        private static readonly char[] HexAlphabet = "0123456789abcdef".ToCharArray();
        private static readonly string[] BackgroundNameTokens = { "background", "overlay", "gradient", "shadow", "shading", "environment" };
        private static readonly string[] ProtectedNameTokens = { "button", "warning", "slot", "header", "label", "toggle", "menu", "loader", "characteristic", "backbutton", "caption" };
        private static readonly string[] BackdropComponentNames = { "CameraImage", "RawImage", "Image" };
        private static readonly string[] ProtectedComponentNames = { "Button", "Toggle", "TMP_Text", "TextMeshProUGUI", "InputField", "TMP_InputField", "Scrollbar", "Slider", "CameraViewporter", "WeaponPreview", "DropDownMenu", "CharacteristicsPanel", "ModdingScreenSlotView" };
        private static readonly string[] BattleCameraSuppressionTypeNames = { "SSAAImpl", "SSAA", "SSAAPropagator", "SSAAPropagatorOpaque", "InventoryBlur", "Freecam" };
        private static readonly string[] BattleCameraSupersamplingSuppressionTypeNames = { "SSAAImpl", "SSAA", "SSAAPropagator", "SSAAPropagatorOpaque" };
        private static readonly string[] SnowShaderFloatGlobalNames = { "_SurfaceSnowBias", "_SurfaceSnowIntensity", "_Opaqueness", "_SurfaceWetFactor", "_WetAngleMin", "_WetAngleMax", "_SnowTransition", "_MapDownscale", "_GlitterThreshold", "_GlitterIntensity", "_StormEnabled", "_DampingDistance" };
        private static readonly string[] SnowShaderVectorGlobalNames = { "_SpringSnowFactor", "_MapBoundsMin", "_MapBoundsMax", "_TextureTiling", "_NoiseTiling", "_ScreenNoiseTiling", "_SnowSpecularFactor", "_MapStart", "_MapScale" };
        private static readonly string[] SnowShaderColorGlobalNames = { "_SurfaceSnowDiffuseColor", "_SurfaceSnowSpecColor" };
        private static readonly string[] SnowShaderTextureGlobalNames = { "_AlbedoTex", "_NoiseMap", "_SpecularMap", "_BumpMap", "_SnowNormalMap", "_ScreenSpaceNormalAndAlphaMask", "_DisableSnowMask", "_swampSnowIgnoreMask", "wetOffMask", "_WeatherDepthMap" };
        private static readonly string[] TerrainMaterialTexturePropertyNames =
        {
            "_TopAlbedoASmoothness", "_TopNormalMap", "_BaseAlbedoASmoothness", "_BaseNormalMap",
            "_MainTex", "_MainTex0", "_MainTex1", "_MainTex2", "_BumpMap", "_BumpMap0", "_BumpMap1", "_BumpMap2",
            "_MainTexArray", "_NormalMapArray", "_Diffuse", "_DiffuseArray", "_NormalSAO", "_NormalArray",
            "_IndexTex", "_Control", "_Control0", "_Control1", "_Splat0", "_Splat1", "_Splat2", "_Splat3",
            "_SnowTex", "_SnowNormalMap", "_GlobalWetnessRT", "_WeatherDepthMap"
        };

        private static readonly string[] TerrainMaterialFloatPropertyNames =
        {
            "_SnowAmount", "_SnowLevel", "_SnowStrength", "_SnowHeight", "_SnowContrast", "_SnowBrightness",
            "_TopBlend", "_TopContrast", "_TopPower", "_Wetness", "_Opaqueness"
        };

        private static readonly string[] TerrainMaterialColorPropertyNames =
        {
            "_TopColor", "_Color", "_Color0", "_Color1", "_Color2", "_SnowColor"
        };
        private static readonly int[] PreviewMaterialColorPropertyIds =
        {
            Shader.PropertyToID("_Color"),
            Shader.PropertyToID("_MainColor"),
            Shader.PropertyToID("_TintColor")
        };
        private static readonly int[] PreviewMaterialEmissionPropertyIds =
        {
            Shader.PropertyToID("_EmissionColor"),
            Shader.PropertyToID("_EmissiveColor"),
            Shader.PropertyToID("_Emission")
        };

        private static bool _lookupAttempted;
        private static Type _itemType;
        private static Type _compoundItemType;
        private static Type _inventoryControllerType;
        private static Type _mongoIdType;
        private static Type _itemFactoryType;
        private static Type _flatItemsDataType;
        private static Type _unparsedDataType;
        private static Type _utilityApplicationType;
        private static Type _canvasType;
        private static ConstructorInfo _mongoIdCtor;
        private static ConstructorInfo _flatItemsDataCtor;
        private static ConstructorInfo _unparsedDataConstructor;
        private static FieldInfo _flatIdField;
        private static FieldInfo _flatTplField;
        private static FieldInfo _flatParentIdField;
        private static FieldInfo _flatSlotIdField;
        private static FieldInfo _flatLocationField;
        private static FieldInfo _flatUpdField;
        private static FieldInfo _unparsedDataTokenField;
        private static FieldInfo _utilityItemFactoryField;
        private static PropertyInfo _singletonInstanceProperty;
        private static MethodInfo _flatItemsToTreeMethod;
        private static string _reflectionLookupError;

        private static RaidEditBuildController _activeController;
        private static BepInEx.Logging.ManualLogSource _log;
        private static GamePlayerOwner _inventoryHostOwner;
        private static Player _inventoryHostPlayer;
        private static bool _inventoryHostActive;
        private static bool _forcedInventoryOpen;
        private static bool _forcedRaidInputIgnore;
        private static bool _battlePresentationApplied;
        private static bool _postOpenSetupPending;
        private static int _postOpenSetupPassesRemaining;
        private static float _nextPostOpenSetupAt;
        private static int _postCloseRestorePassesRemaining;
        private static float _nextPostCloseRestoreAt;
        private static int _postEditorCloseRestorePassesRemaining;
        private static float _nextPostEditorCloseRestoreAt;
        private static int _postPreviewInventoryToggleStage;
        private static float _nextPostPreviewInventoryToggleAt;
        private static float _previewOpenedAt;
        private static readonly Dictionary<int, int> AsyncPreviewSetupVersions = new Dictionary<int, int>();
        private static bool _backgroundSuppressionAttempted;
        private static bool _backdropDiagnosticsLogged;
        private static bool _cameraDiagnosticsLogged;
        private static bool _cameraImageDiagnosticsLogged;
        private static bool _previewPanelDiagnosticsLogged;
        private static bool _delayedPreviewDiagnosticsLogged;
        private static bool _battleCameraComponentDiagnosticsLogged;
        private static EditOpenTimingSession _editOpenTiming;
        private static EFT.UI.Screens.IBaseScreenController<EEftScreenType> _previewPreviousScreenController;
        private static bool _battleScreenContextRestored;
        private static bool _previewTransitionActive;
        private static readonly List<Light> CapturedEnabledSceneLights = new List<Light>(512);
        private static readonly List<LightDiagnosticsSnapshot> CapturedSceneLightDiagnostics = new List<LightDiagnosticsSnapshot>(512);
        private static readonly List<Light> RestoredSceneLights = new List<Light>(64);
        private static readonly List<PreviewLightSnapshot> SuppressedPreviewLights = new List<PreviewLightSnapshot>(16);
        private static readonly List<PreviewRendererMaterialSnapshot> BrightenedPreviewRenderers = new List<PreviewRendererMaterialSnapshot>(256);
        private static readonly RenderSettingsSnapshot PreviewRenderSettingsSnapshot = new RenderSettingsSnapshot();
        private static readonly List<CanvasStateSnapshot> HiddenBattleUiCanvases = new List<CanvasStateSnapshot>(8);
        private static readonly List<CanvasGroupStateSnapshot> HiddenBattleUiCanvasGroups = new List<CanvasGroupStateSnapshot>(4);
        private static bool _battleUiShellApplied;
        private static bool _raidInfoVisibilityChanged;
        private static Camera _configuredPreviewCamera;
        private static CameraClearFlags _previewCameraClearFlags;
        private static int _previewCameraCullingMask;
        private static Color _previewCameraBackgroundColor;
        private static float _previewCameraDepth;
        private static bool _previewCameraUseOcclusionCulling;
        private static Rect _previewCameraRect;
        private static RenderTexture _previewCameraTargetTexture;
        private static RenderingPath _previewCameraRenderingPath;
        private static readonly List<Camera> SuppressedAuxiliaryCameras = new List<Camera>(4);
        private static readonly List<Behaviour> SuppressedPreviewCameraBehaviours = new List<Behaviour>(8);
        private static readonly List<Behaviour> SuppressedBattleCameraBehaviours = new List<Behaviour>(8);
        private static readonly Dictionary<int, BattleCameraVisualSnapshot> CapturedBattleCameraVisualBehaviours = new Dictionary<int, BattleCameraVisualSnapshot>(64);
        private static readonly List<GameObject> SuppressedBackgroundObjects = new List<GameObject>(16);
        private static readonly List<Behaviour> SuppressedBackdropBehaviours = new List<Behaviour>(8);
        private static readonly List<Behaviour> SuppressedBackdropGraphics = new List<Behaviour>(16);
        private static readonly List<SnowRendererSnapshot> CapturedSnowRenderers = new List<SnowRendererSnapshot>(8);
        private static readonly List<TerrainVisualSnapshot> CapturedTerrainVisuals = new List<TerrainVisualSnapshot>(32);
        private static RenderTexture _forcedPreviewRenderTexture;
        private static RawImage _forcedPreviewRawImage;
        private static RectTransform _forcedPreviewHost;
        private static int _forcedPreviewWidth;
        private static int _forcedPreviewHeight;
        private static bool _forcedPreviewRenderTextureLogged;
        private static bool _previewCameraPreCullRegistered;
        private static bool _previewCameraPreRenderRegistered;
        private static bool _previewCameraPostRenderRegistered;
        private static bool _previewCameraAmbientOverrideActive;
        private static bool _previewCameraWeaponPreviewKeywordWasEnabled;
        private static readonly RenderSettingsSnapshot PreviewCameraAmbientSnapshot = new RenderSettingsSnapshot();
        private static Camera _battleCamera;
        private static Vector3 _battleCameraPosition;
        private static Quaternion _battleCameraRotation;
        private static Rect _battleCameraRect;
        private static RenderTexture _battleCameraTargetTexture;
        private static Matrix4x4 _battleCameraProjectionMatrix;
        private static bool _battleCameraStateCaptured;
        private static bool _battleCameraPreCullRegistered;
        private static bool _battleCameraPreRenderRegistered;
        private static int _lastGpuInstancerCameraPinFrame = -1;
        private static bool _snowGlittersKeywordWasEnabled;
        private static bool _winterSnowKeywordWasEnabled;
        private static bool _snowMaskWasDisabled;
        private static bool _rainStateCaptured;
        private static float _capturedRainWetting;
        private static float _capturedRainOpaqueness;
        private static float _capturedRainIntensity;
        private static FieldInfo _snowRendererDirtyField;
        private static int _previewSnowPreCullEventCount;
        private static int _previewSnowPreCullAcceptedCameraCount;
        private static int _previewSnowPreCullAcceptedMainCameraCount;
        private static int _previewSnowPreCullAcceptedOpticCameraCount;
        private static int _previewSnowPreCullCapturedBattleCameraCount;
        private static int _previewSnowPreCullLooksBattleCameraCount;
        private static int _previewSnowPreCullPreviewCameraCount;
        private static int _previewSnowPreCullUiCameraCount;
        private static int _previewSnowPreCullOtherCameraCount;
        private static string _lastPreviewSnowPreCullCameraPath;
        private static string _lastPreviewSnowPreCullCameraTag;
        private static bool _lastPreviewSnowPreCullTreatedAsBattleCamera;
        private static readonly Dictionary<string, bool> RuntimeAttachableSupportByTpl = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static int _previewSnowMaskDisableRequestCount;
        private static bool? _lastPreviewSnowMaskDisableRequest;
        private static int _lastManualSnowRenderFrame = -1;
        private static Camera _snowCommandBufferCameraOverride;
        private static int _snowCommandBufferCameraOverrideCount;
        private static string _lastSnowCommandBufferCameraOverridePath;
        private static int _terrainSeasonForceCount;
        private static int _terrainSeasonRefreshCount;
        private static int _terrainSeasonMaterialFixCount;
        private static string _lastTerrainSeasonState;
        private static bool _terrainSeasonRefreshAttempted;
        private static float _nextTerrainSeasonRefreshAt;
        private static float _nextSnowPresentationForceAt;
        private static int _snowPresentationForceCount;
        private static bool _snowCommandBuffersRebuiltForPreview;
        private static int _snowCommandBufferRebuildCount;
        private static string _lastSnowCommandBufferRebuildState;
        private static bool _seasonMaterialsPreviewLoadStarted;
        private static bool _seasonMaterialsPreviewFixApplied;
        private static Task _seasonMaterialsPreviewLoadTask;
        private static float _nextSeasonMaterialsPreviewPollAt;
        private static int _seasonMaterialsPreviewFixCount;
        private static string _seasonMaterialsPreviewExpectedName;
        private static string _lastSeasonMaterialsPreviewState;
        private static string _capturedBattleCameraVisualState;
        private static string _capturedBattleCameraShaderKeywords;
        private static string _capturedBattleCameraCommandBuffers;
        private static readonly List<object> SeasonMaterialsPreviewComponents = new List<object>(16);
        private static readonly Dictionary<string, string> CapturedTerrainMaterialPropertyStates = new Dictionary<string, string>(StringComparer.Ordinal);
        private static string _lastTerrainMaterialPropertySnapshotState;
        private static Camera _manualSnowRenderCamera;
        private static LootItem _previewSourceItem;
        private static LootItem _previewOriginalItem;
        private static CompoundItem _openingPreviewItem;
        private static EFT.IEftSession _openingPreviewSession;
        private static bool _previewApplyOnClose;
        private static bool _previewEditableManipulationInstalled;
        private static bool _previewEditBuildActionsRestricted;
        private static float _runtimeAttachmentPoolInstallAt;
        private static EFT.InventoryLogic.ItemController _cachedEditBuildTraderController;
        private static EFT.ItemFactory _cachedItemFactory;
        private static int _cachedArmorPlateTemplateSourceCount = -1;
        private static string[] _cachedArmorPlateTemplateIds = Array.Empty<string>();
        private static bool _lastPreviewAppliedChanges;
        private static string _lastPreviewEditMessage;
        private readonly struct SnowRendererSnapshot
        {
            public SnowRendererSnapshot(
                SnowWetRenderer renderer,
                bool winterShow,
                bool enabled,
                float wetting,
                float opaqueness,
                Vector3 springSnowFactor,
                bool stormEnabled)
            {
                Renderer = renderer;
                WinterShow = winterShow;
                Enabled = enabled;
                Wetting = wetting;
                Opaqueness = opaqueness;
                SpringSnowFactor = springSnowFactor;
                StormEnabled = stormEnabled;
            }

            public SnowWetRenderer Renderer { get; }
            public bool WinterShow { get; }
            public bool Enabled { get; }
            public float Wetting { get; }
            public float Opaqueness { get; }
            public Vector3 SpringSnowFactor { get; }
            public bool StormEnabled { get; }
        }

        private readonly struct TerrainVisualSnapshot
        {
            public TerrainVisualSnapshot(Terrain terrain)
            {
                Terrain = terrain;
                Enabled = terrain != null && terrain.enabled;
                DrawHeightmap = terrain == null || terrain.drawHeightmap;
                DrawTreesAndFoliage = terrain == null || terrain.drawTreesAndFoliage;
                DetailObjectDensity = terrain != null ? terrain.detailObjectDensity : 1f;
                DetailObjectDistance = terrain != null ? terrain.detailObjectDistance : 0f;
                TreeDistance = terrain != null ? terrain.treeDistance : 0f;
                TreeBillboardDistance = terrain != null ? terrain.treeBillboardDistance : 0f;
                TerrainLod = terrain != null ? terrain.GetComponent<TerrainLod>() : null;
                TerrainLodVisible = TerrainLod == null || TerrainLod.TerrainIsVisible;
            }

            public Terrain Terrain { get; }
            public bool Enabled { get; }
            public bool DrawHeightmap { get; }
            public bool DrawTreesAndFoliage { get; }
            public float DetailObjectDensity { get; }
            public float DetailObjectDistance { get; }
            public float TreeDistance { get; }
            public float TreeBillboardDistance { get; }
            public TerrainLod TerrainLod { get; }
            public bool TerrainLodVisible { get; }

            public void Restore()
            {
                if (Terrain == null)
                {
                    return;
                }

                Terrain.enabled = Enabled;
                Terrain.drawHeightmap = DrawHeightmap;
                Terrain.drawTreesAndFoliage = DrawTreesAndFoliage;
                Terrain.detailObjectDensity = DetailObjectDensity;
                Terrain.detailObjectDistance = DetailObjectDistance;
                Terrain.treeDistance = TreeDistance;
                Terrain.treeBillboardDistance = TreeBillboardDistance;

                if (TerrainLod != null && TerrainLod.TerrainIsVisible != TerrainLodVisible)
                {
                    TerrainLod.TerrainIsVisible = TerrainLodVisible;
                }
            }
        }

        private readonly struct BattleCameraVisualSnapshot
        {
            public BattleCameraVisualSnapshot(string typeName, bool enabled)
            {
                TypeName = typeName;
                Enabled = enabled;
            }

            public string TypeName { get; }
            public bool Enabled { get; }
        }

        private readonly struct CanvasStateSnapshot
        {
            public CanvasStateSnapshot(Canvas canvas, bool enabled)
            {
                Canvas = canvas;
                Enabled = enabled;
            }

            public Canvas Canvas { get; }
            public bool Enabled { get; }
        }

        private readonly struct CanvasGroupStateSnapshot
        {
            public CanvasGroupStateSnapshot(
                CanvasGroup canvasGroup,
                float alpha,
                bool interactable,
                bool blocksRaycasts,
                bool ignoreParentGroups,
                bool addedByPreview)
            {
                CanvasGroup = canvasGroup;
                Alpha = alpha;
                Interactable = interactable;
                BlocksRaycasts = blocksRaycasts;
                IgnoreParentGroups = ignoreParentGroups;
                AddedByPreview = addedByPreview;
            }

            public CanvasGroup CanvasGroup { get; }
            public float Alpha { get; }
            public bool Interactable { get; }
            public bool BlocksRaycasts { get; }
            public bool IgnoreParentGroups { get; }
            public bool AddedByPreview { get; }
        }

        public static bool CanOpen(LootItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Tpl))
            {
                return false;
            }

            if (LootItemTreeValidator.HasMissingTemplates(item))
            {
                return false;
            }

            if (TplCache.SupportsPresetPreview(item.Tpl))
            {
                return true;
            }

            return RuntimeSupportsAttachmentEditing(item.Tpl);
        }

        private static bool RuntimeSupportsAttachmentEditing(string tpl)
        {
            if (string.IsNullOrWhiteSpace(tpl))
            {
                return false;
            }

            if (RuntimeAttachableSupportByTpl.TryGetValue(tpl, out var cached))
            {
                return cached;
            }

            var supported = false;
            try
            {
                if (EnsureReflection(null, out _) &&
                    TryResolveItemFactory(out var itemFactoryObject, out _) &&
                    itemFactoryObject is EFT.ItemFactory itemFactory)
                {
                    var item = itemFactory.CreateItem(GenerateMongoId(), tpl, null);
                    supported = IsSupportedEditableRootItem(item) &&
                                item is CompoundItem compound &&
                                compound.AllSlots != null &&
                                compound.AllSlots.Any();
                }
            }
            catch
            {
                supported = false;
            }

            RuntimeAttachableSupportByTpl[tpl] = supported;
            return supported;
        }

        private static bool IsSupportedEditableRootItem(Item item)
        {
            if (item == null)
            {
                return false;
            }

            if (item is Weapon)
            {
                return true;
            }

            if (item is EFT.InventoryLogic.ArmorPlate)
            {
                return false;
            }

            if (item is EFT.InventoryLogic.Armor || item is EFT.InventoryLogic.Headwear)
            {
                return true;
            }

            return item is EFT.InventoryLogic.Vest && HasEditableArmorComponents(item);
        }

        private static bool HasEditableArmorComponents(Item item)
        {
            return item != null &&
                   (item.GetItemComponent<ArmorComponent>() != null ||
                    item.GetItemComponent<ArmorHolderComponent>() != null ||
                    item.GetItemComponent<HelmetComponent>() != null);
        }

        public static bool TryCreateRuntimeItem(LootItem item, BepInEx.Logging.ManualLogSource log, out Item runtimeItem, out string error)
        {
            runtimeItem = null;
            error = null;

            if (!CanOpen(item))
            {
                error = "This item does not support attachment editing.";
                return false;
            }

            if (!EnsureReflection(log, out error))
            {
                return false;
            }

            if (!TryBuildRuntimePresetItem(item, log, out var runtimeRoot, out error))
            {
                return false;
            }

            runtimeItem = runtimeRoot as Item;
            if (runtimeItem == null)
            {
                error = "Tarkov did not return a compatible runtime item.";
                return false;
            }

            return true;
        }

        public static bool CanCreateWorldPreview(LootItem item)
        {
            return item != null &&
                   !string.IsNullOrWhiteSpace(item.Tpl) &&
                   !LootItemTreeValidator.HasMissingTemplates(item);
        }

        internal static async Task<WorldPreviewObject> CreateWorldPreviewObjectAsync(LootItem item, BepInEx.Logging.ManualLogSource log)
        {
            var description = DescribeLootItem(item);
            if (!CanCreateWorldPreview(item))
            {
                return WorldPreviewObject.Failed($"Cannot preview {description}: one or more templates are missing.");
            }

            if (!EnsureReflection(log, out var error))
            {
                return WorldPreviewObject.Failed($"Cannot preview {description}: {FallbackError(error, "preview bridge reflection setup failed")}");
            }

            if (!TryBuildRuntimePresetItem(item, log, out var runtimeRoot, out error))
            {
                return WorldPreviewObject.Failed($"Cannot preview {description}: {FallbackError(error, "runtime item build failed")}");
            }

            if (runtimeRoot is not Item runtimeItem)
            {
                return WorldPreviewObject.Failed($"Cannot preview {description}: Tarkov did not return a compatible runtime item.");
            }

            object bundleTokens = null;
            GameObject prefab = null;
            try
            {
                var tokens = runtimeItem.GetAllBundleTokens();
                bundleTokens = tokens;
                await EFT.EasyAssetsExtensions.LoadBundles(tokens);

                var poolManager = Singleton<EFT.ObjectsFactory>.Instance;
                if (poolManager == null)
                {
                    TryReleaseBundleTokens(bundleTokens);
                    return WorldPreviewObject.Failed($"Cannot preview {description}: Tarkov's loot prefab pool is not ready.");
                }

                prefab = await poolManager.CreateCleanLootPrefabAsync(runtimeItem, null);
                if (prefab == null)
                {
                    TryReleaseBundleTokens(bundleTokens);
                    return WorldPreviewObject.Failed($"Cannot preview {description}: Tarkov returned an empty item preview object.");
                }

                prefab.SetActive(false);
                return WorldPreviewObject.Success(prefab, bundleTokens);
            }
            catch (Exception ex)
            {
                TryReleaseBundleTokens(bundleTokens);
                ReturnPreviewPrefab(prefab);
                var unwrapped = Unwrap(ex);
                return WorldPreviewObject.Failed($"Cannot preview {description}: prefab creation failed ({unwrapped.GetType().Name}: {unwrapped.Message}).");
            }
        }

        private static string DescribeLootItem(LootItem item)
        {
            if (item == null)
            {
                return "item <null>";
            }

            var name = !string.IsNullOrWhiteSpace(item.PresetName)
                ? item.PresetName
                : "item";
            return $"{name} [{item.Tpl ?? "<no tpl>"}]";
        }

        private static string FallbackError(string error, string fallback)
        {
            return string.IsNullOrWhiteSpace(error) ? fallback : error;
        }

        internal static bool TrySetupAsyncWeaponPreview(
            WeaponPreview preview,
            Item item,
            Action onLoadingStart,
            Action onLoadingFinished,
            Callback onFinished,
            bool setAsClosest,
            Vector3? initialRotation,
            bool enableWeaponLights)
        {
            if (!EnableAsyncEditBuildPreviewSetup ||
                preview == null ||
                item == null ||
                !IsPreviewTransitionActiveOrOpen)
            {
                return false;
            }

            var version = IncrementAsyncPreviewSetupVersion(preview);
            _ = SetupAsyncWeaponPreview(
                preview,
                item,
                onLoadingStart,
                onLoadingFinished,
                onFinished,
                setAsClosest,
                initialRotation,
                enableWeaponLights,
                version);
            return true;
        }

        internal static void CancelAsyncWeaponPreviewSetup(WeaponPreview preview)
        {
            if (preview == null)
            {
                return;
            }

            IncrementAsyncPreviewSetupVersion(preview);
        }

        internal static void ResetRaidVisualCaches()
        {
            ResetSeasonMaterialsPreviewFixState();
        }

        private static void LogDebug(string message)
        {
            if (Plugin.DebugLoggingEnabled)
            {
                _log?.LogInfo(message);
            }
        }

        private static void LogDebug(BepInEx.Logging.ManualLogSource log, string message)
        {
            if (Plugin.DebugLoggingEnabled)
            {
                log?.LogInfo(message);
            }
        }

        internal static bool TryWarmRaidSeasonMaterialsForPreview(out string state)
        {
            state = null;

            if (IsPreviewTransitionActiveOrOpen)
            {
                state = "previewActive=True";
                return false;
            }

            if (_seasonMaterialsPreviewFixApplied)
            {
                state = _lastSeasonMaterialsPreviewState ?? "alreadyApplied=True";
                return true;
            }

            var stopwatch = Stopwatch.StartNew();
            var warmed = TryProcessSeasonMaterialsPreviewFixCore(
                requirePreviewOpen: false,
                ignoreThrottle: true,
                requireLoadedComponents: true);
            stopwatch.Stop();

            state = $"{(_lastSeasonMaterialsPreviewState ?? "<no-state>")} elapsed={stopwatch.Elapsed.TotalMilliseconds:0.0}ms";
            return warmed;
        }

        public static bool IsPreviewOpen
        {
            get
            {
                Poll();
                return _activeController != null;
            }
        }

        internal static void Tick()
        {
            Poll();
        }

        internal static bool IsPreviewTransitionActiveOrOpen => _previewTransitionActive || _activeController != null;

        internal static bool ShouldDisableWeaponPreviewItemLights =>
            IsPreviewTransitionActiveOrOpen && SuppressEditBuildPreviewLightsDuringRaid;

        internal static long BeginEditOpenTimingStage(string stage)
        {
            if (!ShouldRecordEditOpenTiming)
            {
                return 0L;
            }

            _editOpenTiming.Mark(stage + ".begin");
            return Stopwatch.GetTimestamp();
        }

        internal static void EndEditOpenTimingStage(string stage, long startTicks)
        {
            if (!ShouldRecordEditOpenTiming || startTicks == 0L)
            {
                return;
            }

            var elapsedMs = StopwatchTicksToMilliseconds(Stopwatch.GetTimestamp() - startTicks);
            _editOpenTiming.Mark($"{stage}.end ({elapsedMs:F1} ms)");
        }

        internal static void MarkEditOpenTiming(string step)
        {
            if (!ShouldRecordEditOpenTiming)
            {
                return;
            }

            _editOpenTiming.Mark(step);
        }

        internal static Action WrapEditOpenTimingAction(Action action, string stage)
        {
            if (!ShouldRecordEditOpenTiming)
            {
                return action;
            }

            return () =>
            {
                var startTicks = BeginEditOpenTimingStage(stage);
                try
                {
                    action?.Invoke();
                }
                finally
                {
                    EndEditOpenTimingStage(stage, startTicks);
                }
            };
        }

        internal static void CompleteEditOpenTiming(string reason)
        {
            if (_editOpenTiming == null)
            {
                return;
            }

            _editOpenTiming.Report(reason);
        }

        private static bool ShouldRecordEditOpenTiming =>
            EnableEditOpenTimingDiagnostics && _editOpenTiming != null && !_editOpenTiming.Reported;

        private static void BeginEditOpenTiming(LootItem item, BepInEx.Logging.ManualLogSource log, bool applyOnClose)
        {
            if (!EnableEditOpenTimingDiagnostics)
            {
                return;
            }

            _editOpenTiming = new EditOpenTimingSession(log, item, applyOnClose);
            _editOpenTiming.Mark("TryOpen.begin");
        }

        private static void TryReportStaleEditOpenTiming()
        {
            if (!ShouldRecordEditOpenTiming || _editOpenTiming.ElapsedSeconds < 8f)
            {
                return;
            }

            _editOpenTiming.Report("timeout waiting for slot icons");
        }

        private static float StopwatchTicksToMilliseconds(long ticks)
        {
            return ticks * 1000f / Stopwatch.Frequency;
        }

        internal static void TryReuseCachedEditBuildTrader(EditBuildScreen screen)
        {
            if (!IsPreviewTransitionActiveOrOpen || screen == null || _cachedEditBuildTraderController == null)
            {
                return;
            }

            try
            {
                SetFieldValue(screen, "_allItemsFakeController", _cachedEditBuildTraderController);
            }
            catch
            {
                // Cache reuse is only an open-speed optimization.
            }
        }

        internal static void TryCaptureCachedEditBuildTrader(EditBuildScreen screen)
        {
            if (!IsPreviewTransitionActiveOrOpen || screen == null || _cachedEditBuildTraderController != null)
            {
                return;
            }

            try
            {
                _cachedEditBuildTraderController = GetFieldValue(screen, "_allItemsFakeController") as EFT.InventoryLogic.ItemController;
            }
            catch
            {
                // Cache capture is only an open-speed optimization.
            }
        }

        internal static bool ShouldPreserveWinterSnowMaskDuringPreview =>
            _previewTransitionActive || _activeController != null;

        internal static bool ShouldBypassAmbientResetDuringPreview =>
            _previewTransitionActive || _activeController != null;

        public static bool ConsumeLastAppliedChanges(out string message)
        {
            message = _lastPreviewEditMessage;
            var applied = _lastPreviewAppliedChanges;
            _lastPreviewAppliedChanges = false;
            _lastPreviewEditMessage = null;
            return applied;
        }

        internal static void RecordSnowPreCullCamera(Camera currentCamera)
        {
            if (!IsPreviewTransitionActiveOrOpen)
            {
                return;
            }

            try
            {
                _previewSnowPreCullEventCount++;
                _lastPreviewSnowPreCullCameraPath = currentCamera != null ? GetTransformPath(currentCamera.transform) : "<null>";
                _lastPreviewSnowPreCullCameraTag = TryGetCameraTag(currentCamera);
                _lastPreviewSnowPreCullTreatedAsBattleCamera = ShouldTreatCameraAsBattleWorldCamera(currentCamera);

                var acceptedMain = false;
                var acceptedOptic = false;
                if (IsSnowWetRendererAcceptedCamera(currentCamera, out acceptedMain, out acceptedOptic))
                {
                    _previewSnowPreCullAcceptedCameraCount++;
                    if (acceptedMain)
                    {
                        _previewSnowPreCullAcceptedMainCameraCount++;
                    }

                    if (acceptedOptic)
                    {
                        _previewSnowPreCullAcceptedOpticCameraCount++;
                    }
                }

                if (_lastPreviewSnowPreCullTreatedAsBattleCamera)
                {
                    _previewSnowPreCullCapturedBattleCameraCount++;
                }

                if (LooksLikeBattleWorldCamera(currentCamera))
                {
                    _previewSnowPreCullLooksBattleCameraCount++;
                }

                if (IsConfiguredPreviewCamera(currentCamera))
                {
                    _previewSnowPreCullPreviewCameraCount++;
                }
                else if (IsKnownUiCamera(currentCamera))
                {
                    _previewSnowPreCullUiCameraCount++;
                }
                else if (!_lastPreviewSnowPreCullTreatedAsBattleCamera)
                {
                    _previewSnowPreCullOtherCameraCount++;
                }
            }
            catch
            {
                // Diagnostics only.
            }
        }

        internal static void RecordSnowMaskDisableRequest(bool disable)
        {
            if (!IsPreviewTransitionActiveOrOpen)
            {
                return;
            }

            _previewSnowMaskDisableRequestCount++;
            _lastPreviewSnowMaskDisableRequest = disable;
        }

        internal static bool TryGetCapturedSnowState(
            SnowWetRenderer renderer,
            out bool winterShow,
            out bool enabled,
            out float wetting,
            out float opaqueness,
            out Vector3 springSnowFactor,
            out bool stormEnabled)
        {
            winterShow = false;
            enabled = false;
            wetting = 0f;
            opaqueness = 0f;
            springSnowFactor = default;
            stormEnabled = false;

            if (renderer == null || CapturedSnowRenderers.Count == 0)
            {
                return false;
            }

            foreach (var snapshot in CapturedSnowRenderers)
            {
                if (!ReferenceEquals(snapshot.Renderer, renderer))
                {
                    continue;
                }

                winterShow = snapshot.WinterShow;
                enabled = snapshot.Enabled;
                wetting = snapshot.Wetting;
                opaqueness = snapshot.Opaqueness;
                springSnowFactor = snapshot.SpringSnowFactor;
                stormEnabled = snapshot.StormEnabled;
                return true;
            }

            return false;
        }

        internal static bool TryGetManualSnowRenderCamera(out Camera camera)
        {
            camera = _manualSnowRenderCamera;
            return IsPreviewTransitionActiveOrOpen && camera != null;
        }

        internal static bool TryGetSnowCommandBufferCameraOverride(out Camera camera)
        {
            camera = _snowCommandBufferCameraOverride;
            return IsPreviewTransitionActiveOrOpen && camera != null;
        }

        internal static void BeginSnowCommandBufferCameraOverride(Camera currentCamera)
        {
            if (!ForceSnowCommandBufferCameraOverride ||
                !IsPreviewTransitionActiveOrOpen ||
                currentCamera == null ||
                !IsSnowWetRendererAcceptedCamera(currentCamera, out _, out _))
            {
                return;
            }

            _snowCommandBufferCameraOverride = currentCamera;
            _snowCommandBufferCameraOverrideCount++;
            _lastSnowCommandBufferCameraOverridePath = GetTransformPath(currentCamera.transform);
        }

        internal static void EndSnowCommandBufferCameraOverride(Camera currentCamera)
        {
            if (currentCamera == null || ReferenceEquals(currentCamera, _snowCommandBufferCameraOverride))
            {
                _snowCommandBufferCameraOverride = null;
            }
        }

        internal static void HandleWeaponModdingScreenShown(WeaponModdingScreen screen)
        {
            if (_activeController == null || screen == null)
            {
                return;
            }

            try
            {
                RestoreSceneLights(screen);
                PreviewRenderSettingsSnapshot.Restore();
                RestoreToggleBorders(screen);
                _backgroundSuppressionAttempted = false;
                _postOpenSetupPending = true;
                _postOpenSetupPassesRemaining = Math.Max(_postOpenSetupPassesRemaining, PostOpenSetupPassCount);
                _nextPostOpenSetupAt = Time.unscaledTime;
            }
            catch
            {
                // Best-effort recovery only.
            }
        }

        internal static void HandleWeaponModdingScreenClosed(WeaponModdingScreen screen)
        {
            if (_activeController == null && !_previewTransitionActive && !_battlePresentationApplied)
            {
                return;
            }

            try
            {
                RestoreAfterPreviewClose("screen close hook");
            }
            catch
            {
                // Best-effort recovery only.
            }
        }

        internal static void HandleEditBuildScreenShown(EditBuildScreen screen)
        {
            if (screen == null || (_activeController == null && !_previewTransitionActive))
            {
                return;
            }

            try
            {
                RestoreSceneLights(screen);
                RestoreTerrainVisualsDuringPreview();
                PreviewRenderSettingsSnapshot.Restore();
                RestoreToggleBorders(screen);
                TryPinGpuInstancerCamera(GetBattleCamera());
                TryApplyLightFreePreviewIllumination(screen);
                TryForceSnowPresentation();
                if (_activeController != null)
                {
                    TryPrepareEditBuildScreen(screen);
                }

                TryApplyLightFreePreviewIllumination(screen);
                TryForceSnowPresentation();
                _backgroundSuppressionAttempted = false;
                _postOpenSetupPending = true;
                _postOpenSetupPassesRemaining = Math.Max(_postOpenSetupPassesRemaining, PostOpenSetupPassCount);
                _nextPostOpenSetupAt = Time.unscaledTime;
            }
            catch
            {
                // Best-effort recovery only.
            }
        }

        internal static void HandleEditBuildScreenClosed(EditBuildScreen screen)
        {
            if (_activeController == null && !_previewTransitionActive && !_battlePresentationApplied)
            {
                return;
            }

            try
            {
                RestoreAfterPreviewClose("edit-build screen close hook");
            }
            catch
            {
                // Best-effort recovery only.
            }
        }

        private static bool TryResolveBackendSession(out EFT.IEftSession session, out string error)
        {
            session = null;
            error = null;

            try
            {
                session = Singleton<ClientApplication<EFT.IEftSession>>.Instance?.GetClientBackEndSession();
            }
            catch (Exception ex)
            {
                error = $"Tarkov session is not ready: {Unwrap(ex).Message}";
                return false;
            }

            if (session?.Profile == null)
            {
                error = "Tarkov session profile is not ready yet.";
                return false;
            }

            if (session.RagFair == null || session.WeaponBuildsStorage == null || session.InsuranceCompany == null)
            {
                error = "Tarkov edit-build services are not ready yet.";
                return false;
            }

            return true;
        }

        public static bool TryOpen(LootItem item, BepInEx.Logging.ManualLogSource log, out string error)
        {
            return TryOpen(item, log, applyOnClose: false, out error);
        }

        public static bool TryOpen(LootItem item, BepInEx.Logging.ManualLogSource log, bool applyOnClose, out string error)
        {
            error = null;
            Poll();
            _lastPreviewAppliedChanges = false;
            _lastPreviewEditMessage = null;

            if (!CanOpen(item))
            {
                error = "This item does not support attachment editing.";
                return false;
            }

            if (_activeController != null)
            {
                error = "Close the current preset preview before opening another one.";
                return false;
            }

            BeginEditOpenTiming(item, log, applyOnClose);

            var reflectionTicks = BeginEditOpenTimingStage("EnsureReflection");
            if (!EnsureReflection(log, out error))
            {
                EndEditOpenTimingStage("EnsureReflection", reflectionTicks);
                CompleteEditOpenTiming("failed: reflection unavailable");
                return false;
            }
            EndEditOpenTimingStage("EnsureReflection", reflectionTicks);

            MarkEditOpenTiming("resolve player inventory begin");
            var player = GamePlayerOwner.MyPlayer;
            var inventoryController = player?.InventoryController;
            if (inventoryController == null)
            {
                error = "Inventory controller is not ready yet.";
                CompleteEditOpenTiming("failed: inventory controller unavailable");
                return false;
            }
            MarkEditOpenTiming("resolve player inventory end");

            var sessionTicks = BeginEditOpenTimingStage("ResolveBackendSession");
            if (!TryResolveBackendSession(out var session, out error))
            {
                EndEditOpenTimingStage("ResolveBackendSession", sessionTicks);
                CompleteEditOpenTiming("failed: backend session unavailable");
                return false;
            }
            EndEditOpenTimingStage("ResolveBackendSession", sessionTicks);

            var runtimeBuildTicks = BeginEditOpenTimingStage("BuildRuntimePresetItem");
            if (!TryBuildRuntimePresetItem(item, log, out var runtimeRoot, out error))
            {
                EndEditOpenTimingStage("BuildRuntimePresetItem", runtimeBuildTicks);
                CompleteEditOpenTiming("failed: runtime item build");
                return false;
            }
            EndEditOpenTimingStage("BuildRuntimePresetItem", runtimeBuildTicks);

            if (runtimeRoot is not CompoundItem compoundRoot)
            {
                error = "The preset root item is not compatible with Tarkov's preview screen.";
                CompleteEditOpenTiming("failed: runtime root not compound");
                return false;
            }

            LootItem normalizedOriginalItem = null;
            if (applyOnClose && compoundRoot is not Weapon)
            {
                var captureTicks = BeginEditOpenTimingStage("CaptureOriginalRuntimeLootItem");
                TryCaptureRuntimeLootItem(compoundRoot, item, out normalizedOriginalItem, out _);
                EndEditOpenTimingStage("CaptureOriginalRuntimeLootItem", captureTicks);
            }

            try
            {
                MarkEditOpenTiming("capture render settings begin");
                PreviewRenderSettingsSnapshot.Clear();
                PreviewRenderSettingsSnapshot.Capture();
                if (EnablePreviewDiagnostics)
                {
                    CaptureTerrainMaterialPropertiesBeforePreview();
                }
                _previewPreviousScreenController = EFT.UI.Screens.EftScreenManager.Instance?.CurrentBaseScreenController;
                _battleScreenContextRestored = false;
                _previewTransitionActive = true;
                _previewSourceItem = applyOnClose ? item : null;
                _previewOriginalItem = applyOnClose ? normalizedOriginalItem ?? item.Clone() : null;
                _previewApplyOnClose = applyOnClose;
                _previewEditableManipulationInstalled = false;
                _previewEditBuildActionsRestricted = false;
                _previewOpenedAt = Time.unscaledTime;
                _runtimeAttachmentPoolInstallAt = _previewOpenedAt + RuntimeAttachmentPoolInstallDelay;
                _openingPreviewItem = compoundRoot;
                _openingPreviewSession = session;
                MarkEditOpenTiming("capture render settings end");

                var presentationTicks = BeginEditOpenTimingStage("ApplyBattleSafePresentation");
                CaptureSceneLightsBeforePreview();
                CaptureTerrainVisualsBeforePreview();
                ApplyBattleSafePresentation();
                EndEditOpenTimingStage("ApplyBattleSafePresentation", presentationTicks);

                var controller = new RaidEditBuildController(compoundRoot, inventoryController, session);
                var showQueuedTicks = BeginEditOpenTimingStage("RaidEditBuildController.TryShowQueued");
                if (!controller.TryShowQueued(out error))
                {
                    EndEditOpenTimingStage("RaidEditBuildController.TryShowQueued", showQueuedTicks);
                    _previewTransitionActive = false;
                    _openingPreviewItem = null;
                    _openingPreviewSession = null;
                    ClearActivePreviewEditState();
                    RestoreBattlePresentation();
                    CompleteEditOpenTiming("failed: TryShowQueued");
                    return false;
                }
                EndEditOpenTimingStage("RaidEditBuildController.TryShowQueued", showQueuedTicks);

                _activeController = controller;
                _previewTransitionActive = false;
                _log = log;
                var immediatePresentationTicks = BeginEditOpenTimingStage("ApplyImmediateEditBuildPresentation");
                TryApplyImmediateEditBuildPresentation();
                EndEditOpenTimingStage("ApplyImmediateEditBuildPresentation", immediatePresentationTicks);
                _postCloseRestorePassesRemaining = 0;
                _postOpenSetupPending = true;
                _postOpenSetupPassesRemaining = PostOpenSetupPassCount;
                _nextPostOpenSetupAt = Time.unscaledTime;
                _backdropDiagnosticsLogged = false;
                _cameraDiagnosticsLogged = false;
                _cameraImageDiagnosticsLogged = false;
                _previewPanelDiagnosticsLogged = false;
                _delayedPreviewDiagnosticsLogged = false;
                _battleCameraComponentDiagnosticsLogged = false;
                ResetPreviewSnowDiagnostics();
                MarkEditOpenTiming("TryOpen.return true");
                return true;
            }
            catch (Exception ex)
            {
                _previewTransitionActive = false;
                _openingPreviewItem = null;
                _openingPreviewSession = null;
                ClearActivePreviewEditState();
                _previewPreviousScreenController = null;
                _battleScreenContextRestored = false;
                error = $"Failed to open preset preview: {Unwrap(ex).Message}";
                log?.LogWarning($"[ULE] {error}");
                CompleteEditOpenTiming("failed: TryOpen exception");
                return false;
            }
        }

        public static void Poll()
        {
            if (_activeController == null)
            {
                TryProcessPendingBattleRestore();
                TryProcessPendingEditorCloseRestore();
                TryProcessPendingPreviewInventoryToggle();
                return;
            }

            try
            {
                TryReportStaleEditOpenTiming();

                if (_postOpenSetupPending && Time.unscaledTime >= _nextPostOpenSetupAt)
                {
                    var screen = GetLiveEditBuildScreen();
                     if (screen != null && screen.gameObject.activeInHierarchy)
                     {
                         TryRestoreBattleScreenContext();
                         TryApplyBattleUiShell();
                         RestoreSceneLights(screen);
                         RestoreTerrainVisualsDuringPreview();
                          RestoreToggleBorders(screen);
                          var battleCamera = GetBattleCamera();
                          TryPinGpuInstancerCamera(battleCamera);
                          TryKeepBattleCameraStable(battleCamera);
                          TryConfigurePreviewCamera();
                          TryApplyLightFreePreviewIllumination(screen);
                          TryForceSnowPresentation();
                          TryProcessSeasonMaterialsPreviewFix();
                          TryPrepareEditBuildScreen(screen);
                          TrySuppressScreenBackground();
                          TrySuppressCameraBackedScreenBackdrops(screen);
                      }

                    PreviewRenderSettingsSnapshot.Restore();
                    if (EnablePreviewDiagnostics)
                    {
                        TryLogRemainingBackdropCandidates();
                        TryLogCameraDiagnostics();
                        TryLogCameraImageDiagnostics(screen);
                        TryLogPreviewPanelDiagnostics(screen);
                        TryLogPreviewContext(screen);
                    }

                    _postOpenSetupPassesRemaining--;
                    if (_postOpenSetupPassesRemaining <= 0)
                    {
                        _postOpenSetupPending = false;
                    }
                    else
                    {
                        _nextPostOpenSetupAt = Time.unscaledTime + PostOpenSetupInterval;
                    }
                }

                if (!_postOpenSetupPending)
                {
                    var screen = GetLiveEditBuildScreen();
                    if (Time.unscaledTime - _previewOpenedAt > 0.5f &&
                        (screen == null || !screen.gameObject.activeInHierarchy))
                    {
                        RestoreAfterPreviewClose("screen inactive");
                        return;
                    }

                    if (screen != null && screen.gameObject.activeInHierarchy)
                    {
                        TryRestoreBattleScreenContext();
                        TryApplyBattleUiShell();
                        RestoreSceneLights(screen);
                        RestoreTerrainVisualsDuringPreview();
                        RestoreToggleBorders(screen);
                        var battleCamera = GetBattleCamera();
                        TryPinGpuInstancerCamera(battleCamera);
                        TryKeepBattleCameraStable(battleCamera);
                        TryApplyLightFreePreviewIllumination(screen);
                        TryForceSnowPresentation();
                        TryProcessSeasonMaterialsPreviewFix();
                        TryPrepareEditBuildScreen(screen);
                        TrySuppressScreenBackground();
                        TrySuppressCameraBackedScreenBackdrops(screen);
                    }

                PreviewRenderSettingsSnapshot.Restore();
                TryConfigurePreviewCamera();
                if (screen != null && screen.gameObject.activeInHierarchy)
                {
                    TryApplyLightFreePreviewIllumination(screen);
                    TryForceSnowPresentation();
                }
                if (EnablePreviewDiagnostics)
                {
                    TryLogDelayedPreviewDiagnostics(screen);
                }
            }

                if (_activeController.Closed)
                {
                    RestoreAfterPreviewClose("controller closed");
                }
            }
            catch
            {
                RestoreAfterPreviewClose("poll exception");
            }
        }

        private static async Task SetupAsyncWeaponPreview(
            WeaponPreview preview,
            Item item,
            Action onLoadingStart,
            Action onLoadingFinished,
            Callback onFinished,
            bool setAsClosest,
            Vector3? initialRotation,
            bool enableWeaponLights,
            int version)
        {
            object bundleTokens = null;
            GameObject prefab = null;

            try
            {
                preview.Init();

                if (!IsCurrentAsyncPreviewSetup(preview, version))
                {
                    return;
                }

                var previousItem = GetFieldValue(preview, "_currentItem") as Item;
                if (!ReferenceEquals(previousItem, item))
                {
                    preview.ResetRotator(-1f);
                }

                SetFieldValue(preview, "_initialRotation", initialRotation);
                SetFieldValue(preview, "_currentItem", item);
                if (preview.Rotator == null)
                {
                    TryInvokeParameterless(preview, "CreateRotator");
                }

                if (enableWeaponLights && item is Weapon weapon)
                {
                    var trackedLights = GetFieldValue(preview, "_enabledLightMods") as IList;
                    trackedLights?.Clear();
                    foreach (var lightComponent in weapon.Mods.GetComponents<LightComponent>())
                    {
                        if (lightComponent == null || lightComponent.IsActive)
                        {
                            continue;
                        }

                        trackedLights?.Add(lightComponent);
                        lightComponent.IsActive = true;
                    }
                }

                onLoadingStart?.Invoke();

                var tokens = item.GetAllBundleTokens();
                bundleTokens = tokens;
                await EFT.EasyAssetsExtensions.LoadBundles(tokens);

                if (!IsCurrentAsyncPreviewSetup(preview, version))
                {
                    TryReleaseBundleTokens(bundleTokens);
                    return;
                }

                TryInvokeParameterless(preview, "ReleasePreviousTokensIfNeeded");
                var previousTokens = GetFieldValue(preview, "_objectTokens");
                SetFieldValue(preview, "_previousTokens", previousTokens);
                SetFieldValue(preview, "_objectTokens", bundleTokens);
                bundleTokens = null;

                onLoadingFinished?.Invoke();

                TryInvokeParameterless(preview, "ReleasePreviousTokensIfNeeded");
                TryInvokeParameterless(preview, "DestroyOriginalObject");

                prefab = await Singleton<EFT.ObjectsFactory>.Instance.CreateCleanLootPrefabAsync(item, null);

                if (!IsCurrentAsyncPreviewSetup(preview, version))
                {
                    ReturnPreviewPrefab(prefab);
                    return;
                }

                if (prefab == null)
                {
                    throw new InvalidOperationException("Tarkov returned a null preview prefab.");
                }

                SetFieldValue(preview, "_originalObject", prefab);
                prefab.SetActive(true);
                TryInvoke(preview, "PositionGameObject", prefab, initialRotation);
                TryInvokeParameterless(preview, "SetPreviewMaskToChildrenLights");

                var positionedObject = GetFieldValue(preview, "_positionedObject") as Transform;
                if (positionedObject == null)
                {
                    throw new InvalidOperationException("Tarkov preview positioned object was not created.");
                }

                positionedObject.SetParent(preview.Rotator, false);
                var bounds = WeaponPreview.GetBounds(positionedObject.gameObject);
                SetFieldValue(preview, "_originalBounds", new Bounds?(bounds));
                preview.enabled = true;

                TransformTools.SetLayersRecursively(prefab, LayersMaskController.WeaponPreview);

                if (setAsClosest)
                {
                    preview.WeaponPreviewCamera.PlaceInClosestPosition(
                        bounds,
                        item.TemplateId,
                        new ClampValue<float>
                        {
                            Min = -2.7f,
                            Max = -0.45f
                        });
                }

                onFinished?.Succeed();
            }
            catch (Exception ex)
            {
                TryReleaseBundleTokens(bundleTokens);
                ReturnPreviewPrefab(prefab);
                onLoadingFinished?.Invoke();
                _log?.LogWarning($"[ULE] Async edit preview setup failed: {Unwrap(ex).Message}");
                onFinished?.Fail(Unwrap(ex).Message);
            }
        }

        private static int IncrementAsyncPreviewSetupVersion(WeaponPreview preview)
        {
            var key = preview.GetInstanceID();
            AsyncPreviewSetupVersions.TryGetValue(key, out var version);
            version++;
            AsyncPreviewSetupVersions[key] = version;
            return version;
        }

        private static bool IsCurrentAsyncPreviewSetup(WeaponPreview preview, int version)
        {
            return preview != null &&
                   AsyncPreviewSetupVersions.TryGetValue(preview.GetInstanceID(), out var current) &&
                   current == version;
        }

        private static void ReturnPreviewPrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            try
            {
                prefab.SetActive(false);
                AssetPoolObject.ReturnToPool(prefab, true);
            }
            catch
            {
                try
                {
                    UnityEngine.Object.Destroy(prefab);
                }
                catch
                {
                    // Ignore cleanup errors while cancelling async preview setup.
                }
            }
        }

        internal sealed class WorldPreviewObject
        {
            private WorldPreviewObject(GameObject prefab, object bundleTokens, string error)
            {
                Prefab = prefab;
                BundleTokens = bundleTokens;
                Error = error;
            }

            public GameObject Prefab { get; }
            public object BundleTokens { get; }
            public string Error { get; }
            public bool Succeeded => Prefab != null;

            public static WorldPreviewObject Success(GameObject prefab, object bundleTokens)
            {
                return new WorldPreviewObject(prefab, bundleTokens, null);
            }

            public static WorldPreviewObject Failed(string error)
            {
                return new WorldPreviewObject(null, null, string.IsNullOrWhiteSpace(error) ? "Failed to create item preview." : error);
            }

            public void Release()
            {
                ReturnPreviewPrefab(Prefab);
                TryReleaseBundleTokens(BundleTokens);
            }
        }

        private static void TryReleaseBundleTokens(object tokens)
        {
            if (tokens == null)
            {
                return;
            }

            try
            {
                tokens.GetType().GetMethod("Release", AnyBinding, null, Type.EmptyTypes, null)?.Invoke(tokens, null);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        private static void RestoreAfterPreviewClose(string reason)
        {
            LogDebug($"[ULE] Preset preview closed ({reason}); restoring battle presentation.");
            CompleteEditOpenTiming("preview closed before slot timing completed");
            TryApplyRuntimePreviewEdits();
            _activeController = null;
            _previewTransitionActive = false;
            _postOpenSetupPending = false;
            _postOpenSetupPassesRemaining = 0;
            RestoreBattlePresentation();
        }

        private static EditBuildScreen GetLiveEditBuildScreen()
        {
            try
            {
                if (MonoBehaviourSingleton<CommonUI>.Instantiated)
                {
                    var liveScreen = MonoBehaviourSingleton<CommonUI>.Instance?.EditBuildScreen;
                    if (liveScreen != null)
                    {
                        return liveScreen;
                    }
                }
            }
            catch
            {
                // Ignore lookup issues and fall back to controller state.
            }

            return _activeController?.ScreenInstance;
        }

        private static void TryApplyImmediateEditBuildPresentation()
        {
            var screen = GetLiveEditBuildScreen();
            if (screen == null || !screen.gameObject.activeInHierarchy)
            {
                return;
            }

            var contextTicks = BeginEditOpenTimingStage("Immediate.ContextAndUiRepair");
            TryRestoreBattleScreenContext();
            TryApplyBattleUiShell();
            RestoreSceneLights(screen);
            RestoreTerrainVisualsDuringPreview();
            RestoreToggleBorders(screen);
            EndEditOpenTimingStage("Immediate.ContextAndUiRepair", contextTicks);

            var battleCameraTicks = BeginEditOpenTimingStage("Immediate.BattleCameraRepair");
            var battleCamera = GetBattleCamera();
            TryPinGpuInstancerCamera(battleCamera);
            TryKeepBattleCameraStable(battleCamera);
            EndEditOpenTimingStage("Immediate.BattleCameraRepair", battleCameraTicks);

            var previewCameraTicks = BeginEditOpenTimingStage("Immediate.PreviewCameraSetup");
            TryConfigurePreviewCamera();
            EndEditOpenTimingStage("Immediate.PreviewCameraSetup", previewCameraTicks);

            var previewLightTicks = BeginEditOpenTimingStage("Immediate.PreviewLighting");
            TryApplyLightFreePreviewIllumination(screen);
            EndEditOpenTimingStage("Immediate.PreviewLighting", previewLightTicks);

            var snowSeasonTicks = BeginEditOpenTimingStage("Immediate.SnowSeasonRepair");
            var snowPresentationTicks = BeginEditOpenTimingStage("Immediate.SnowPresentation");
            TryForceSnowPresentation();
            EndEditOpenTimingStage("Immediate.SnowPresentation", snowPresentationTicks);
            var seasonMaterialsTicks = BeginEditOpenTimingStage("Immediate.SeasonMaterials");
            TryProcessSeasonMaterialsPreviewFix();
            EndEditOpenTimingStage("Immediate.SeasonMaterials", seasonMaterialsTicks);
            EndEditOpenTimingStage("Immediate.SnowSeasonRepair", snowSeasonTicks);

            var screenPrepTicks = BeginEditOpenTimingStage("Immediate.ScreenPrepAndBackdrops");
            TryPrepareEditBuildScreen(screen);
            TrySuppressScreenBackground();
            TrySuppressCameraBackedScreenBackdrops(screen);
            EndEditOpenTimingStage("Immediate.ScreenPrepAndBackdrops", screenPrepTicks);

            var renderSettingsTicks = BeginEditOpenTimingStage("Immediate.RenderSettingsRestore");
            PreviewRenderSettingsSnapshot.Restore();
            EndEditOpenTimingStage("Immediate.RenderSettingsRestore", renderSettingsTicks);
        }

        private static void TryPrepareEditBuildScreen(EditBuildScreen screen)
        {
            if (_activeController == null || screen == null)
            {
                return;
            }

            try
            {
                if (!_previewEditBuildActionsRestricted)
                {
                    TryRestrictEditBuildPresetActions(screen);
                    _previewEditBuildActionsRestricted = true;
                }

                if (_previewEditableManipulationInstalled ||
                    Time.unscaledTime < _runtimeAttachmentPoolInstallAt)
                {
                    return;
                }

                if (TryInstallRuntimeAttachmentPool(screen))
                {
                    _previewEditableManipulationInstalled = true;
                    LogDebug("[ULE] Prepared edit-build screen for spawn-point-local attachment editing.");
                }
            }
            catch (Exception ex)
            {
                _previewEditableManipulationInstalled = true;
                _log?.LogWarning($"[ULE] Failed to prepare edit-build screen: {Unwrap(ex).Message}");
            }
        }

        private static bool TryInstallRuntimeAttachmentPool(EditBuildScreen screen)
        {
            if (screen == null || _activeController?.Session?.Profile == null)
            {
                return false;
            }

            if (!ShouldInstallRuntimeAttachmentPoolForActiveItem())
            {
                return true;
            }

            var existingManipulation = GetFieldValue(screen, "_useAllManipulation") as EFT.InventoryLogic.EditBuildManipulation;
            if (existingManipulation == null)
            {
                return false;
            }

            if (!TryResolveItemFactory(out var itemFactoryObject, out var error) ||
                itemFactoryObject is not EFT.ItemFactory itemFactory)
            {
                _log?.LogWarning($"[ULE] Failed to build runtime attachment pool: {error}");
                return true;
            }

            var activeItem = _activeController?.Item ?? _activeController?.BuildItem ?? _openingPreviewItem;
            var candidates = BuildRuntimeAttachmentCandidates(
                itemFactory,
                activeItem,
                out var armorPlateCount,
                out var slotCandidateCount).ToArray();

            if (candidates.Length == 0)
            {
                LogDebug("[ULE] Runtime attachment extension pool was empty; leaving EFT's default edit-build choices in place.");
                return true;
            }

            var virtualStash = itemFactory.CreateFakeStash(null);
            virtualStash.Grids[0] = new EFT.InventoryLogic.CornucopiaGrid(Guid.NewGuid().ToString(), Math.Max(30, candidates.Length), 1, true, Array.Empty<ItemFilter>(), virtualStash);

            var added = 0;
            var failed = 0;
            foreach (var candidate in candidates)
            {
                if (TryAddRuntimeAttachmentCandidate(virtualStash, candidate))
                {
                    added++;
                }
                else
                {
                    failed++;
                }
            }

            var collections = (existingManipulation.Collections ?? Array.Empty<CompoundItem>())
                .Where(collection => collection != null)
                .Concat(new CompoundItem[] { virtualStash })
                .ToArray();
            var manipulationController = existingManipulation.InventoryController ??
                                         new InventoryController(_activeController.Session.Profile, true);
            var manipulation = new UleRuntimeAttachmentManipulation(
                manipulationController,
                collections,
                _log,
                CountRuntimeCollectionItems(existingManipulation.Collections),
                armorPlateCount,
                slotCandidateCount,
                added,
                failed);

            screen.UpdateManipulation(manipulation);
            LogDebug($"[ULE] Installed runtime attachment pool extension: baseItems={manipulation.ModCount}, armorPlates={armorPlateCount}, slotCandidates={slotCandidateCount}, uniqueItems={candidates.Length}, added={added}, failed={failed}.");
            return true;
        }

        internal static bool TryCreateFastEditBuildManipulation(EditBuildScreen screen, out EFT.InventoryLogic.DropdownManipulation manipulation)
        {
            manipulation = null;
            if (!IsPreviewTransitionActiveOrOpen || screen == null)
            {
                return false;
            }

            try
            {
                var session = _openingPreviewSession ?? _activeController?.Session ?? GetFieldValue(screen, "_session") as EFT.IEftSession;
                var profile = session?.Profile ?? GetFieldValue(screen, "_profile") as Profile;
                if (session == null || profile == null)
                {
                    return false;
                }

                if (!TryResolveItemFactory(out var itemFactoryObject, out _) ||
                    itemFactoryObject is not EFT.ItemFactory itemFactory)
                {
                    return false;
                }

                var traderController = _cachedEditBuildTraderController;
                if (traderController == null || traderController.RootItem is not CompoundItem)
                {
                    traderController = screen.CreateFakeController(itemFactory.CreateAllModsEver());
                    if (traderController == null || traderController.RootItem is not CompoundItem)
                    {
                        return false;
                    }

                    _cachedEditBuildTraderController = traderController;
                }

                SetFieldValue(screen, "_allItemsFakeController", traderController);

                var screenInventoryController = screen.InventoryController ?? _activeController?.InventoryController;
                var playerItems = screenInventoryController?.Inventory?
                    .GetPlayerItems(EPlayerItems.Stash)?
                    .ToList() ?? new List<Item>();
                SetFieldValue(screen, "_playerItems", playerItems);

                var collections = new List<CompoundItem>
                {
                    (CompoundItem)traderController.RootItem
                };

                var armorPlateCount = 0;
                var slotCandidateCount = 0;
                var added = 0;
                var failed = 0;
                var activeItem = _openingPreviewItem ?? _activeController?.Item ?? _activeController?.BuildItem;
                if (ShouldInstallRuntimeAttachmentPoolForItem(activeItem) &&
                    TryCreateRuntimeAttachmentCollection(
                        itemFactory,
                        activeItem,
                        out var armorPlateCollection,
                        out armorPlateCount,
                        out slotCandidateCount,
                        out added,
                        out failed))
                {
                    collections.Add(armorPlateCollection);
                }

                var controller = new InventoryController(profile, false);
                manipulation = new UleRuntimeAttachmentManipulation(
                    controller,
                    collections.ToArray(),
                    _log,
                    CountRuntimeCollectionItems(collections),
                    armorPlateCount,
                    slotCandidateCount,
                    added,
                    failed);

                SetFieldValue(screen, "_useAllManipulation", manipulation);
                SetFieldValue(screen, "_useAvailableManipulation", manipulation);

                _previewEditableManipulationInstalled = true;
                return true;
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Fast edit-build manipulation failed, falling back to EFT default: {Unwrap(ex).Message}");
                return false;
            }
        }

        private static int CountRuntimeCollectionItems(IEnumerable<CompoundItem> collections)
        {
            if (collections == null)
            {
                return 0;
            }

            try
            {
                var collectionArray = collections
                    .Where(collection => collection != null)
                    .ToArray();

                return collectionArray.Length;
            }
            catch
            {
                return 0;
            }
        }

        private static IEnumerable<Item> BuildRuntimeAttachmentCandidates(
            EFT.ItemFactory itemFactory,
            Item activeItem,
            out int armorPlateCount,
            out int slotCandidateCount)
        {
            armorPlateCount = 0;
            slotCandidateCount = 0;
            var results = new List<Item>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var templateId in GetRuntimeArmorPlateTemplateIds(itemFactory))
            {
                if (!TryCreateRuntimeAttachmentCandidate(itemFactory, templateId, out var armorPlate))
                {
                    continue;
                }

                armorPlateCount++;
                if (seen.Add(templateId))
                {
                    results.Add(armorPlate);
                }
            }

            foreach (var templateId in GetRuntimeSlotFilterTemplateIds(itemFactory, activeItem))
            {
                if (!TryCreateRuntimeAttachmentCandidate(itemFactory, templateId, out var candidate))
                {
                    continue;
                }

                slotCandidateCount++;
                if (seen.Add(templateId))
                {
                    results.Add(candidate);
                }
            }

            return results;
        }

        private static bool TryCreateRuntimeAttachmentCandidate(EFT.ItemFactory itemFactory, string templateId, out Item item)
        {
            item = null;
            if (itemFactory == null || string.IsNullOrWhiteSpace(templateId))
            {
                return false;
            }

            try
            {
                item = itemFactory.CreateItem(itemFactory.NextId, templateId, null);
                return item != null;
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to create runtime attachment candidate '{templateId}': {Unwrap(ex).Message}");
                return false;
            }
        }

        private static bool TryCreateRuntimeAttachmentCollection(
            EFT.ItemFactory itemFactory,
            Item activeItem,
            out CompoundItem collection,
            out int armorPlateCount,
            out int slotCandidateCount,
            out int added,
            out int failed)
        {
            collection = null;
            armorPlateCount = 0;
            slotCandidateCount = 0;
            added = 0;
            failed = 0;

            var candidates = BuildRuntimeAttachmentCandidates(
                itemFactory,
                activeItem,
                out armorPlateCount,
                out slotCandidateCount).ToArray();
            if (candidates.Length == 0)
            {
                return false;
            }

            var virtualStash = itemFactory.CreateFakeStash(null);
            virtualStash.Grids[0] = new EFT.InventoryLogic.CornucopiaGrid(Guid.NewGuid().ToString(), Math.Max(30, candidates.Length), 1, true, Array.Empty<ItemFilter>(), virtualStash);

            foreach (var candidate in candidates)
            {
                if (TryAddRuntimeAttachmentCandidate(virtualStash, candidate))
                {
                    added++;
                }
                else
                {
                    failed++;
                }
            }

            collection = virtualStash;
            return added > 0;
        }

        private static string[] GetRuntimeArmorPlateTemplateIds(EFT.ItemFactory itemFactory)
        {
            if (itemFactory?.ItemTemplates == null)
            {
                return Array.Empty<string>();
            }

            var sourceCount = itemFactory.ItemTemplates.Count;
            if (_cachedArmorPlateTemplateSourceCount == sourceCount && _cachedArmorPlateTemplateIds.Length > 0)
            {
                return _cachedArmorPlateTemplateIds;
            }

            _cachedArmorPlateTemplateIds = itemFactory.ItemTemplates
                .Where(kv => kv.Value is EFT.InventoryLogic.ArmorPlateTemplate &&
                             !ShouldHideRuntimeAttachmentTemplate(kv.Value, itemFactory.ItemTemplates))
                .Select(kv => kv.Key.ToString())
                .Where(tpl => !string.IsNullOrWhiteSpace(tpl))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            _cachedArmorPlateTemplateSourceCount = sourceCount;
            return _cachedArmorPlateTemplateIds;
        }

        private static string[] GetRuntimeSlotFilterTemplateIds(EFT.ItemFactory itemFactory, Item activeItem)
        {
            if (itemFactory?.ItemTemplates == null || activeItem is not CompoundItem compound || compound.AllSlots == null)
            {
                return Array.Empty<string>();
            }

            var templates = itemFactory.ItemTemplates;
            var filters = compound.AllSlots
                .Where(slot => slot != null && !slot.Locked && slot.Filters != null)
                .SelectMany(slot => slot.Filters)
                .Where(filter => filter != null && filter.Filter != null && filter.Filter.Length > 0)
                .ToArray();
            if (filters.Length == 0)
            {
                return Array.Empty<string>();
            }

            var activeTemplateId = activeItem.TemplateId.ToString();
            return templates
                .Where(kv => kv.Value != null &&
                             kv.Value._type == NodeType.Item &&
                             !string.Equals(kv.Key.ToString(), activeTemplateId, StringComparison.Ordinal) &&
                             !ShouldHideRuntimeAttachmentTemplate(kv.Value, templates) &&
                             SlotFiltersAllowTemplate(kv.Value, filters, templates))
                .Select(kv => kv.Key.ToString())
                .Where(tpl => !string.IsNullOrWhiteSpace(tpl))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static bool SlotFiltersAllowTemplate(
            ItemTemplate template,
            IEnumerable<ItemFilter> filters,
            IDictionary<MongoID, ItemTemplate> templates)
        {
            if (template == null || filters == null)
            {
                return false;
            }

            foreach (var filter in filters)
            {
                if (filter == null || filter.Filter == null || filter.Filter.Length == 0)
                {
                    continue;
                }

                if (!TemplateMatchesAnyFilter(template, filter.Filter, templates))
                {
                    continue;
                }

                if (TemplateMatchesAnyFilter(template, filter.ExcludedFilter, templates))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool TemplateMatchesAnyFilter(
            ItemTemplate template,
            IEnumerable<MongoID> filterIds,
            IDictionary<MongoID, ItemTemplate> templates)
        {
            if (template == null || filterIds == null)
            {
                return false;
            }

            foreach (var filterId in filterIds)
            {
                if (TemplateIsOrChildOf(template, filterId.ToString(), templates))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TemplateIsOrChildOf(
            ItemTemplate template,
            string parentTemplateId,
            IDictionary<MongoID, ItemTemplate> templates)
        {
            if (template == null || string.IsNullOrWhiteSpace(parentTemplateId))
            {
                return false;
            }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = template;
            while (current != null && visited.Add(current._id.ToString()))
            {
                if (string.Equals(current._id.ToString(), parentTemplateId, StringComparison.Ordinal))
                {
                    return true;
                }

                if (current.Parent != null)
                {
                    current = current.Parent;
                    continue;
                }

                if (!current.ParentId.HasValue || templates == null || !templates.TryGetValue(current.ParentId.Value, out current))
                {
                    break;
                }
            }

            return false;
        }

        private static bool ShouldHideRuntimeAttachmentTemplate(
            ItemTemplate template,
            IDictionary<MongoID, ItemTemplate> templates)
        {
            return template == null ||
                   template._type != NodeType.Item ||
                   IsBuiltInInsertTemplate(template, templates);
        }

        private static bool IsBuiltInInsertTemplate(ItemTemplate template, IDictionary<MongoID, ItemTemplate> templates)
        {
            if (template == null)
            {
                return false;
            }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = template;
            while (current != null && visited.Add(current._id.ToString()))
            {
                if (string.Equals(current._name, "BuiltInInserts", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (current.Parent != null)
                {
                    current = current.Parent;
                    continue;
                }

                if (!current.ParentId.HasValue || templates == null || !templates.TryGetValue(current.ParentId.Value, out current))
                {
                    break;
                }
            }

            return false;
        }

        private static bool ShouldInstallRuntimeAttachmentPoolForActiveItem()
        {
            var item = _activeController?.Item ?? _activeController?.BuildItem;
            return ShouldInstallRuntimeAttachmentPoolForItem(item);
        }

        private static bool ShouldInstallRuntimeAttachmentPoolForItem(Item item)
        {
            return item != null && !(item is Weapon);
        }

        private static bool TryAddRuntimeAttachmentCandidate(EFT.InventoryLogic.Stash virtualStash, Item candidate)
        {
            if (virtualStash?.Grid == null || candidate == null)
            {
                return false;
            }

            try
            {
                candidate.StackObjectsCount = Math.Max(1, candidate.StackMaxSize);
                foreach (var nested in candidate.GetAllItems())
                {
                    nested.PinLockState = EItemPinLockState.Free;
                }

                return virtualStash.Grid.AddAnywhere(candidate, EErrorHandlingType.Ignore).Succeeded;
            }
            catch
            {
                return false;
            }
        }

        private static void TryRestrictEditBuildPresetActions(EditBuildScreen screen)
        {
            if (screen == null)
            {
                return;
            }

            var fieldsToHide = new[]
            {
                "_onlyAvailableToggle",
                "_newBuildButton",
                "_openBuildButton",
                "_saveAsHoverArea",
                "_saveAsBuildButton",
                "_selectItemHoverArea",
                "_selectItemButton",
                "_findPartsHoverArea",
                "_findPartsButton",
                "_publishButton",
                "_deleteBuildButton",
                "_deleteCanvasGroup",
                "_saveButton",
                "_saveHoverArea",
                "_assembleButton",
                "_assembleHoverArea",
                "_buildName",
                "_itemReadyToAssemble",
                "_itemReadyToAssembleButton",
                "_openBuildWindow",
                "_selectWeaponBodyWindow"
            };

            foreach (var fieldName in fieldsToHide)
            {
                TrySetScreenFieldActive(screen, fieldName, false);
            }

            TryCloseScreenField(screen, "_openBuildWindow");
            TryCloseScreenField(screen, "_selectWeaponBodyWindow");
        }

        private static void TrySetScreenFieldActive(object owner, string fieldName, bool active)
        {
            try
            {
                var value = owner?.GetType().GetField(fieldName, AnyBinding)?.GetValue(owner);
                switch (value)
                {
                    case GameObject gameObject:
                        gameObject.SetActive(active);
                        break;
                    case Component component when component.gameObject != null:
                        component.gameObject.SetActive(active);
                        break;
                }
            }
            catch
            {
                // UI layout hardening only.
            }
        }

        private static void TryCloseScreenField(object owner, string fieldName)
        {
            try
            {
                var value = owner?.GetType().GetField(fieldName, AnyBinding)?.GetValue(owner);
                value?.GetType().GetMethod("Close", AnyBinding, null, Type.EmptyTypes, null)?.Invoke(value, null);
            }
            catch
            {
                // UI layout hardening only.
            }
        }

        private static void TryApplyRuntimePreviewEdits()
        {
            if (!_previewApplyOnClose || _previewSourceItem == null || _activeController == null)
            {
                return;
            }

            try
            {
                if (!TryCaptureRuntimeLootItem(_activeController.BuildItem, _previewSourceItem, out var editedItem, out var error))
                {
                    _lastPreviewEditMessage = string.IsNullOrWhiteSpace(error)
                        ? "Weapon edits could not be captured."
                        : error;
                    _log?.LogWarning($"[ULE] {_lastPreviewEditMessage}");
                    return;
                }

                if (AreLootItemStructuresEquivalent(_previewOriginalItem, editedItem))
                {
                    LogDebug("[ULE] Spawn-item preview closed with no attachment changes.");
                    return;
                }

                CopyLootItem(editedItem, _previewSourceItem);
                _lastPreviewAppliedChanges = true;
                _lastPreviewEditMessage = "Item edits applied only to this spawn point. Use Save to write the spawn edit.";
                LogDebug("[ULE] Captured edited spawn-item tree from preview screen.");
            }
            catch (Exception ex)
            {
                _lastPreviewEditMessage = $"Item edits could not be captured: {Unwrap(ex).Message}";
                _log?.LogWarning($"[ULE] {_lastPreviewEditMessage}");
            }
        }

        private static bool TryCaptureRuntimeLootItem(Item runtimeRoot, LootItem sourceItem, out LootItem editedItem, out string error)
        {
            editedItem = null;
            error = null;

            if (runtimeRoot == null)
            {
                error = "The edited weapon item was not available when the preview closed.";
                return false;
            }

            if (!TryResolveItemFactory(out var itemFactoryObject, out error) || itemFactoryObject is not EFT.ItemFactory itemFactory)
            {
                error = string.IsNullOrWhiteSpace(error)
                    ? "The Tarkov item factory was not available when the preview closed."
                    : error;
                return false;
            }

            JsonType.FlatItem[] flatItems;
            try
            {
                flatItems = itemFactory.TreeToFlatItems(runtimeRoot);
            }
            catch (Exception ex)
            {
                error = $"Failed to serialize the edited weapon: {Unwrap(ex).Message}";
                return false;
            }

            if (flatItems == null || flatItems.Length == 0)
            {
                error = "The edited weapon serialized to an empty item tree.";
                return false;
            }

            var records = new List<RuntimeFlatRecord>(flatItems.Length);
            var byId = new Dictionary<string, RuntimeFlatRecord>(StringComparer.Ordinal);
            foreach (var flatItem in flatItems)
            {
                var record = RuntimeFlatRecord.FromFlatItem(flatItem);
                if (record == null || string.IsNullOrWhiteSpace(record.Tpl))
                {
                    continue;
                }

                records.Add(record);
                if (!string.IsNullOrWhiteSpace(record.Id) && !byId.ContainsKey(record.Id))
                {
                    byId[record.Id] = record;
                }
            }

            var rootId = runtimeRoot.Id ?? string.Empty;
            RuntimeFlatRecord root = null;
            foreach (var record in records)
            {
                if (!string.IsNullOrWhiteSpace(record.ParentId) &&
                    byId.TryGetValue(record.ParentId, out var parent))
                {
                    parent.Children.Add(record);
                    continue;
                }

                if (root == null ||
                    string.Equals(record.Id, rootId, StringComparison.Ordinal))
                {
                    root = record;
                }
            }

            if (root == null)
            {
                error = "The edited weapon root item was not found in the serialized item tree.";
                return false;
            }

            editedItem = new LootItem
            {
                Tpl = root.Tpl,
                ComposedKey = string.IsNullOrWhiteSpace(sourceItem?.ComposedKey)
                    ? Util.GenerateComposedKey()
                    : sourceItem.ComposedKey,
                Weight = sourceItem?.Weight ?? 1f,
                PresetId = null,
                PresetName = null,
                SlotId = root.SlotId,
                LocationJson = root.LocationJson ?? sourceItem?.LocationJson,
                UpdJson = root.UpdJson ?? sourceItem?.UpdJson,
                StackMin = root.StackCount ?? sourceItem?.StackMin,
                StackMax = root.StackCount ?? sourceItem?.StackMax,
                Children = root.Children.Select(ConvertRuntimeChild).ToList()
            };

            return true;
        }

        private static LootItemNode ConvertRuntimeChild(RuntimeFlatRecord record)
        {
            return new LootItemNode
            {
                Tpl = record.Tpl,
                SlotId = record.SlotId,
                LocationJson = record.LocationJson,
                UpdJson = record.UpdJson,
                StackMin = record.StackCount,
                StackMax = record.StackCount,
                Children = record.Children.Select(ConvertRuntimeChild).ToList()
            };
        }

        private static void CopyLootItem(LootItem source, LootItem target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target.Tpl = source.Tpl;
            target.ComposedKey = source.ComposedKey;
            target.Weight = source.Weight;
            target.SlotId = source.SlotId;
            target.LocationJson = source.LocationJson;
            target.UpdJson = source.UpdJson;
            target.StackMin = source.StackMin;
            target.StackMax = source.StackMax;
            target.PresetId = source.PresetId;
            target.PresetName = source.PresetName;
            target.Children = source.Children?
                .Select(child => child?.CloneNode())
                .Where(child => child != null)
                .ToList() ?? new List<LootItemNode>();
        }

        private static bool AreLootItemStructuresEquivalent(LootItemNode left, LootItemNode right)
        {
            return string.Equals(BuildStructureSignature(left), BuildStructureSignature(right), StringComparison.Ordinal);
        }

        private static string BuildStructureSignature(LootItemNode node)
        {
            if (node == null)
            {
                return "<null>";
            }

            var children = (node.Children ?? new List<LootItemNode>())
                .Where(child => child != null)
                .OrderBy(child => child.SlotId ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(child => child.Tpl ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(BuildStructureSignature, StringComparer.Ordinal)
                .Select(BuildStructureSignature);

            return string.Join("|", new[]
            {
                node.Tpl ?? string.Empty,
                node.SlotId ?? string.Empty,
                node.StackMin.HasValue ? node.StackMin.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty,
                node.StackMax.HasValue ? node.StackMax.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty,
                "[" + string.Join(",", children.ToArray()) + "]"
            });
        }

        private static void ClearActivePreviewEditState()
        {
            _previewSourceItem = null;
            _previewOriginalItem = null;
            _previewApplyOnClose = false;
            _previewEditableManipulationInstalled = false;
            _previewEditBuildActionsRestricted = false;
            _runtimeAttachmentPoolInstallAt = 0f;
        }

        private static bool TryBuildRuntimePresetItem(LootItem rootItem, BepInEx.Logging.ManualLogSource log, out object runtimeRoot, out string error)
        {
            runtimeRoot = null;
            error = null;

            if (!TryResolveItemFactory(out var itemFactory, out error))
            {
                return false;
            }

            var flatRecords = new List<FlatRecord>(32);
            var rootId = AppendFlatRecords(rootItem, parentId: null, flatRecords);
            if (flatRecords.Count == 0 || string.IsNullOrWhiteSpace(rootId))
            {
                error = "The preset tree is empty.";
                return false;
            }

            var flatItemsArray = Array.CreateInstance(_flatItemsDataType, flatRecords.Count);
            for (var i = 0; i < flatRecords.Count; i++)
            {
                flatItemsArray.SetValue(CreateFlatItem(flatRecords[i]), i);
            }

            var preexistingItems = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), _itemType));

            try
            {
                var treeResult = _flatItemsToTreeMethod.Invoke(itemFactory, new[] { flatItemsArray, true, preexistingItems });
                if (treeResult == null)
                {
                    error = "Tarkov did not return a preview item tree.";
                    return false;
                }

                var itemsField = treeResult.GetType().GetField("Items", AnyBinding);
                var items = itemsField?.GetValue(treeResult) as IDictionary;
                if (items == null || !items.Contains(rootId))
                {
                    error = "Tarkov failed to build the preset preview tree.";
                    return false;
                }

                runtimeRoot = items[rootId];
                if (runtimeRoot is Item typedRuntimeRoot && itemFactory is EFT.ItemFactory typedItemFactory)
                {
                    var addedDefaultInserts = TryMaterializeMissingLockedArmorInserts(typedItemFactory, typedRuntimeRoot);
                    if (addedDefaultInserts > 0)
                    {
                        LogDebug(log, $"[ULE] Materialized {addedDefaultInserts} missing locked armor insert(s) for runtime attachment editing.");
                    }
                }

                return runtimeRoot != null;
            }
            catch (Exception ex)
            {
                error = $"Failed to build Tarkov preview tree: {Unwrap(ex).Message}";
                log?.LogWarning($"[ULE] {error}");
                return false;
            }
        }

        private static int TryMaterializeMissingLockedArmorInserts(EFT.ItemFactory itemFactory, Item rootItem)
        {
            if (itemFactory == null || rootItem == null)
            {
                return 0;
            }

            if (rootItem is Weapon)
            {
                return 0;
            }

            var added = 0;
            try
            {
                var compoundItems = rootItem.GetAllItems()
                    .OfType<CompoundItem>()
                    .ToArray();

                foreach (var compoundItem in compoundItems)
                {
                    foreach (var slot in compoundItem.Slots)
                    {
                        if (slot == null || slot.ContainedItem != null || slot.Filters == null || slot.Filters.Length == 0)
                        {
                            continue;
                        }

                        if (!slot.Filters.Any(filter => filter != null && filter.locked))
                        {
                            continue;
                        }

                        var plateTemplateId = slot.Filters
                            .Where(filter => filter?.Plate != null)
                            .Select(filter => filter.Plate.Value)
                            .FirstOrDefault();
                        if (plateTemplateId == default)
                        {
                            continue;
                        }

                        Item defaultInsert;
                        try
                        {
                            defaultInsert = itemFactory.CreateItem(itemFactory.NextId, plateTemplateId.ToString(), null);
                        }
                        catch
                        {
                            continue;
                        }

                        if (defaultInsert == null)
                        {
                            continue;
                        }

                        var addResult = slot.AddWithoutRestrictions(defaultInsert);
                        if (addResult.Succeeded)
                        {
                            added++;
                        }
                    }
                }
            }
            catch
            {
                // Default inserts are a visual/editing normalization; never block opening the editor.
            }

            return added;
        }

        private static bool TryResolveItemFactory(out object itemFactory, out string error)
        {
            itemFactory = null;
            error = null;

            if (_cachedItemFactory != null)
            {
                itemFactory = _cachedItemFactory;
                return true;
            }

            try
            {
                if (Singleton<EFT.ItemFactory>.Instantiated)
                {
                    itemFactory = Singleton<EFT.ItemFactory>.Instance;
                    _cachedItemFactory = itemFactory as EFT.ItemFactory;
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = $"Failed to resolve singleton item factory: {Unwrap(ex).Message}";
            }

            if (_singletonInstanceProperty != null)
            {
                try
                {
                    itemFactory = _singletonInstanceProperty.GetValue(null, null);
                    if (itemFactory != null)
                    {
                        _cachedItemFactory = itemFactory as EFT.ItemFactory;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    error = $"Failed to resolve singleton item factory: {Unwrap(ex).Message}";
                }
            }

            if (_utilityApplicationType != null && _utilityItemFactoryField != null)
            {
                try
                {
                    var utilityApplications = Resources.FindObjectsOfTypeAll(_utilityApplicationType);
                    foreach (var app in utilityApplications)
                    {
                        var candidate = _utilityItemFactoryField.GetValue(app);
                        if (candidate != null)
                        {
                            itemFactory = candidate;
                            _cachedItemFactory = candidate as EFT.ItemFactory;
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = $"Failed to resolve item factory: {Unwrap(ex).Message}";
                    return false;
                }
            }

            error = "Item factory is not ready yet.";
            return false;
        }

        private static string AppendFlatRecords(LootItemNode node, string parentId, List<FlatRecord> records)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Tpl))
            {
                return null;
            }

            var id = GenerateMongoId();
            records.Add(new FlatRecord
            {
                Id = id,
                ParentId = parentId,
                Tpl = node.Tpl,
                SlotId = string.IsNullOrWhiteSpace(parentId) ? null : node.SlotId,
                LocationJson = node.LocationJson,
                UpdJson = NormalizeUpdJson(node)
            });

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    AppendFlatRecords(child, id, records);
                }
            }

            return id;
        }

        private static object CreateFlatItem(FlatRecord record)
        {
            var flatItem = _flatItemsDataCtor.Invoke(null);
            _flatIdField.SetValue(flatItem, _mongoIdCtor.Invoke(new object[] { record.Id }));
            _flatTplField.SetValue(flatItem, _mongoIdCtor.Invoke(new object[] { record.Tpl }));

            if (string.IsNullOrWhiteSpace(record.ParentId))
            {
                _flatParentIdField.SetValue(flatItem, null);
            }
            else
            {
                var mongoIdValue = _mongoIdCtor.Invoke(new object[] { record.ParentId });
                var nullableParentType = typeof(Nullable<>).MakeGenericType(_mongoIdType);
                var nullableParent = Activator.CreateInstance(nullableParentType, mongoIdValue);
                _flatParentIdField.SetValue(flatItem, nullableParent);
            }

            _flatSlotIdField.SetValue(flatItem, string.IsNullOrWhiteSpace(record.SlotId) ? null : record.SlotId);
            _flatLocationField.SetValue(flatItem, CreateJsonWrapper(record.LocationJson));
            _flatUpdField.SetValue(flatItem, CreateJsonWrapper(record.UpdJson));
            return flatItem;
        }

        private static object CreateJsonWrapper(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return null;
            }

            try
            {
                var wrapper = _unparsedDataConstructor.Invoke(null);
                _unparsedDataTokenField.SetValue(wrapper, JToken.Parse(rawJson));
                return wrapper;
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeUpdJson(LootItemNode node)
        {
            if (!string.IsNullOrWhiteSpace(node?.UpdJson))
            {
                return node.UpdJson;
            }

            if (node?.StackMin is int stackCount && stackCount > 0)
            {
                return new JObject
                {
                    ["StackObjectsCount"] = stackCount
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            return null;
        }

        private static bool EnsureReflection(BepInEx.Logging.ManualLogSource log, out string error)
        {
            error = null;
            if (_lookupAttempted)
            {
                error = _reflectionLookupError;
                return _flatItemsToTreeMethod != null;
            }

            _lookupAttempted = true;

            try
            {
                _itemType = typeof(Item);
                _compoundItemType = typeof(CompoundItem);
                _inventoryControllerType = typeof(InventoryController);
                _mongoIdType = typeof(MongoID);
                _itemFactoryType = typeof(EFT.ItemFactory);
                _flatItemsDataType = typeof(JsonType.FlatItem);
                _unparsedDataType = typeof(UnparsedData);
                _utilityApplicationType = ResolvePreviewBridgeType(UtilityApplicationTypeName);
                _canvasType = typeof(Canvas);
                _mongoIdCtor = _mongoIdType?.GetConstructor(new[] { typeof(string) });
                _flatItemsDataCtor = _flatItemsDataType?.GetConstructor(Type.EmptyTypes);
                _unparsedDataConstructor = _unparsedDataType?.GetConstructor(Type.EmptyTypes);
                _flatIdField = _flatItemsDataType?.GetField("_id", AnyBinding);
                _flatTplField = _flatItemsDataType?.GetField("_tpl", AnyBinding);
                _flatParentIdField = _flatItemsDataType?.GetField("parentId", AnyBinding);
                _flatSlotIdField = _flatItemsDataType?.GetField("slotId", AnyBinding);
                _flatLocationField = _flatItemsDataType?.GetField("location", AnyBinding);
                _flatUpdField = _flatItemsDataType?.GetField("upd", AnyBinding);
                _unparsedDataTokenField = _unparsedDataType?.GetField("JToken", AnyBinding);
                _utilityItemFactoryField = _utilityApplicationType?.GetField("ItemFactory", AnyBinding) ??
                                           _utilityApplicationType?.GetField("itemFactory", AnyBinding) ??
                                           _utilityApplicationType?.GetField("_itemFactory", AnyBinding);
                var singletonGenericType = Type.GetType("Comfort.Common.Singleton`1, Comfort", throwOnError: false);
                if (singletonGenericType != null && _itemFactoryType != null)
                {
                    var singletonItemFactoryType = singletonGenericType.MakeGenericType(_itemFactoryType);
                    _singletonInstanceProperty = singletonItemFactoryType.GetProperty("Instance", AnyBinding);
                }

                _flatItemsToTreeMethod = _itemFactoryType?.GetMethod("FlatItemsToTree", AnyBinding);

                var ready = _mongoIdCtor != null &&
                            _flatItemsDataCtor != null &&
                            _unparsedDataConstructor != null &&
                            _flatIdField != null &&
                            _flatTplField != null &&
                            _flatParentIdField != null &&
                            _flatSlotIdField != null &&
                            _flatLocationField != null &&
                            _flatUpdField != null &&
                            _unparsedDataTokenField != null &&
                            _flatItemsToTreeMethod != null;

                if (!ready)
                {
                    error = BuildMissingReflectionMessage();
                    _reflectionLookupError = error;
                    log?.LogWarning($"[ULE] {error}");
                    return false;
                }

                _reflectionLookupError = null;
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to initialize preset preview bridge: {Unwrap(ex).Message}";
                _reflectionLookupError = error;
                log?.LogWarning($"[ULE] {error}");
                return false;
            }
        }

        private static Type ResolvePreviewBridgeType(params string[] typeNames)
        {
            if (typeNames == null)
            {
                return null;
            }

            foreach (var typeName in typeNames)
            {
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    continue;
                }

                var type = Type.GetType(typeName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static string BuildMissingReflectionMessage()
        {
            var missing = new List<string>();
            if (_itemType == null)
            {
                missing.Add(ItemTypeName);
            }

            if (_compoundItemType == null)
            {
                missing.Add(CompoundItemTypeName);
            }

            if (_inventoryControllerType == null)
            {
                missing.Add(InventoryControllerTypeName);
            }

            if (_mongoIdCtor == null)
            {
                missing.Add("MongoID(string)");
            }

            if (_itemFactoryType == null)
            {
                missing.Add(ItemFactoryTypeName);
            }

            if (_flatItemsDataCtor == null)
            {
                missing.Add(FlatItemsDataTypeName + "()");
            }

            if (_unparsedDataConstructor == null)
            {
                missing.Add(UnparsedDataTypeName + "()");
            }

            if (_flatIdField == null)
            {
                missing.Add("FlatItem._id");
            }

            if (_flatTplField == null)
            {
                missing.Add("FlatItem._tpl");
            }

            if (_flatParentIdField == null)
            {
                missing.Add("FlatItem.parentId");
            }

            if (_flatSlotIdField == null)
            {
                missing.Add("FlatItem.slotId");
            }

            if (_flatLocationField == null)
            {
                missing.Add("FlatItem.location");
            }

            if (_flatUpdField == null)
            {
                missing.Add("FlatItem.upd");
            }

            if (_unparsedDataTokenField == null)
            {
                missing.Add("UnparsedData.JToken");
            }

            if (_flatItemsToTreeMethod == null)
            {
                missing.Add("ItemFactory.FlatItemsToTree");
            }

            return missing.Count == 0
                ? "Tarkov's preview UI types are not available in this scene."
                : "Tarkov's preview UI types are not available. Missing: " + string.Join(", ", missing);
        }

        private static Exception Unwrap(Exception ex)
        {
            while (ex is TargetInvocationException tie && tie.InnerException != null)
            {
                ex = tie.InnerException;
            }

            return ex;
        }

        private static string GenerateMongoId()
        {
            var bytes = Guid.NewGuid().ToByteArray();
            var chars = new char[24];
            for (var i = 0; i < 12; i++)
            {
                var value = bytes[i];
                chars[i * 2] = HexAlphabet[value >> 4];
                chars[i * 2 + 1] = HexAlphabet[value & 0x0F];
            }

            return new string(chars);
        }

        private static void ApplyBattleSafePresentation()
        {
            if (_battlePresentationApplied)
            {
                return;
            }

            _battlePresentationApplied = true;

            try
            {
                GamePlayerOwner.SetIgnoreInput(true);
                GamePlayerOwner.SetIgnoreInputWithKeepResetLook(true);
                _forcedRaidInputIgnore = true;
            }
            catch
            {
                _forcedRaidInputIgnore = false;
            }

            CaptureBattleCameraState();
            if (EnablePreviewDiagnostics)
            {
                CaptureBattleCameraVisualBaseline();
            }
            CaptureSnowPresentation();
            if (!PreserveBattleCameraEffectsDuringPreview)
            {
                SuppressBattleCameraBehaviours(GetBattleCamera());
            }
            else if (SuppressBattleCameraSupersamplingDuringPreview)
            {
                SuppressBattleCameraSupersampling(GetBattleCamera(), exhaustive: true);
            }

            EnsureBattleCameraCallbacks();
            TryApplyBattleUiShell();
            TryHideBattleUi();
        }

        private static void RestoreBattlePresentation()
        {
            if (_inventoryHostActive)
            {
                TryCloseInventoryHost();
            }

            RestoreSuppressedBackground();
            RestoreSuppressedBackdropVisuals();
            RestoreLightFreePreviewIllumination();
            RestorePreviewCamera();
            RestoreBattleCameraState();
            RestoreBattleCameraBehaviours();
            RestorePreviewCameraBehaviours();
            RestoreAuxiliaryCameras();
            RestoreSceneLights();
            RestoreTerrainVisualsDuringPreview();
            RestoreBattleUi();
            _battleUiShellApplied = false;
            ForceRestoreBattleUiNow();
            _backdropDiagnosticsLogged = false;
            _cameraDiagnosticsLogged = false;
            _cameraImageDiagnosticsLogged = false;
            _previewPanelDiagnosticsLogged = false;
            _delayedPreviewDiagnosticsLogged = false;
            _battleCameraComponentDiagnosticsLogged = false;

            try
            {
                if (_forcedInventoryOpen && GamePlayerOwner.MyPlayer != null)
                {
                    GamePlayerOwner.MyPlayer.SetInventoryOpened(false);
                }

                if (_forcedRaidInputIgnore)
                {
                    GamePlayerOwner.SetIgnoreInput(false);
                    GamePlayerOwner.SetIgnoreInputWithKeepResetLook(false);
                }
            }
            catch
            {
                // Ignore cleanup errors during screen transitions.
            }
            finally
            {
                PreviewRenderSettingsSnapshot.Restore();
                PreviewRenderSettingsSnapshot.Clear();
                CapturedEnabledSceneLights.Clear();
                CapturedSceneLightDiagnostics.Clear();
                CapturedTerrainVisuals.Clear();
                _lastGpuInstancerCameraPinFrame = -1;
                RemoveBattleCameraCallbacks();
                RestoreSnowPresentation();
                QueuePreviewInventoryToggleRestore();
                _battlePresentationApplied = false;
                _battleUiShellApplied = false;
                _forcedInventoryOpen = false;
                _forcedRaidInputIgnore = false;
                _postCloseRestorePassesRemaining = 12;
                _nextPostCloseRestoreAt = Time.unscaledTime;
                _previewTransitionActive = false;
                _battleScreenContextRestored = false;
                ClearActivePreviewEditState();
                ResetPreviewSnowDiagnostics();
                CapturedTerrainMaterialPropertyStates.Clear();
                _lastTerrainMaterialPropertySnapshotState = null;
                ClearBattleCameraVisualBaseline();
                ClearCapturedRainSnowLevel();
                _log = null;
            }
        }

        internal static bool TryOverrideCurrentScreenController(
            EFT.UI.Screens.IBaseScreenController<EEftScreenType> proposedController,
            out EFT.UI.Screens.IBaseScreenController<EEftScreenType> replacement)
        {
            replacement = null;

            if (!_previewTransitionActive || proposedController == null)
            {
                return false;
            }

            if (proposedController.ScreenType != EEftScreenType.WeaponModding &&
                proposedController.ScreenType != EEftScreenType.EditBuild)
            {
                return false;
            }

            if (ForceBattleUiShellDuringEditBuild && proposedController.ScreenType == EEftScreenType.EditBuild)
            {
                var owner = GamePlayerOwner.MyPlayer != null ? GamePlayerOwner.MyPlayer.GetComponent<GamePlayerOwner>() : null;
                replacement = GetBattleScreenController(owner);
            }

            replacement ??= _previewPreviousScreenController;
            if (replacement == null)
            {
                var current = EFT.UI.Screens.EftScreenManager.Instance?.CurrentBaseScreenController;
                if (current != null && current.ScreenType == EEftScreenType.BattleUI)
                {
                    replacement = current;
                }
            }

            return replacement != null;
        }

        private static void TryRestoreBattleScreenContext()
        {
            var previousController = _previewPreviousScreenController;
            if (previousController == null)
            {
                try
                {
                    var owner = GamePlayerOwner.MyPlayer != null ? GamePlayerOwner.MyPlayer.GetComponent<GamePlayerOwner>() : null;
                    var battleField = typeof(GamePlayerOwner).GetField("BattleUIScreenController", AnyBinding);
                    previousController = battleField?.GetValue(owner) as EFT.UI.Screens.IBaseScreenController<EEftScreenType>;
                }
                catch
                {
                    previousController = null;
                }
            }

            if (previousController == null)
            {
                return;
            }

            var screenManager = EFT.UI.Screens.EftScreenManager.Instance;
            if (screenManager == null)
            {
                return;
            }

            var currentController = screenManager.CurrentBaseScreenController;
            if (_battleScreenContextRestored &&
                currentController != null &&
                currentController.ScreenType == EEftScreenType.BattleUI)
            {
                return;
            }

            if (previousController.ScreenType != EEftScreenType.BattleUI)
            {
                _battleScreenContextRestored = true;
                return;
            }

            try
            {
                if (!ReferenceEquals(screenManager.CurrentBaseScreenController, previousController))
                {
                    screenManager.CurrentBaseScreenController = previousController;
                }

                _battleScreenContextRestored = ReferenceEquals(screenManager.CurrentBaseScreenController, previousController);
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to restore battle screen context: {Unwrap(ex).Message}");
            }
        }

        private static void TryProcessPendingBattleRestore()
        {
            if (_postCloseRestorePassesRemaining <= 0 || Time.unscaledTime < _nextPostCloseRestoreAt)
            {
                return;
            }

            try
            {
                var player = GamePlayerOwner.MyPlayer;
                var owner = player != null ? player.GetComponent<GamePlayerOwner>() : null;
                TryRestoreBattleScreenContext();
                TryCloseAllActiveScreens();

                try
                {
                    player?.SetInventoryOpened(false);
                }
                catch
                {
                    // Ignore inventory-state restore issues during delayed teardown.
                }

                try
                {
                    if (MonoBehaviourSingleton<CommonUI>.Instantiated)
                    {
                        var battleScreen = MonoBehaviourSingleton<CommonUI>.Instance?.EftBattleUIScreen;
                        if (battleScreen != null && !battleScreen.gameObject.activeSelf)
                        {
                            battleScreen.gameObject.SetActive(true);
                        }
                    }
                }
                catch
                {
                    // Ignore delayed HUD-object restore issues.
                }

                try
                {
                    if (MonoBehaviourSingleton<PreloaderUI>.Instantiated)
                    {
                        MonoBehaviourSingleton<PreloaderUI>.Instance.RaidInfoVisibility = true;
                    }
                }
                catch
                {
                    // Ignore delayed raid-info restore issues.
                }

                try
                {
                    owner?.ShowBattleUIScreen();
                    TryRevealBattleHud(owner);
                }
                catch
                {
                    // Fall back to the reflected restore path below.
                }

                TryRestoreBattleHudController();
            }
            finally
            {
                _postCloseRestorePassesRemaining--;
                _nextPostCloseRestoreAt = Time.unscaledTime + 0.1f;
                if (_postCloseRestorePassesRemaining <= 0)
                {
                    _previewPreviousScreenController = null;
                    _battleScreenContextRestored = false;
                }
            }
        }

        private static void TryProcessPendingEditorCloseRestore()
        {
            if (_postEditorCloseRestorePassesRemaining <= 0 || Time.unscaledTime < _nextPostEditorCloseRestoreAt)
            {
                return;
            }

            try
            {
                RefreshBattleUiAfterEditorClose();
            }
            finally
            {
                _postEditorCloseRestorePassesRemaining--;
                _nextPostEditorCloseRestoreAt = Time.unscaledTime + 0.1f;
            }
        }

        private static void QueuePreviewInventoryToggleRestore()
        {
            _postPreviewInventoryToggleStage = 1;
            _nextPostPreviewInventoryToggleAt = Time.unscaledTime + 0.05f;
        }

        private static void TryProcessPendingPreviewInventoryToggle()
        {
            if (_postPreviewInventoryToggleStage <= 0 || Time.unscaledTime < _nextPostPreviewInventoryToggleAt)
            {
                return;
            }

            try
            {
                var screenManager = EFT.UI.Screens.EftScreenManager.Instance;
                if (screenManager == null)
                {
                    _postPreviewInventoryToggleStage = 0;
                    return;
                }

                switch (_postPreviewInventoryToggleStage)
                {
                    case 1:
                        screenManager.ToggleScreen(EEftScreenType.Inventory, null);
                        _postPreviewInventoryToggleStage = 2;
                        _nextPostPreviewInventoryToggleAt = Time.unscaledTime + 0.08f;
                        break;
                    case 2:
                    {
                        screenManager.ToggleScreen(EEftScreenType.BattleUI, null);
                        var player = GamePlayerOwner.MyPlayer;
                        var owner = player != null ? player.GetComponent<GamePlayerOwner>() : null;
                        owner?.ShowBattleUIScreen();
                        TryRevealBattleHud(owner);
                        _postPreviewInventoryToggleStage = 0;
                        break;
                    }
                }
            }
            catch
            {
                _postPreviewInventoryToggleStage = 0;
            }
        }

        private static bool TryOpenInventoryHost(Player player, InventoryController inventoryController, out string error)
        {
            error = null;

            if (_inventoryHostActive)
            {
                return true;
            }

            var owner = player != null ? player.GetComponent<GamePlayerOwner>() : null;
            if (owner == null)
            {
                error = "Game player owner is not ready yet.";
                return false;
            }

            try
            {
                if (!EFT.UI.Screens.EftScreenManager.Instance.CheckCurrentScreen(EEftScreenType.BattleUI))
                {
                    error = "Battle UI is not the active screen.";
                    return false;
                }

                player.SetInventoryOpened(true);

                var showInventoryScreen = owner.GetType()
                    .GetMethods(AnyBinding)
                    .FirstOrDefault(method =>
                        string.Equals(method.Name, "ShowInventoryScreen", StringComparison.Ordinal) &&
                        method.GetParameters().Length == 9);

                if (showInventoryScreen == null)
                {
                    error = "Could not find the in-raid inventory host method.";
                    return false;
                }

                Action exitAction = () =>
                {
                    try
                    {
                        player.SetInventoryOpened(false);
                    }
                    catch
                    {
                        // Ignore teardown issues.
                    }
                };

                showInventoryScreen.Invoke(owner, new object[]
                {
                    exitAction,
                    player.HealthController,
                    inventoryController,
                    player.QuestController,
                    player.AchievementsController,
                    player.PrestigeController,
                    null,
                    EInventoryTab.Gear,
                    false
                });

                _inventoryHostOwner = owner;
                _inventoryHostPlayer = player;
                _inventoryHostActive = true;
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to open the inventory host: {Unwrap(ex).Message}";
                return false;
            }
        }

        private static void TryCloseInventoryHost()
        {
            try
            {
                var currentController = EFT.UI.Screens.EftScreenManager.Instance.CurrentBaseScreenController;
                if (currentController != null)
                {
                    var screenType = currentController.ScreenType;
                    if (screenType is EEftScreenType eftScreenType && eftScreenType == EEftScreenType.Inventory)
                    {
                        currentController.GetType().GetMethod("CloseScreen", AnyBinding)?.Invoke(currentController, null);
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to close inventory host: {Unwrap(ex).Message}");
            }

            try
            {
                _inventoryHostPlayer?.SetInventoryOpened(false);
            }
            catch
            {
                // Ignore teardown issues.
            }

            try
            {
                _inventoryHostOwner?.ShowBattleUIScreen();
                TryRevealBattleHud(_inventoryHostOwner);
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to restore battle screen from inventory host: {Unwrap(ex).Message}");
            }

            _inventoryHostOwner = null;
            _inventoryHostPlayer = null;
            _inventoryHostActive = false;
        }

        private static void TryHideBattleUi()
        {
            try
            {
                HiddenBattleUiCanvases.Clear();
                HiddenBattleUiCanvasGroups.Clear();

                if (MonoBehaviourSingleton<CommonUI>.Instantiated)
                {
                    var battleScreen = MonoBehaviourSingleton<CommonUI>.Instance?.EftBattleUIScreen;
                    if (battleScreen != null)
                    {
                        if (ForceBattleUiShellDuringEditBuild)
                        {
                            TryHideBattleUiWithCanvasGroup(battleScreen.gameObject);
                        }
                        else
                        {
                            foreach (var canvas in battleScreen.GetComponentsInChildren<Canvas>(true))
                            {
                                if (canvas == null)
                                {
                                    continue;
                                }

                                HiddenBattleUiCanvases.Add(new CanvasStateSnapshot(canvas, canvas.enabled));
                                if (canvas.enabled)
                                {
                                    canvas.enabled = false;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                HiddenBattleUiCanvases.Clear();
                RestoreHiddenBattleUiCanvasGroups();
            }

            try
            {
                if (MonoBehaviourSingleton<PreloaderUI>.Instantiated)
                {
                    var preloader = MonoBehaviourSingleton<PreloaderUI>.Instance;
                    _raidInfoVisibilityChanged = true;
                    preloader.RaidInfoVisibility = false;
                }
            }
            catch
            {
                _raidInfoVisibilityChanged = false;
            }
        }

        private static void TryApplyBattleUiShell()
        {
            if (!ForceBattleUiShellDuringEditBuild || !IsPreviewTransitionActiveOrOpen)
            {
                return;
            }

            try
            {
                var owner = GamePlayerOwner.MyPlayer != null ? GamePlayerOwner.MyPlayer.GetComponent<GamePlayerOwner>() : null;
                var battleController = GetBattleScreenController(owner);
                if (battleController == null)
                {
                    return;
                }

                if (MonoBehaviourSingleton<CommonUI>.Instantiated)
                {
                    var battleScreen = MonoBehaviourSingleton<CommonUI>.Instance?.EftBattleUIScreen;
                    if (battleScreen != null && !battleScreen.gameObject.activeSelf)
                    {
                        battleScreen.gameObject.SetActive(true);
                    }
                }

                TrySetCurrentScreenController(battleController);
                SnowWetRenderer.OnScreenChanged(EEftScreenType.BattleUI);
                _battleUiShellApplied = true;
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to apply BattleUI shell for preview: {Unwrap(ex).Message}");
            }
        }

        private static EFT.UI.Screens.IBaseScreenController<EEftScreenType> GetBattleScreenController(GamePlayerOwner owner)
        {
            if (_previewPreviousScreenController != null &&
                _previewPreviousScreenController.ScreenType == EEftScreenType.BattleUI)
            {
                return _previewPreviousScreenController;
            }

            if (owner == null)
            {
                return null;
            }

            try
            {
                var battleField = typeof(GamePlayerOwner).GetField("BattleUIScreenController", AnyBinding);
                return battleField?.GetValue(owner) as EFT.UI.Screens.IBaseScreenController<EEftScreenType>;
            }
            catch
            {
                return null;
            }
        }

        private static void TryHideBattleUiWithCanvasGroup(GameObject battleScreenObject)
        {
            if (battleScreenObject == null)
            {
                return;
            }

            var canvasGroup = battleScreenObject.GetComponent<CanvasGroup>();
            var addedByPreview = false;
            if (canvasGroup == null)
            {
                canvasGroup = battleScreenObject.AddComponent<CanvasGroup>();
                addedByPreview = true;
            }

            HiddenBattleUiCanvasGroups.Add(new CanvasGroupStateSnapshot(
                canvasGroup,
                canvasGroup.alpha,
                canvasGroup.interactable,
                canvasGroup.blocksRaycasts,
                canvasGroup.ignoreParentGroups,
                addedByPreview));

            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.ignoreParentGroups = false;
        }

        private static void RestoreBattleUi()
        {
            RestoreHiddenBattleUiCanvases();

            try
            {
                if (_raidInfoVisibilityChanged && MonoBehaviourSingleton<PreloaderUI>.Instantiated)
                {
                    MonoBehaviourSingleton<PreloaderUI>.Instance.RaidInfoVisibility = true;
                }
            }
            catch
            {
                // Ignore restore issues during teardown.
            }
            finally
            {
                _raidInfoVisibilityChanged = false;
            }

            TryRestoreBattleHudController();
        }

        private static void RestoreHiddenBattleUiCanvases()
        {
            try
            {
                foreach (var snapshot in HiddenBattleUiCanvases)
                {
                    if (snapshot.Canvas != null)
                    {
                        snapshot.Canvas.enabled = snapshot.Enabled;
                    }
                }
            }
            catch
            {
                // Ignore restore issues during teardown.
            }
            finally
            {
                HiddenBattleUiCanvases.Clear();
            }

            RestoreHiddenBattleUiCanvasGroups();
        }

        private static void RestoreHiddenBattleUiCanvasGroups()
        {
            try
            {
                foreach (var snapshot in HiddenBattleUiCanvasGroups)
                {
                    if (snapshot.CanvasGroup == null)
                    {
                        continue;
                    }

                    if (snapshot.AddedByPreview)
                    {
                        UnityEngine.Object.Destroy(snapshot.CanvasGroup);
                        continue;
                    }

                    snapshot.CanvasGroup.alpha = snapshot.Alpha;
                    snapshot.CanvasGroup.interactable = snapshot.Interactable;
                    snapshot.CanvasGroup.blocksRaycasts = snapshot.BlocksRaycasts;
                    snapshot.CanvasGroup.ignoreParentGroups = snapshot.IgnoreParentGroups;
                }
            }
            catch
            {
                // Ignore restore issues during teardown.
            }
            finally
            {
                HiddenBattleUiCanvasGroups.Clear();
            }
        }

        private static void TryRestoreBattleHudController()
        {
            try
            {
                var player = GamePlayerOwner.MyPlayer;
                var owner = player != null ? player.GetComponent<GamePlayerOwner>() : null;
                if (owner == null)
                {
                    return;
                }

                try
                {
                    player?.SetInventoryOpened(false);
                    player?.SetInventoryOpened(true);
                    player?.SetInventoryOpened(false);
                }
                catch
                {
                    // Ignore inventory-state restore issues during teardown.
                }

                try
                {
                    TryCloseAllActiveScreens();
                    var previewBattleField = typeof(GamePlayerOwner).GetField("BattleUIScreenController", AnyBinding);
                    TrySetCurrentScreenController(previewBattleField?.GetValue(owner));
                    owner.ShowBattleUIScreen();
                    TryRevealBattleHud(owner);
                }
                catch
                {
                    // Fall back to the reflected controller path below.
                }

                var battleField = typeof(GamePlayerOwner).GetField("BattleUIScreenController", AnyBinding);
                var battleController = battleField?.GetValue(owner);
                if (battleController == null)
                {
                    return;
                }

                TrySetCurrentScreenController(battleController);

                var showScreen = battleController.GetType().GetMethod(
                    "ShowScreen",
                    AnyBinding,
                    binder: null,
                    types: new[] { typeof(EScreenState) },
                    modifiers: null);

                showScreen?.Invoke(battleController, new object[] { EScreenState.Root });
                TryRevealBattleHud(owner);
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to restore battle HUD controller: {Unwrap(ex).Message}");
            }
        }

        private static void RestoreSceneLights(Component screen)
        {
            RestoreSceneLights();

            if (screen == null)
            {
                return;
            }

            try
            {
                var lightField = screen.GetType().BaseType?.GetField("light_0", AnyBinding);
                if (lightField?.GetValue(screen) is IEnumerable lights)
                {
                    foreach (var lightObject in lights)
                    {
                        if (lightObject is Light light && light != null)
                        {
                            if (!light.enabled)
                            {
                                light.enabled = true;
                            }

                            RestoredSceneLights.Add(light);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to restore scene lights for preset screen: {Unwrap(ex).Message}");
            }
        }

        private static void CaptureSceneLightsBeforePreview()
        {
            CapturedEnabledSceneLights.Clear();
            CapturedSceneLightDiagnostics.Clear();

            try
            {
                foreach (var light in FindObjectsProxy.FindUnityObjectsOfType<Light>())
                {
                    if (light != null && light.enabled)
                    {
                        CapturedEnabledSceneLights.Add(light);
                        CapturedSceneLightDiagnostics.Add(new LightDiagnosticsSnapshot(light));
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to capture scene lights before preview: {Unwrap(ex).Message}");
            }
        }

        private static void RestoreSceneLights()
        {
            if (CapturedEnabledSceneLights.Count == 0 && RestoredSceneLights.Count == 0)
            {
                return;
            }

            foreach (var light in CapturedEnabledSceneLights)
            {
                if (light != null)
                {
                    light.enabled = true;
                }
            }

            foreach (var light in RestoredSceneLights)
            {
                if (light != null)
                {
                    light.enabled = true;
                }
            }

            RestoredSceneLights.Clear();
        }

        private static void CaptureTerrainVisualsBeforePreview()
        {
            CapturedTerrainVisuals.Clear();

            try
            {
                foreach (var terrain in FindObjectsProxy.FindUnityObjectsOfType<Terrain>())
                {
                    if (terrain != null)
                    {
                        CapturedTerrainVisuals.Add(new TerrainVisualSnapshot(terrain));
                    }
                }
            }
            catch (Exception ex)
            {
                if (EnablePreviewDiagnostics)
                {
                    _log?.LogWarning($"[ULE] Failed to capture terrain visuals before preview: {Unwrap(ex).Message}");
                }
            }
        }

        private static void RestoreTerrainVisualsDuringPreview()
        {
            if (CapturedTerrainVisuals.Count == 0)
            {
                return;
            }

            foreach (var snapshot in CapturedTerrainVisuals)
            {
                try
                {
                    snapshot.Restore();
                }
                catch
                {
                    // Terrain visuals are restored best-effort during the EditBuild transition.
                }
            }
        }

        private static void TryPinGpuInstancerCamera(Camera battleCamera)
        {
            if (battleCamera == null || _lastGpuInstancerCameraPinFrame == Time.frameCount)
            {
                return;
            }

            _lastGpuInstancerCameraPinFrame = Time.frameCount;

            try
            {
                var utilityType = Type.GetType("GPUInstancer.GPUInstancerAPI, Assembly-CSharp", throwOnError: false);
                var getManagers = utilityType?.GetMethod("GetActiveManagers", BindingFlags.Public | BindingFlags.Static);
                var setCamera = utilityType?.GetMethod(
                    "SetCamera",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(Camera) },
                    modifiers: null);
                if (getManagers == null || setCamera == null)
                {
                    return;
                }

                if (getManagers.Invoke(null, null) is not IEnumerable managers)
                {
                    return;
                }

                foreach (var manager in managers)
                {
                    if (manager != null && !ReferenceEquals(GetMemberValue(manager, "Camera") as Camera, battleCamera))
                    {
                        setCamera.Invoke(null, new object[] { battleCamera });
                        return;
                    }
                }
            }
            catch
            {
                // GPU-instancer camera repair is only a visual continuity hint.
            }
        }

        private static void TryApplyLightFreePreviewIllumination(Component screen)
        {
            if (screen == null || !screen.gameObject.activeInHierarchy)
            {
                return;
            }

            try
            {
                if (SuppressEditBuildPreviewLightsDuringRaid)
                {
                    SuppressScreenPreviewLights(screen);
                }

                if (BrightenPreviewRenderersWithoutPreviewLights)
                {
                    BrightenPreviewRenderers(screen);
                }
            }
            catch (Exception ex)
            {
                if (EnablePreviewDiagnostics)
                {
                    _log?.LogWarning($"[ULE] Failed to apply light-free preview illumination: {Unwrap(ex).Message}");
                }
            }
        }

        private static void SuppressScreenPreviewLights(Component screen)
        {
            foreach (var light in screen.GetComponentsInChildren<Light>(true))
            {
                if (light == null)
                {
                    continue;
                }

                if (!HasPreviewLightSnapshot(light))
                {
                    SuppressedPreviewLights.Add(new PreviewLightSnapshot(light));
                }

                light.intensity = 0f;
            }
        }

        private static bool HasPreviewLightSnapshot(Light light)
        {
            for (var i = 0; i < SuppressedPreviewLights.Count; i++)
            {
                if (SuppressedPreviewLights[i].Light == light)
                {
                    return true;
                }
            }

            return false;
        }

        private static void BrightenPreviewRenderers(Component screen)
        {
            foreach (var renderer in screen.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsPreviewItemRenderer(renderer, screen))
                {
                    continue;
                }

                var materials = renderer.sharedMaterials;
                for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material == null)
                    {
                        continue;
                    }

                    if (!HasPreviewRendererSnapshot(renderer, materialIndex))
                    {
                        var originalBlock = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(originalBlock, materialIndex);
                        BrightenedPreviewRenderers.Add(new PreviewRendererMaterialSnapshot(renderer, materialIndex, originalBlock));
                    }

                    var block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block, materialIndex);

                    var changed = false;
                    var baseColor = Color.white;
                    var hasBaseColor = false;
                    foreach (var propertyId in PreviewMaterialColorPropertyIds)
                    {
                        if (!TryGetMaterialColor(material, propertyId, out var color))
                        {
                            continue;
                        }

                        if (!hasBaseColor)
                        {
                            baseColor = color;
                            hasBaseColor = true;
                        }

                        block.SetColor(propertyId, BoostColor(color, PreviewLightFreeColorBoost));
                        changed = true;
                    }

                    foreach (var propertyId in PreviewMaterialEmissionPropertyIds)
                    {
                        if (!material.HasProperty(propertyId))
                        {
                            continue;
                        }

                        block.SetColor(propertyId, BoostColor(hasBaseColor ? baseColor : Color.white, PreviewLightFreeEmissionBoost));
                        changed = true;
                    }

                    if (changed)
                    {
                        renderer.SetPropertyBlock(block, materialIndex);
                    }
                }
            }
        }

        private static bool IsPreviewItemRenderer(Renderer renderer, Component screen)
        {
            if (renderer == null || renderer.transform == null || screen == null || screen.transform == null)
            {
                return false;
            }

            if (renderer.gameObject.layer == LayersMaskController.WeaponPreview)
            {
                return true;
            }

            for (var current = renderer.transform; current != null && current != screen.transform; current = current.parent)
            {
                if (string.Equals(current.name, "Weapon Preview", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasPreviewRendererSnapshot(Renderer renderer, int materialIndex)
        {
            for (var i = 0; i < BrightenedPreviewRenderers.Count; i++)
            {
                var snapshot = BrightenedPreviewRenderers[i];
                if (snapshot.Renderer == renderer && snapshot.MaterialIndex == materialIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetMaterialColor(Material material, int propertyId, out Color color)
        {
            color = Color.white;

            try
            {
                if (material == null || !material.HasProperty(propertyId))
                {
                    return false;
                }

                color = material.GetColor(propertyId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Color BoostColor(Color color, float multiplier)
        {
            return new Color(
                Mathf.Min(color.r * multiplier, 4f),
                Mathf.Min(color.g * multiplier, 4f),
                Mathf.Min(color.b * multiplier, 4f),
                color.a);
        }

        private static void RestoreLightFreePreviewIllumination()
        {
            foreach (var snapshot in BrightenedPreviewRenderers)
            {
                if (snapshot.Renderer == null)
                {
                    continue;
                }

                try
                {
                    snapshot.Renderer.SetPropertyBlock(snapshot.PropertyBlock, snapshot.MaterialIndex);
                }
                catch
                {
                    // Ignore teardown races while the preview object returns to its pool.
                }
            }

            BrightenedPreviewRenderers.Clear();

            foreach (var snapshot in SuppressedPreviewLights)
            {
                if (snapshot.Light == null)
                {
                    continue;
                }

                try
                {
                    snapshot.Light.enabled = snapshot.Enabled;
                    snapshot.Light.intensity = snapshot.Intensity;
                }
                catch
                {
                    // Ignore destroyed preview lights.
                }
            }

            SuppressedPreviewLights.Clear();
        }

        private static void RestoreToggleBorders(Component screen)
        {
            if (screen == null)
            {
                return;
            }

            try
            {
                var itemObserveScreenType = screen.GetType().BaseType;
                var modClassTogglesField = itemObserveScreenType?.GetField("ModClassToggles", AnyBinding);
                if (modClassTogglesField?.GetValue(screen) is Toggle[] modClassToggles)
                {
                    foreach (var toggle in modClassToggles)
                    {
                        RestoreToggleBorders(toggle);
                    }
                }

                var allClassesToggleField = screen.GetType().GetField("_allClassesToggle", AnyBinding);
                if (allClassesToggleField?.GetValue(screen) is Toggle allClassesToggle)
                {
                    RestoreToggleBorders(allClassesToggle);
                }
            }
            catch
            {
                // Best-effort UI repair only.
            }
        }

        private static void RestoreToggleBorders(Toggle toggle)
        {
            if (toggle == null || !toggle.gameObject.activeInHierarchy)
            {
                return;
            }

            foreach (var transform in toggle.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(transform.name, "Border", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!transform.gameObject.activeSelf)
                {
                    transform.gameObject.SetActive(true);
                }
            }
        }

        private static void TryConfigurePreviewCamera()
        {
            var screen = GetLiveEditBuildScreen();
            if (screen == null || !screen.gameObject.activeInHierarchy)
            {
                return;
            }

            var viewporter = screen.GetComponentInChildren<CameraViewporter>(true);
            var previewCamera = viewporter?.TargetCamera;
            if (previewCamera == null || !previewCamera.gameObject.activeInHierarchy)
            {
                return;
            }

            if (_configuredPreviewCamera != previewCamera)
            {
                RestorePreviewCamera();

                _configuredPreviewCamera = previewCamera;
                _previewCameraClearFlags = previewCamera.clearFlags;
                _previewCameraCullingMask = previewCamera.cullingMask;
                _previewCameraBackgroundColor = previewCamera.backgroundColor;
                _previewCameraDepth = previewCamera.depth;
                _previewCameraUseOcclusionCulling = previewCamera.useOcclusionCulling;
                _previewCameraRect = previewCamera.rect;
                _previewCameraTargetTexture = previewCamera.targetTexture;
                _previewCameraRenderingPath = previewCamera.renderingPath;
            }

            previewCamera.enabled = true;
            previewCamera.useOcclusionCulling = false;
            previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            previewCamera.cullingMask = _previewCameraCullingMask;
            previewCamera.depth = _previewCameraDepth;
            if (ForcePreviewIntoRenderTexture)
            {
                previewCamera.clearFlags = CameraClearFlags.Depth;
                previewCamera.renderingPath = RenderingPath.UsePlayerSettings;
                EnsurePreviewRenderTarget(viewporter, previewCamera);
                EnsurePreviewCameraCallbacks();
            }
            else
            {
                previewCamera.clearFlags = CameraClearFlags.Depth;
                if (UsePreviewCameraAmbientFillWithoutPreviewLights)
                {
                    EnsurePreviewCameraCallbacks();
                }
                else
                {
                    RemovePreviewCameraCallbacks();
                }

                DestroyPreviewRenderTarget();
                previewCamera.targetTexture = null;
                // Match the raid camera/project path so EFT's deferred terrain and snow buffers are
                // not reinterpreted through a forward-only preview pass.
                previewCamera.renderingPath = RenderingPath.UsePlayerSettings;
            }
            SuppressPreviewCameraBehaviours(previewCamera);
            SuppressAuxiliaryScreenCameras(screen, previewCamera);
        }

        private static void RestorePreviewCamera()
        {
            if (_configuredPreviewCamera == null)
            {
                return;
            }

            try
            {
                _configuredPreviewCamera.clearFlags = _previewCameraClearFlags;
                _configuredPreviewCamera.cullingMask = _previewCameraCullingMask;
                _configuredPreviewCamera.backgroundColor = _previewCameraBackgroundColor;
                _configuredPreviewCamera.depth = _previewCameraDepth;
                _configuredPreviewCamera.useOcclusionCulling = _previewCameraUseOcclusionCulling;
                _configuredPreviewCamera.rect = _previewCameraRect;
                _configuredPreviewCamera.targetTexture = _previewCameraTargetTexture;
                _configuredPreviewCamera.renderingPath = _previewCameraRenderingPath;
            }
            catch
            {
                // Ignore cleanup issues during screen teardown.
            }
            finally
            {
                RemovePreviewCameraCallbacks();
                DestroyPreviewRenderTarget();
                _configuredPreviewCamera = null;
                _previewCameraTargetTexture = null;
            }
        }

        private static void CaptureBattleCameraState()
        {
            var battleCamera = GetBattleCamera();
            if (battleCamera == null)
            {
                _battleCamera = null;
                _battleCameraStateCaptured = false;
                return;
            }

            _battleCamera = battleCamera;
            _battleCameraPosition = battleCamera.transform.position;
            _battleCameraRotation = battleCamera.transform.rotation;
            _battleCameraRect = battleCamera.rect;
            _battleCameraTargetTexture = battleCamera.targetTexture;
            _battleCameraProjectionMatrix = battleCamera.projectionMatrix;
            _battleCameraStateCaptured = true;
        }

        private static void CaptureBattleCameraVisualBaseline()
        {
            ClearBattleCameraVisualBaseline();

            var battleCamera = GetBattleCamera();
            if (battleCamera == null)
            {
                _capturedBattleCameraVisualState = "<no-camera>";
                return;
            }

            try
            {
                _capturedBattleCameraVisualState = DescribeBattleCameraVisualState(battleCamera);
                _capturedBattleCameraShaderKeywords = DescribePreviewRelevantShaderKeywords();
                _capturedBattleCameraCommandBuffers = DescribeCameraCommandBufferSummary(battleCamera);

                foreach (var behaviour in battleCamera.GetComponents<Behaviour>())
                {
                    if (behaviour == null || behaviour is Camera)
                    {
                        continue;
                    }

                    CapturedBattleCameraVisualBehaviours[behaviour.GetInstanceID()] =
                        new BattleCameraVisualSnapshot(behaviour.GetType().FullName ?? behaviour.GetType().Name, behaviour.enabled);
                }
            }
            catch (Exception ex)
            {
                _capturedBattleCameraVisualState = $"error={Unwrap(ex).Message}";
            }
        }

        private static void ClearBattleCameraVisualBaseline()
        {
            CapturedBattleCameraVisualBehaviours.Clear();
            _capturedBattleCameraVisualState = null;
            _capturedBattleCameraShaderKeywords = null;
            _capturedBattleCameraCommandBuffers = null;
        }

        private static Camera GetBattleCamera()
        {
            if (_battleCameraStateCaptured && _battleCamera != null)
            {
                return _battleCamera;
            }

            try
            {
                var battleCamera = EFT.CameraControl.CameraManager.Instance?.Camera;
                if (battleCamera != null)
                {
                    return battleCamera;
                }
            }
            catch
            {
                // Ignore lookup issues and fall back to Camera.main.
            }

            return Camera.main;
        }

        private static void EnsureBattleCameraCallbacks()
        {
            if (_battleCameraPreCullRegistered)
            {
                if (!_battleCameraPreRenderRegistered)
                {
                    Camera.onPreRender += HandleBattleCameraPreRender;
                    _battleCameraPreRenderRegistered = true;
                }

                return;
            }

            Camera.onPreCull += HandleBattleCameraPreCull;
            _battleCameraPreCullRegistered = true;
            Camera.onPreRender += HandleBattleCameraPreRender;
            _battleCameraPreRenderRegistered = true;
        }

        private static void RemoveBattleCameraCallbacks()
        {
            if (!_battleCameraPreCullRegistered)
            {
                if (_battleCameraPreRenderRegistered)
                {
                    Camera.onPreRender -= HandleBattleCameraPreRender;
                    _battleCameraPreRenderRegistered = false;
                }

                return;
            }

            Camera.onPreCull -= HandleBattleCameraPreCull;
            _battleCameraPreCullRegistered = false;
            if (_battleCameraPreRenderRegistered)
            {
                Camera.onPreRender -= HandleBattleCameraPreRender;
                _battleCameraPreRenderRegistered = false;
            }
        }

        private static void HandleBattleCameraPreCull(Camera camera)
        {
            if (!_battleCameraStateCaptured || !IsBattleCameraCallbackTarget(camera))
            {
                return;
            }

            RestorePreviewCameraAmbientFill();

            if (ReferenceEquals(camera, _battleCamera))
            {
                TryKeepBattleCameraStable(camera);
            }

            TryForceSnowPresentation();
        }

        private static void HandleBattleCameraPreRender(Camera camera)
        {
            if (!_battleCameraStateCaptured || !IsBattleCameraCallbackTarget(camera))
            {
                return;
            }

            RestorePreviewCameraAmbientFill();

            if (ReferenceEquals(camera, _battleCamera))
            {
                TryKeepBattleCameraStable(camera);
            }

            TryForceSnowPresentation();
        }

        private static bool IsBattleCameraCallbackTarget(Camera camera)
        {
            if (camera == null)
            {
                return false;
            }

            return ReferenceEquals(camera, _battleCamera);
        }

        private static void TryKeepBattleCameraStable(Camera battleCamera)
        {
            if (!_battleCameraStateCaptured || battleCamera == null)
            {
                return;
            }

            if (PreserveBattleCameraEffectsDuringPreview)
            {
                try
                {
                    if (StabilizeBattleCameraTransformDuringPreview)
                    {
                        StabilizeBattleCameraTransform(battleCamera);
                    }

                    TryPinManagedBattleCamera(battleCamera);
                    TryPinGpuInstancerCamera(battleCamera);
                    PreviewRenderSettingsSnapshot.Restore();
                    RestoreSceneLights();
                    RestoreTerrainVisualsDuringPreview();
                    if (SuppressBattleCameraSupersamplingDuringPreview)
                    {
                        SuppressBattleCameraSupersampling(battleCamera, exhaustive: false);
                    }
                }
                catch
                {
                    // Best-effort only during preview render.
                }

                TryDisableBattleBlur();
                return;
            }

            try
            {
                battleCamera.ResetWorldToCameraMatrix();
                battleCamera.ResetProjectionMatrix();

                StabilizeBattleCameraTransform(battleCamera);

                if (battleCamera.rect != _battleCameraRect)
                {
                    battleCamera.rect = _battleCameraRect;
                }

                if (!ReferenceEquals(battleCamera.targetTexture, _battleCameraTargetTexture))
                {
                    battleCamera.targetTexture = _battleCameraTargetTexture;
                }

                battleCamera.projectionMatrix = _battleCameraProjectionMatrix;
                TryPinManagedBattleCamera(battleCamera);
                TryPinGpuInstancerCamera(battleCamera);
                PreviewRenderSettingsSnapshot.Restore();
                RestoreSceneLights();
                RestoreTerrainVisualsDuringPreview();
            }
            catch
            {
                // Best-effort only during preview render.
            }

            TryDisableBattleBlur();
            if (!PreserveBattleCameraEffectsDuringPreview)
            {
                SuppressBattleCameraBehaviours(battleCamera);
            }
        }

        private static void StabilizeBattleCameraTransform(Camera battleCamera)
        {
            if (battleCamera == null)
            {
                return;
            }

            var transform = battleCamera.transform;
            if ((transform.position - _battleCameraPosition).sqrMagnitude > 0.000001f)
            {
                transform.position = _battleCameraPosition;
            }

            if (Quaternion.Angle(transform.rotation, _battleCameraRotation) > 0.01f)
            {
                transform.rotation = _battleCameraRotation;
            }
        }

        private static void RestoreBattleCameraState()
        {
            if (!_battleCameraStateCaptured || _battleCamera == null)
            {
                _battleCamera = null;
                _battleCameraStateCaptured = false;
                return;
            }

            try
            {
                _battleCamera.ResetWorldToCameraMatrix();
                _battleCamera.ResetProjectionMatrix();
                _battleCamera.transform.position = _battleCameraPosition;
                _battleCamera.transform.rotation = _battleCameraRotation;
                _battleCamera.rect = _battleCameraRect;
                _battleCamera.targetTexture = _battleCameraTargetTexture;
                _battleCamera.projectionMatrix = _battleCameraProjectionMatrix;
                TryPinManagedBattleCamera(_battleCamera);
            }
            catch
            {
                // Ignore restore issues during teardown.
            }
            finally
            {
                _battleCamera = null;
                _battleCameraStateCaptured = false;
            }
        }

        private static void TryForceSnowPresentation()
        {
            if (!ShouldForceSnowPresentationNow())
            {
                return;
            }

            try
            {
                SnowWetRenderer.OnScreenChanged(EEftScreenType.BattleUI);
                TryRestoreRainSnowLevelDuringPreview();
                RestoreCapturedSnowRenderersDuringPreview();
                TryRebuildSnowCommandBuffersForPreviewOnce();
                SnowWetRenderer.OnScreenChanged();
                WaterRendererv3.DisableSnowMask(false);
                TryForceTerrainSeasonPresentation();
                if (_snowGlittersKeywordWasEnabled)
                {
                    Shader.EnableKeyword("SNOW_GLITTERS");
                }
            }
            catch
            {
                // Ignore winter keyword issues during preview.
            }
        }

        private static void TryRebuildSnowCommandBuffersForPreviewOnce()
        {
            if (_snowCommandBuffersRebuiltForPreview)
            {
                return;
            }

            _snowCommandBuffersRebuiltForPreview = true;

            try
            {
                var rendererCount = 0;
                var activeCount = 0;
                var rebuiltCount = 0;

                foreach (var renderer in UnityEngine.Object.FindObjectsOfType<SnowWetRenderer>())
                {
                    if (renderer == null)
                    {
                        continue;
                    }

                    rendererCount++;
                    if (!renderer.isActiveAndEnabled)
                    {
                        continue;
                    }

                    activeCount++;
                    renderer.method_1();
                    renderer.method_2();
                    rebuiltCount++;
                }

                _snowCommandBufferRebuildCount += rebuiltCount;
                _lastSnowCommandBufferRebuildState = $"attempted=true renderers={rendererCount} active={activeCount} rebuilt={rebuiltCount} total={_snowCommandBufferRebuildCount}";
                if (EnablePreviewDiagnostics)
                {
                    _log?.LogInfo($"[ULE] Snow command buffers rebuilt for preview: renderers={rendererCount} active={activeCount} rebuilt={rebuiltCount}");
                }
            }
            catch (Exception ex)
            {
                _lastSnowCommandBufferRebuildState = $"failed total={_snowCommandBufferRebuildCount} error={Unwrap(ex).Message}";
                _log?.LogWarning($"[ULE] Failed to rebuild snow command buffers for preview: {Unwrap(ex).Message}");
            }
        }

        private static bool ShouldForceSnowPresentationNow()
        {
            if (!IsPreviewTransitionActiveOrOpen)
            {
                return false;
            }

            var now = Time.unscaledTime;
            if (now < _nextSnowPresentationForceAt)
            {
                return false;
            }

            var previewAge = _previewOpenedAt > 0f ? now - _previewOpenedAt : 0f;
            if (previewAge >= 2f)
            {
                _nextSnowPresentationForceAt = now + 999f;
                return false;
            }

            _nextSnowPresentationForceAt = now + 0.15f;
            _snowPresentationForceCount++;
            return true;
        }

        private static void TryForceTerrainSeasonPresentation()
        {
            if (!IsPreviewTransitionActiveOrOpen)
            {
                return;
            }

            try
            {
                if (!ForceMicroSplatTerrainSeasonDuringPreview)
                {
                    _lastTerrainSeasonState = $"disabled forced={_terrainSeasonForceCount} refreshes={_terrainSeasonRefreshCount} materialFixes={_terrainSeasonMaterialFixCount}";
                    return;
                }

                if (!TryGetExpectedMicroSplatSeason(out var expectedSeason, out var expectedName, out var controllerState))
                {
                    _lastTerrainSeasonState = $"controller={controllerState} microSplat=<unavailable> expected=<unavailable> forced={_terrainSeasonForceCount}";
                    return;
                }

                var microSplatObjectType = Type.GetType("JBooth.MicroSplat.MicroSplatObject, JBooth.MicroSplat.Core", throwOnError: false);
                var currentSeasonField = microSplatObjectType?.GetField("currentSeason", AnyBinding);
                if (currentSeasonField == null)
                {
                    _lastTerrainSeasonState = $"controller={controllerState} microSplat=<missing field> expected={expectedName} forced={_terrainSeasonForceCount}";
                    return;
                }

                var currentSeason = currentSeasonField.GetValue(null);
                var refreshed = false;
                var now = Time.unscaledTime;
                if (!Equals(currentSeason, expectedSeason))
                {
                    currentSeasonField.SetValue(null, expectedSeason);
                    currentSeason = expectedSeason;
                    _terrainSeasonForceCount++;
                    refreshed = TryRefreshMicroSplatTerrains();
                    _terrainSeasonRefreshAttempted = true;
                    _nextTerrainSeasonRefreshAt = now + 0.15f;
                }
                else if (ShouldRefreshMicroSplatTerrainDuringPreview(now))
                {
                    refreshed = TryRefreshMicroSplatTerrains();
                    _terrainSeasonRefreshAttempted = true;
                    _nextTerrainSeasonRefreshAt = now + 0.15f;
                }

                var materialRepaired = TryRepairMicroSplatTerrainMaterials();
                _lastTerrainSeasonState = $"controller={controllerState} microSplat={currentSeason} expected={expectedName} forced={_terrainSeasonForceCount} refreshed={refreshed} refreshes={_terrainSeasonRefreshCount} repaired={materialRepaired} materialFixes={_terrainSeasonMaterialFixCount}";
            }
            catch (Exception ex)
            {
                _lastTerrainSeasonState = $"error={Unwrap(ex).Message} forced={_terrainSeasonForceCount} refreshes={_terrainSeasonRefreshCount} materialFixes={_terrainSeasonMaterialFixCount}";
            }
        }

        private static void TryProcessSeasonMaterialsPreviewFix()
        {
            TryProcessSeasonMaterialsPreviewFixCore(
                requirePreviewOpen: true,
                ignoreThrottle: false,
                requireLoadedComponents: false);
        }

        private static bool TryProcessSeasonMaterialsPreviewFixCore(bool requirePreviewOpen, bool ignoreThrottle, bool requireLoadedComponents)
        {
            if (!ForceSeasonMaterialsPreviewFixDuringEditBuild)
            {
                _lastSeasonMaterialsPreviewState = $"disabled started={_seasonMaterialsPreviewLoadStarted} applied={_seasonMaterialsPreviewFixApplied} fixes={_seasonMaterialsPreviewFixCount}";
                return true;
            }

            if ((requirePreviewOpen && !IsPreviewTransitionActiveOrOpen) || _seasonMaterialsPreviewFixApplied)
            {
                return _seasonMaterialsPreviewFixApplied;
            }

            var now = Time.unscaledTime;
            if (!ignoreThrottle && now < _nextSeasonMaterialsPreviewPollAt)
            {
                return false;
            }

            _nextSeasonMaterialsPreviewPollAt = now + 0.15f;

            try
            {
                if (!TryGetExpectedSeasonName(out var expectedSeasonName, out var controllerState))
                {
                    _lastSeasonMaterialsPreviewState = $"controller={controllerState} expected=<unavailable>";
                    return false;
                }

                if (_seasonMaterialsPreviewLoadStarted &&
                    !string.Equals(_seasonMaterialsPreviewExpectedName, expectedSeasonName, StringComparison.Ordinal))
                {
                    ResetSeasonMaterialsPreviewFixState();
                }

                if (!_seasonMaterialsPreviewLoadStarted)
                {
                    StartSeasonMaterialsPreviewLoad(expectedSeasonName);
                }

                if (_seasonMaterialsPreviewLoadTask != null && !_seasonMaterialsPreviewLoadTask.IsCompleted)
                {
                    _lastSeasonMaterialsPreviewState = $"controller={controllerState} expected={expectedSeasonName} loading=True components={SeasonMaterialsPreviewComponents.Count}";
                    return false;
                }

                if (requireLoadedComponents && SeasonMaterialsPreviewComponents.Count == 0)
                {
                    _lastSeasonMaterialsPreviewState = $"controller={controllerState} expected={expectedSeasonName} waitingForActiveSeasonComponents=True";
                    _seasonMaterialsPreviewLoadStarted = false;
                    _seasonMaterialsPreviewLoadTask = null;
                    _seasonMaterialsPreviewExpectedName = null;
                    SeasonMaterialsPreviewComponents.Clear();
                    return false;
                }

                ApplySeasonMaterialsPreviewFix(expectedSeasonName, controllerState);
                return _seasonMaterialsPreviewFixApplied;
            }
            catch (Exception ex)
            {
                _lastSeasonMaterialsPreviewState = $"error={Unwrap(ex).Message} fixes={_seasonMaterialsPreviewFixCount}";
                return false;
            }
        }

        private static bool TryGetExpectedSeasonName(out string expectedSeasonName, out string controllerState)
        {
            expectedSeasonName = null;
            controllerState = "<null>";

            try
            {
                var controller = Seasons.Controller;
                if (controller != null)
                {
                    var season = controller.Season;
                    var status = controller.Status;
                    expectedSeasonName = season.ToString();
                    controllerState = $"controller={season}/{status}";
                    return true;
                }

                if (TryGetExpectedSeasonNameFromLiveSession(out expectedSeasonName, out controllerState))
                {
                    return true;
                }

                controllerState = "controller=<null> session=<unavailable>";
                return false;
            }
            catch (Exception ex)
            {
                if (TryGetExpectedSeasonNameFromLiveSession(out expectedSeasonName, out var sessionState))
                {
                    controllerState = $"controllerError={Unwrap(ex).Message}; {sessionState}";
                    return true;
                }

                controllerState = $"controller=<unavailable:{Unwrap(ex).Message}> session=<unavailable>";
                return false;
            }
        }

        private static bool TryGetExpectedSeasonNameFromLiveSession(out string expectedSeasonName, out string controllerState)
        {
            expectedSeasonName = null;
            controllerState = "session=<unavailable>";

            try
            {
                foreach (var typeName in new[] { "EFT.LocalGame, Assembly-CSharp", "LocalGame, Assembly-CSharp" })
                {
                    var localGameType = Type.GetType(typeName, throwOnError: false);
                    if (localGameType == null)
                    {
                        continue;
                    }

                    foreach (var game in Resources.FindObjectsOfTypeAll(localGameType))
                    {
                        if (game == null)
                        {
                            continue;
                        }

                        if (game is Component component && !component.gameObject.scene.IsValid())
                        {
                            continue;
                        }

                        if (!TryReadSeasonNameFromLiveGame(game, out expectedSeasonName, out var source))
                        {
                            continue;
                        }

                        controllerState = $"session={source}/{expectedSeasonName}";
                        return true;
                    }
                }

                controllerState = "session=<no-local-game>";
                return false;
            }
            catch (Exception ex)
            {
                controllerState = $"session=<error:{Unwrap(ex).Message}>";
                return false;
            }
        }

        private static bool TryReadSeasonNameFromLiveGame(object game, out string seasonName, out string source)
        {
            seasonName = null;
            source = null;

            if (TryReadSeasonName(game, out seasonName))
            {
                source = game.GetType().Name;
                return true;
            }

            foreach (var field in GetFieldsInHierarchy(game.GetType()))
            {
                if (!LooksLikeSessionField(field))
                {
                    continue;
                }

                object session = null;
                try
                {
                    session = field.GetValue(game);
                }
                catch
                {
                    continue;
                }

                if (!TryReadSeasonName(session, out seasonName))
                {
                    continue;
                }

                source = $"{game.GetType().Name}.{field.Name}";
                return true;
            }

            return false;
        }

        private static bool TryReadSeasonName(object value, out string seasonName)
        {
            seasonName = null;
            if (value == null)
            {
                return false;
            }

            try
            {
                var season = GetMemberValue(value, "Season");
                if (season == null)
                {
                    return false;
                }

                seasonName = season.ToString();
                return !string.IsNullOrEmpty(seasonName);
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksLikeSessionField(FieldInfo field)
        {
            if (field == null)
            {
                return false;
            }

            if (field.Name.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var fieldType = field.FieldType;
            if (fieldType == null)
            {
                return false;
            }

            if (fieldType.Name.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return fieldType.GetInterfaces().Any(interfaceType =>
                string.Equals(interfaceType.Name, "EFT.IEftSession", StringComparison.Ordinal));
        }

        private static void StartSeasonMaterialsPreviewLoad(string expectedSeasonName)
        {
            _seasonMaterialsPreviewLoadStarted = true;
            _seasonMaterialsPreviewExpectedName = expectedSeasonName;
            SeasonMaterialsPreviewComponents.Clear();

            var loadMethodName = GetSeasonMaterialLoadMethodName(expectedSeasonName);
            if (string.IsNullOrEmpty(loadMethodName))
            {
                _seasonMaterialsPreviewLoadTask = Task.CompletedTask;
                _lastSeasonMaterialsPreviewState = $"expected={expectedSeasonName} loadMethod=<unsupported>";
                return;
            }

            var tasks = new List<Task>(8);
            var failures = 0;
            foreach (var typeName in new[]
                     {
                         "SeasonsMaterialsSlice, Assembly-CSharp",
                         "SeasonsMaterialsTerrain, Assembly-CSharp",
                         "SeasonsMaterialsGrassInitialization, Assembly-CSharp"
                     })
            {
                var type = Type.GetType(typeName, throwOnError: false);
                if (type == null)
                {
                    continue;
                }

                var loadMethod = type.GetMethod(loadMethodName, AnyBinding, null, Type.EmptyTypes, null);
                foreach (var component in Resources.FindObjectsOfTypeAll(type))
                {
                    if (component == null || !IsActiveSceneComponent(component))
                    {
                        continue;
                    }

                    SeasonMaterialsPreviewComponents.Add(component);
                    if (loadMethod == null)
                    {
                        continue;
                    }

                    try
                    {
                        if (loadMethod.Invoke(component, null) is Task task)
                        {
                            tasks.Add(task);
                        }
                    }
                    catch
                    {
                        failures++;
                    }
                }
            }

            _seasonMaterialsPreviewLoadTask = tasks.Count > 0 ? Task.WhenAll(tasks) : Task.CompletedTask;
            _lastSeasonMaterialsPreviewState = $"expected={expectedSeasonName} load={loadMethodName} components={SeasonMaterialsPreviewComponents.Count} tasks={tasks.Count} failures={failures}";
        }

        private static string GetSeasonMaterialLoadMethodName(string seasonName)
        {
            switch (seasonName)
            {
                case "Summer":
                    return "LoadSummer";
                case "Winter":
                    return "LoadWinter";
                case "Spring":
                    return "LoadSpring";
                case "SpringEarly":
                    return "LoadSpringEarly";
                case "Autumn":
                    return "LoadAutumn";
                case "AutumnLate":
                    return "LoadAutumnLate";
                default:
                    return null;
            }
        }

        private static void ApplySeasonMaterialsPreviewFix(string expectedSeasonName, string controllerState)
        {
            if (_seasonMaterialsPreviewLoadTask != null && _seasonMaterialsPreviewLoadTask.IsFaulted)
            {
                _lastSeasonMaterialsPreviewState = $"controller={controllerState} expected={expectedSeasonName} loadFaulted={Unwrap(_seasonMaterialsPreviewLoadTask.Exception).Message}";
                return;
            }

            var fixedCount = 0;
            var failures = 0;
            foreach (var component in SeasonMaterialsPreviewComponents.ToArray())
            {
                if (component == null || !IsActiveSceneComponent(component))
                {
                    continue;
                }

                try
                {
                    var fixMethod = component.GetType().GetMethod("Fix", AnyBinding, null, Type.EmptyTypes, null);
                    if (fixMethod == null)
                    {
                        continue;
                    }

                    fixMethod.Invoke(component, null);
                    fixedCount++;
                }
                catch
                {
                    failures++;
                }
            }

            if (fixedCount > 0)
            {
                TryRefreshMicroSplatTerrains();
                TryRepairMicroSplatTerrainMaterials();
                _seasonMaterialsPreviewFixCount += fixedCount;
            }

            _seasonMaterialsPreviewFixApplied = true;
            _lastSeasonMaterialsPreviewState = $"controller={controllerState} expected={expectedSeasonName} applied=True fixed={fixedCount} failures={failures} totalFixes={_seasonMaterialsPreviewFixCount}";
        }

        private static void ResetSeasonMaterialsPreviewFixState()
        {
            _seasonMaterialsPreviewLoadStarted = false;
            _seasonMaterialsPreviewFixApplied = false;
            _seasonMaterialsPreviewLoadTask = null;
            _nextSeasonMaterialsPreviewPollAt = 0f;
            _seasonMaterialsPreviewFixCount = 0;
            _seasonMaterialsPreviewExpectedName = null;
            _lastSeasonMaterialsPreviewState = null;
            SeasonMaterialsPreviewComponents.Clear();
        }

        private static bool IsActiveSceneComponent(object value)
        {
            if (value is not Component component)
            {
                return false;
            }

            return component.gameObject.scene.IsValid() && component.gameObject.activeInHierarchy;
        }

        private static bool ShouldRefreshMicroSplatTerrainDuringPreview(float now)
        {
            return !_terrainSeasonRefreshAttempted;
        }

        private static bool TryGetExpectedMicroSplatSeason(out object expectedSeason, out string expectedName, out string controllerState)
        {
            expectedSeason = null;
            expectedName = null;
            controllerState = "<null>";

            ESeason season;
            ESeasonStatus status;
            try
            {
                var controller = Seasons.Controller;
                if (controller == null)
                {
                    return false;
                }

                season = controller.Season;
                status = controller.Status;
                controllerState = $"{season}/{status}";
            }
            catch
            {
                controllerState = "<unavailable>";
                return false;
            }

            var microSplatSeasonType = Type.GetType("JBooth.MicroSplat.TextureArrayConfig+Season, JBooth.MicroSplat.Core", throwOnError: false);
            if (microSplatSeasonType == null)
            {
                return false;
            }

            expectedName = season.ToString();
            if (!Enum.IsDefined(microSplatSeasonType, expectedName))
            {
                return false;
            }

            expectedSeason = Enum.Parse(microSplatSeasonType, expectedName);
            return true;
        }

        private static bool TryRefreshMicroSplatTerrains()
        {
            try
            {
                var terrainType = Type.GetType("JBooth.MicroSplat.MicroSplatTerrain, JBooth.MicroSplat.Core", throwOnError: false);
                if (terrainType == null)
                {
                    return false;
                }

                var applySeasonMaterial = terrainType.GetMethod("ApplySeasonMaterial", AnyBinding);
                var sync = terrainType.GetMethod("Sync", AnyBinding);
                if (applySeasonMaterial == null && sync == null)
                {
                    return false;
                }

                var refreshedAny = false;
                var materialFixes = 0;
                foreach (var terrain in UnityEngine.Object.FindObjectsOfType(terrainType))
                {
                    if (terrain == null)
                    {
                        continue;
                    }

                    applySeasonMaterial?.Invoke(terrain, null);
                    if (TryApplyMicroSplatTemplateMaterial(terrain, terrainType))
                    {
                        materialFixes++;
                    }

                    sync?.Invoke(terrain, null);
                    if (TryApplyMicroSplatTemplateMaterial(terrain, terrainType))
                    {
                        materialFixes++;
                    }

                    refreshedAny = true;
                }

                if (refreshedAny)
                {
                    _terrainSeasonRefreshCount++;
                }

                if (materialFixes > 0)
                {
                    _terrainSeasonMaterialFixCount += materialFixes;
                }

                return refreshedAny;
            }
            catch
            {
                // Best-effort only. If MicroSplat is unavailable, the normal EFT path continues.
                return false;
            }
        }

        private static bool TryRepairMicroSplatTerrainMaterials()
        {
            try
            {
                var terrainType = Type.GetType("JBooth.MicroSplat.MicroSplatTerrain, JBooth.MicroSplat.Core", throwOnError: false);
                if (terrainType == null)
                {
                    return false;
                }

                var materialFixes = 0;
                foreach (var terrain in UnityEngine.Object.FindObjectsOfType(terrainType))
                {
                    if (terrain != null && TryApplyMicroSplatTemplateMaterial(terrain, terrainType))
                    {
                        materialFixes++;
                    }
                }

                if (materialFixes <= 0)
                {
                    return false;
                }

                _terrainSeasonMaterialFixCount += materialFixes;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryApplyMicroSplatTemplateMaterial(object microSplatTerrain, Type terrainType)
        {
            if (microSplatTerrain == null || terrainType == null)
            {
                return false;
            }

            try
            {
                var templateMaterial = GetFieldInHierarchy(terrainType, "templateMaterial")?.GetValue(microSplatTerrain) as Material;
                if (templateMaterial == null)
                {
                    return false;
                }

                var changed = false;
                var matInstanceField = GetFieldInHierarchy(terrainType, "matInstance");
                var matInstance = matInstanceField?.GetValue(microSplatTerrain) as Material;
                if (matInstanceField != null && !MaterialsAppearEquivalent(matInstance, templateMaterial))
                {
                    matInstanceField.SetValue(microSplatTerrain, templateMaterial);
                    changed = true;
                }

                var terrain = GetFieldInHierarchy(terrainType, "terrain")?.GetValue(microSplatTerrain);
                var terrainMaterial = terrain != null ? GetMemberValue(terrain, "materialTemplate") as Material : null;
                if (terrain != null && !MaterialsAppearEquivalent(terrainMaterial, templateMaterial))
                {
                    changed |= TrySetMemberValue(terrain, "materialTemplate", templateMaterial);
                    TryInvokeParameterless(terrain, "Flush");
                }

                return changed;
            }
            catch
            {
                return false;
            }
        }

        private static void TryRenderSnowForBattleCamera(Camera battleCamera)
        {
            if (!ForceManualSnowRenderDuringEditBuild ||
                !IsPreviewTransitionActiveOrOpen ||
                !_battleCameraStateCaptured ||
                battleCamera == null ||
                !ReferenceEquals(battleCamera, _battleCamera) ||
                _lastManualSnowRenderFrame == Time.frameCount)
            {
                return;
            }

            _lastManualSnowRenderFrame = Time.frameCount;

            try
            {
                TryPinManagedBattleCamera(battleCamera);

                var renderedAny = false;

                if (CapturedSnowRenderers.Count > 0)
                {
                    foreach (var snapshot in CapturedSnowRenderers)
                    {
                        renderedAny |= TryRenderSnowForRenderer(snapshot.Renderer, battleCamera);
                    }

                    if (renderedAny)
                    {
                        return;
                    }
                }

                foreach (var renderer in UnityEngine.Object.FindObjectsOfType<SnowWetRenderer>())
                {
                    renderedAny |= TryRenderSnowForRenderer(renderer, battleCamera);
                }
            }
            catch
            {
                // Best-effort only; Tarkov's own renderer will still run if it is hooked.
            }
        }

        private static bool TryRenderSnowForRenderer(SnowWetRenderer renderer, Camera battleCamera)
        {
            if (renderer == null || battleCamera == null || !renderer.isActiveAndEnabled)
            {
                return false;
            }

            try
            {
                var controller = Seasons.Controller;
                if (controller != null && controller.Status == ESeasonStatus.Summer)
                {
                    return false;
                }
            }
            catch
            {
                // If season lookup fails, fall through to the renderer state captured from the raid.
            }

            try
            {
                // EFT's SnowWetRenderer.OnPreCullCallback only accepts EFT.CameraControl.CameraManager.Instance.Camera.
                // During the in-raid modding screen that reference can drift, so run the
                // same command-buffer work directly for the captured battle camera.
                var data = SnowRenderer.Instance.GetOrCreateSnowRenderData(battleCamera, renderer);
                _manualSnowRenderCamera = battleCamera;
                try
                {
                    renderer.RenderSwamp(data);
                    renderer.RenderSnowy(data);
                }
                finally
                {
                    _manualSnowRenderCamera = null;
                }

                RecordManualSnowRender(battleCamera);
                return true;
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to render snow for preset preview camera '{battleCamera.name}': {Unwrap(ex).Message}");
                return false;
            }
        }

        private static void RecordManualSnowRender(Camera battleCamera)
        {
            try
            {
                _previewSnowPreCullEventCount++;
                _lastPreviewSnowPreCullCameraPath = battleCamera != null ? GetTransformPath(battleCamera.transform) : "<null>";
                _lastPreviewSnowPreCullCameraTag = TryGetCameraTag(battleCamera);
                _lastPreviewSnowPreCullTreatedAsBattleCamera = true;
                _previewSnowPreCullAcceptedCameraCount++;
                _previewSnowPreCullAcceptedMainCameraCount++;
                _previewSnowPreCullCapturedBattleCameraCount++;
                _previewSnowPreCullLooksBattleCameraCount++;
            }
            catch
            {
                // Diagnostics only.
            }
        }

        private static void RestoreSnowPresentation()
        {
            try
            {
                WaterRendererv3.DisableSnowMask(_snowMaskWasDisabled);

                RestoreCapturedSnowRendererStates();
                SnowWetRenderer.OnScreenChanged();

                if (_snowGlittersKeywordWasEnabled)
                {
                    Shader.EnableKeyword("SNOW_GLITTERS");
                }
                else
                {
                    Shader.DisableKeyword("SNOW_GLITTERS");
                }

                if (_winterSnowKeywordWasEnabled)
                {
                    Shader.EnableKeyword("WINTER_SNOW");
                }
                else
                {
                    Shader.DisableKeyword("WINTER_SNOW");
                }
            }
            catch
            {
                // Ignore winter keyword restore issues during preview teardown.
            }
        }

        private static void TryDisableBattleBlur()
        {
            try
            {
                var cameraManager = EFT.CameraControl.CameraManager.Instance;
                var blur = GetFieldValue(cameraManager, "_blur") as Behaviour;
                if (blur == null)
                {
                    return;
                }

                cameraManager.Blur(false, 0f);
                if (blur.enabled)
                {
                    blur.enabled = false;
                }
            }
            catch
            {
                // Best-effort only during preview render.
            }
        }

        internal static void ForceRestoreBattleUiNow()
        {
            try
            {
                TryRestoreBattleScreenContext();
            }
            catch
            {
                // Best-effort only.
            }

            try
            {
                var player = GamePlayerOwner.MyPlayer;
                var owner = player != null ? player.GetComponent<GamePlayerOwner>() : null;
                player?.SetInventoryOpened(false);
                GamePlayerOwner.SetIgnoreInput(false);
                GamePlayerOwner.SetIgnoreInputWithKeepResetLook(false);

                if (MonoBehaviourSingleton<CommonUI>.Instantiated)
                {
                    var battleScreen = MonoBehaviourSingleton<CommonUI>.Instance?.EftBattleUIScreen;
                    if (battleScreen != null)
                    {
                        RestoreHiddenBattleUiCanvases();
                        battleScreen.gameObject.SetActive(true);
                    }
                }

                if (MonoBehaviourSingleton<PreloaderUI>.Instantiated)
                {
                    MonoBehaviourSingleton<PreloaderUI>.Instance.RaidInfoVisibility = true;
                }

                TryCloseAllActiveScreens();

                try
                {
                    player?.SetInventoryOpened(true);
                    player?.SetInventoryOpened(false);
                }
                catch
                {
                    // Best-effort only.
                }

                try
                {
                    var battleField = typeof(GamePlayerOwner).GetField("BattleUIScreenController", AnyBinding);
                    TrySetCurrentScreenController(battleField?.GetValue(owner));
                }
                catch
                {
                    // Best-effort only.
                }

                owner?.ShowBattleUIScreen();
                TryRevealBattleHud(owner);
                TryRestoreBattleHudController();
                QueuePreviewInventoryToggleRestore();
            }
            catch
            {
                // Best-effort only.
            }
        }

        internal static void RefreshBattleUiAfterEditorClose()
        {
            try
            {
                var player = GamePlayerOwner.MyPlayer;
                var owner = player != null ? player.GetComponent<GamePlayerOwner>() : null;

                GamePlayerOwner.SetIgnoreInput(false);
                GamePlayerOwner.SetIgnoreInputWithKeepResetLook(false);

                try
                {
                    player?.SetInventoryOpened(false);
                }
                catch
                {
                    // Best-effort only.
                }

                RestoreHiddenBattleUiCanvases();

                try
                {
                    if (MonoBehaviourSingleton<CommonUI>.Instantiated)
                    {
                        var battleScreen = MonoBehaviourSingleton<CommonUI>.Instance?.EftBattleUIScreen;
                        if (battleScreen != null)
                        {
                            battleScreen.gameObject.SetActive(true);
                        }
                    }
                }
                catch
                {
                    // Best-effort only.
                }

                try
                {
                    if (MonoBehaviourSingleton<PreloaderUI>.Instantiated)
                    {
                        var preloader = MonoBehaviourSingleton<PreloaderUI>.Instance;
                        preloader.RaidInfoVisibility = true;
                        preloader.ResetTimersForShowRttAndLoss();
                    }
                }
                catch
                {
                    // Best-effort only.
                }

                try
                {
                    var battleField = typeof(GamePlayerOwner).GetField("BattleUIScreenController", AnyBinding);
                    var battleController = battleField?.GetValue(owner);
                    TrySetCurrentScreenController(battleController);
                    var showScreen = battleController?.GetType().GetMethod(
                        "ShowScreen",
                        AnyBinding,
                        binder: null,
                        types: new[] { typeof(EScreenState) },
                        modifiers: null);
                    showScreen?.Invoke(battleController, new object[] { EScreenState.Root });
                }
                catch
                {
                    // Best-effort only.
                }

                TryRevealBattleHud(owner);
            }
            catch
            {
                // Best-effort only.
            }
        }

        internal static void RequestBattleUiRefreshAfterEditorClose()
        {
            try
            {
                RefreshBattleUiAfterEditorClose();
            }
            catch
            {
                // Best-effort only.
            }

            _postEditorCloseRestorePassesRemaining = 8;
            _nextPostEditorCloseRestoreAt = Time.unscaledTime + 0.05f;
        }

        private static void CaptureSnowPresentation()
        {
            CapturedSnowRenderers.Clear();
            ClearCapturedRainSnowLevel();
            _snowGlittersKeywordWasEnabled = Shader.IsKeywordEnabled("SNOW_GLITTERS");
            _winterSnowKeywordWasEnabled = Shader.IsKeywordEnabled("WINTER_SNOW");
            _snowMaskWasDisabled = !_winterSnowKeywordWasEnabled;

            try
            {
                var anyWinterRenderer = false;
                var maxWinterWetting = 0f;
                var maxWinterOpaqueness = 0f;

                foreach (var renderer in UnityEngine.Object.FindObjectsOfType<SnowWetRenderer>())
                {
                    if (renderer == null)
                    {
                        continue;
                    }

                    CapturedSnowRenderers.Add(new SnowRendererSnapshot(
                        renderer,
                        renderer.WinterShow,
                        renderer.enabled,
                        renderer.Wetting,
                        renderer.Opaqueness,
                        renderer.SpringSnowFactor,
                        renderer.StormEnabled));
                    if (renderer.WinterShow)
                    {
                        anyWinterRenderer = true;
                        maxWinterWetting = Mathf.Max(maxWinterWetting, renderer.Wetting);
                        maxWinterOpaqueness = Mathf.Max(maxWinterOpaqueness, renderer.Opaqueness);
                    }
                }

                _snowMaskWasDisabled = !anyWinterRenderer;
                CaptureRainSnowLevel(anyWinterRenderer, maxWinterWetting, maxWinterOpaqueness);
            }
            catch
            {
                CapturedSnowRenderers.Clear();
                CaptureRainSnowLevel(false, 0f, 0f);
            }
        }

        private static void CaptureRainSnowLevel(bool winterVisible, float snowRendererWetting, float snowRendererOpaqueness)
        {
            try
            {
                _capturedRainWetting = RainController.Wetting;
                _capturedRainOpaqueness = RainController.Opaqueness;
                _capturedRainIntensity = RainController.Intensity;
                if (winterVisible)
                {
                    _capturedRainWetting = Mathf.Max(_capturedRainWetting, snowRendererWetting);
                    _capturedRainOpaqueness = Mathf.Max(_capturedRainOpaqueness, snowRendererOpaqueness);
                }

                _rainStateCaptured = true;
            }
            catch
            {
                ClearCapturedRainSnowLevel();
            }
        }

        private static void ClearCapturedRainSnowLevel()
        {
            _rainStateCaptured = false;
            _capturedRainWetting = 0f;
            _capturedRainOpaqueness = 0f;
            _capturedRainIntensity = 0f;
        }

        internal static void EnsureBattleCameraUsedForWorldOverlays(Camera currentCamera)
        {
            if (!_battlePresentationApplied ||
                !_battleCameraStateCaptured ||
                currentCamera == null ||
                !ReferenceEquals(currentCamera, _battleCamera))
            {
                return;
            }

            TryPinManagedBattleCamera(_battleCamera);
        }

        internal static bool ShouldTreatCameraAsBattleWorldCamera(Camera currentCamera)
        {
            return _battlePresentationApplied &&
                   _battleCameraStateCaptured &&
                   currentCamera != null &&
                   ReferenceEquals(currentCamera, _battleCamera);
        }

        internal static bool ShouldInvertBattleCameraUpscalerFinalFlip(object upscaler)
        {
            if (!IsPreviewTransitionActiveOrOpen || upscaler == null)
            {
                return false;
            }

            try
            {
                var camera = GetFieldValue(upscaler, "_currentCamera") as Camera ??
                             GetFieldValue(upscaler, "_camera") as Camera;

                if (camera == null && upscaler is Component component)
                {
                    camera = component.GetComponent<Camera>();
                }

                if (camera == null)
                {
                    return false;
                }

                if (_battleCameraStateCaptured && ReferenceEquals(camera, _battleCamera))
                {
                    return true;
                }

                return LooksLikeBattleWorldCamera(camera) && !IsPreviewOrUiCamera(camera);
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksLikeBattleWorldCamera(Camera camera)
        {
            if (camera == null ||
                !camera.enabled ||
                !camera.gameObject.activeInHierarchy ||
                camera.targetTexture != null)
            {
                return false;
            }

            var rect = camera.rect;
            if (rect.width < 0.95f || rect.height < 0.95f)
            {
                return false;
            }

            var cullingMask = camera.cullingMask;
            if (cullingMask == 0 ||
                cullingMask == (1 << 5) ||
                cullingMask == (1 << 19))
            {
                return false;
            }

            try
            {
                if (camera.CompareTag("MainCamera"))
                {
                    return true;
                }
            }
            catch
            {
                // Some Unity objects can throw while tags are being torn down.
            }

            var path = GetTransformPath(camera.transform);
            return path.IndexOf("FPS Camera", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPreviewOrUiCamera(Camera camera)
        {
            if (camera == null)
            {
                return true;
            }

            if (IsConfiguredPreviewCamera(camera))
            {
                return true;
            }

            return IsKnownUiCamera(camera);
        }

        private static bool IsConfiguredPreviewCamera(Camera camera)
        {
            if (camera == null)
            {
                return false;
            }

            if (ReferenceEquals(camera, _configuredPreviewCamera))
            {
                return true;
            }

            var path = GetTransformPath(camera.transform);
            return path.IndexOf("Weapon Camera(Clone)", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsKnownUiCamera(Camera camera)
        {
            if (camera == null)
            {
                return true;
            }

            var path = GetTransformPath(camera.transform);
            return path.IndexOf("Weapon Modding Screen/UI Camera", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("EditBuildScreen/UI Camera", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("Genius Camera", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSnowWetRendererAcceptedCamera(Camera camera, out bool acceptedMain, out bool acceptedOptic)
        {
            acceptedMain = false;
            acceptedOptic = false;

            if (camera == null)
            {
                return false;
            }

            try
            {
                var cameraManager = EFT.CameraControl.CameraManager.Instance;
                acceptedMain = cameraManager?.Camera != null && ReferenceEquals(camera, cameraManager.Camera);
                acceptedOptic = cameraManager?.OpticCameraManager?.Camera != null &&
                                ReferenceEquals(camera, cameraManager.OpticCameraManager.Camera);
                return acceptedMain || acceptedOptic;
            }
            catch
            {
                return false;
            }
        }

        private static void TryPinManagedBattleCamera(Camera battleCamera)
        {
            if (battleCamera == null)
            {
                return;
            }

            if (_battleCameraStateCaptured && !ReferenceEquals(battleCamera, _battleCamera))
            {
                return;
            }

            try
            {
                var cameraManager = EFT.CameraControl.CameraManager.Instance;
                if (cameraManager != null && !ReferenceEquals(cameraManager.Camera, battleCamera))
                {
                    cameraManager.Camera = battleCamera;
                }
            }
            catch
            {
                // Best-effort only during preview render.
            }
        }

        private static void RestoreCapturedSnowRenderersDuringPreview()
        {
            TryRestoreRainSnowLevelDuringPreview();

            if (CapturedSnowRenderers.Count == 0)
            {
                WaterRendererv3.DisableSnowMask(_snowMaskWasDisabled);
                return;
            }

            var dirtyField = GetSnowRendererDirtyField();
            var winterVisible = false;

            foreach (var snapshot in CapturedSnowRenderers)
            {
                try
                {
                    if (snapshot.Renderer == null)
                    {
                        continue;
                    }

                    snapshot.Renderer.enabled = snapshot.Enabled;

                    if (snapshot.Renderer.WinterShow != snapshot.WinterShow)
                    {
                        snapshot.Renderer.WinterShow = snapshot.WinterShow;
                    }

                    snapshot.Renderer.Wetting = snapshot.Wetting;
                    snapshot.Renderer.Opaqueness = snapshot.Opaqueness;
                    snapshot.Renderer.SpringSnowFactor = snapshot.SpringSnowFactor;
                    snapshot.Renderer.StormEnabled = snapshot.StormEnabled;
                    dirtyField?.SetValue(snapshot.Renderer, false);
                    snapshot.Renderer.method_2();
                    winterVisible |= snapshot.WinterShow;
                }
                catch
                {
                    // Best-effort only during preview.
                }
            }

            WaterRendererv3.DisableSnowMask(!winterVisible);
            if (winterVisible)
            {
                Shader.EnableKeyword("WINTER_SNOW");
            }
            else
            {
                Shader.DisableKeyword("WINTER_SNOW");
            }
        }

        private static void TryRestoreRainSnowLevelDuringPreview()
        {
            if (!TryGetPreviewRainSnowTargets(out var targetWetting, out var targetOpaqueness))
            {
                return;
            }

            try
            {
                if (Mathf.Abs(RainController.Wetting - targetWetting) > 0.001f)
                {
                    var rainController = UnityEngine.Object.FindObjectOfType<RainController>();
                    if (rainController != null)
                    {
                        rainController.SetWetting(targetWetting);
                    }
                    else
                    {
                        RainController.Wetting = targetWetting;
                    }
                }

                if (Mathf.Abs(RainController.Opaqueness - targetOpaqueness) > 0.001f)
                {
                    var rainController = UnityEngine.Object.FindObjectOfType<RainController>();
                    if (rainController != null)
                    {
                        rainController.SetOpaqueness(targetOpaqueness);
                    }
                    else
                    {
                        RainController.Opaqueness = targetOpaqueness;
                    }
                }
            }
            catch
            {
                // Best-effort only; snow renderer state is still restored separately.
            }
        }

        private static bool TryGetPreviewRainSnowTargets(out float targetWetting, out float targetOpaqueness)
        {
            targetWetting = 0f;
            targetOpaqueness = 0f;

            var hasTarget = false;
            if (_rainStateCaptured)
            {
                targetWetting = _capturedRainWetting;
                targetOpaqueness = _capturedRainOpaqueness;
                hasTarget = true;
            }

            foreach (var snapshot in CapturedSnowRenderers)
            {
                if (!snapshot.WinterShow)
                {
                    continue;
                }

                targetWetting = Mathf.Max(targetWetting, snapshot.Wetting);
                targetOpaqueness = Mathf.Max(targetOpaqueness, snapshot.Opaqueness);
                hasTarget = true;
            }

            if (!hasTarget)
            {
                return false;
            }

            targetWetting = Mathf.Clamp01(targetWetting);
            targetOpaqueness = Mathf.Clamp01(targetOpaqueness);
            return true;
        }

        internal static bool TryPreservePreviewRainWetting(ref float wetting)
        {
            if (!IsPreviewTransitionActiveOrOpen ||
                !TryGetPreviewRainSnowTargets(out var targetWetting, out _))
            {
                return false;
            }

            if (targetWetting > wetting + 0.001f)
            {
                wetting = targetWetting;
                return true;
            }

            return false;
        }

        internal static bool TryPreservePreviewRainOpaqueness(ref float opaqueness)
        {
            if (!IsPreviewTransitionActiveOrOpen ||
                !TryGetPreviewRainSnowTargets(out _, out var targetOpaqueness))
            {
                return false;
            }

            if (targetOpaqueness > opaqueness + 0.001f)
            {
                opaqueness = targetOpaqueness;
                return true;
            }

            return false;
        }

        private static void RestoreCapturedSnowRendererStates()
        {
            TryRestoreRainSnowLevelDuringPreview();

            if (CapturedSnowRenderers.Count == 0)
            {
                WaterRendererv3.DisableSnowMask(_snowMaskWasDisabled);
                return;
            }

            var dirtyField = GetSnowRendererDirtyField();
            var winterVisible = false;

            foreach (var snapshot in CapturedSnowRenderers)
            {
                try
                {
                    if (snapshot.Renderer == null)
                    {
                        continue;
                    }

                    snapshot.Renderer.enabled = snapshot.Enabled;
                    snapshot.Renderer.WinterShow = snapshot.WinterShow;
                    snapshot.Renderer.Wetting = snapshot.Wetting;
                    snapshot.Renderer.Opaqueness = snapshot.Opaqueness;
                    snapshot.Renderer.SpringSnowFactor = snapshot.SpringSnowFactor;
                    snapshot.Renderer.StormEnabled = snapshot.StormEnabled;
                    dirtyField?.SetValue(snapshot.Renderer, false);
                    snapshot.Renderer.method_2();
                    winterVisible |= snapshot.WinterShow;
                }
                catch
                {
                    // Ignore teardown issues.
                }
            }

            WaterRendererv3.DisableSnowMask(!winterVisible);
            CapturedSnowRenderers.Clear();
        }

        private static FieldInfo GetSnowRendererDirtyField()
        {
            if (_snowRendererDirtyField != null)
            {
                return _snowRendererDirtyField;
            }

            _snowRendererDirtyField = typeof(SnowWetRenderer).GetField("_winterShowUpdated", AnyBinding);
            return _snowRendererDirtyField;
        }

        private static void TrySetCurrentScreenController(object battleController)
        {
            if (battleController is not EFT.UI.Screens.IBaseScreenController<EEftScreenType> typedBattleController)
            {
                return;
            }

            var screenManager = EFT.UI.Screens.EftScreenManager.Instance;
            if (screenManager == null)
            {
                return;
            }

            if (!ReferenceEquals(screenManager.CurrentBaseScreenController, typedBattleController))
            {
                screenManager.CurrentBaseScreenController = typedBattleController;
            }
        }

        private static void TryCloseAllActiveScreens(bool forceEvenIfBattleUi = false)
        {
            try
            {
                var screenManager = EFT.UI.Screens.EftScreenManager.Instance;
                if (screenManager == null)
                {
                    return;
                }

                var current = screenManager.CurrentBaseScreenController;
                if (!forceEvenIfBattleUi && current != null && current.ScreenType == EEftScreenType.BattleUI)
                {
                    return;
                }

                screenManager.CloseAllScreensForced();
            }
            catch
            {
                // Best-effort only.
            }
        }

        private static void TryRevealBattleHud(GamePlayerOwner owner)
        {
            if (owner == null)
            {
                return;
            }

            try
            {
                MonoBehaviourSingleton<PreloaderUI>.Instance?.ResetTimersForShowRttAndLoss();
            }
            catch
            {
                // Best-effort only.
            }

            try
            {
                GamePlayerOwner.MyPlayer?.UpdateInteractionCast();
            }
            catch
            {
                // Best-effort only.
            }
        }

        private static void SuppressBattleCameraBehaviours(Camera battleCamera)
        {
            SuppressBattleCameraBehaviours(battleCamera, BattleCameraSuppressionTypeNames);
        }

        private static void SuppressBattleCameraSupersampling(Camera battleCamera, bool exhaustive)
        {
            SuppressBattleCameraBehaviours(battleCamera, BattleCameraSupersamplingSuppressionTypeNames);
            ReassertSuppressedBattleCameraBehaviours(BattleCameraSupersamplingSuppressionTypeNames);

            if (!exhaustive)
            {
                return;
            }

            foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera == null || !camera.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (camera == battleCamera ||
                    GetTransformPath(camera.transform).IndexOf("FPS Camera", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SuppressBattleCameraBehaviours(camera, BattleCameraSupersamplingSuppressionTypeNames);
                }
            }

            foreach (var behaviour in Resources.FindObjectsOfTypeAll<Behaviour>())
            {
                if (behaviour == null ||
                    !behaviour.enabled ||
                    behaviour is Camera ||
                    !ShouldSuppressBattleCameraBehaviourForPreview(behaviour, BattleCameraSupersamplingSuppressionTypeNames) ||
                    !IsLikelyFpsCameraEffect(behaviour, battleCamera))
                {
                    continue;
                }

                behaviour.enabled = false;
                if (!SuppressedBattleCameraBehaviours.Contains(behaviour))
                {
                    SuppressedBattleCameraBehaviours.Add(behaviour);
                }
            }
        }

        private static void ReassertSuppressedBattleCameraBehaviours(string[] typeNames)
        {
            foreach (var behaviour in SuppressedBattleCameraBehaviours.ToArray())
            {
                if (behaviour == null ||
                    !behaviour.enabled ||
                    !ShouldSuppressBattleCameraBehaviourForPreview(behaviour, typeNames))
                {
                    continue;
                }

                behaviour.enabled = false;
            }
        }

        private static bool IsLikelyFpsCameraEffect(Behaviour behaviour, Camera battleCamera)
        {
            if (behaviour == null)
            {
                return false;
            }

            var transform = behaviour.transform;
            if (battleCamera != null && transform != null &&
                (transform == battleCamera.transform || transform.IsChildOf(battleCamera.transform)))
            {
                return true;
            }

            var path = GetTransformPath(transform);
            return path.IndexOf("FPS Camera", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void SuppressBattleCameraBehaviours(Camera battleCamera, string[] typeNames)
        {
            if (battleCamera == null)
            {
                return;
            }

            foreach (var behaviour in battleCamera.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour == null || !behaviour.enabled || behaviour is Camera)
                {
                    continue;
                }

                if (!ShouldSuppressBattleCameraBehaviourForPreview(behaviour, typeNames))
                {
                    continue;
                }

                behaviour.enabled = false;
                if (!SuppressedBattleCameraBehaviours.Contains(behaviour))
                {
                    SuppressedBattleCameraBehaviours.Add(behaviour);
                }
            }
        }

        private static bool ShouldSuppressBattleCameraBehaviourForPreview(Behaviour behaviour, string[] typeNames)
        {
            if (behaviour == null || behaviour is Camera || typeNames == null || typeNames.Length == 0)
            {
                return false;
            }

            var type = behaviour.GetType();
            var typeName = type.Name;
            return typeNames.Contains(typeName, StringComparer.Ordinal);
        }

        private static void RestoreBattleCameraBehaviours()
        {
            if (SuppressedBattleCameraBehaviours.Count == 0)
            {
                return;
            }

            foreach (var behaviour in SuppressedBattleCameraBehaviours)
            {
                if (behaviour != null)
                {
                    behaviour.enabled = true;
                }
            }

            SuppressedBattleCameraBehaviours.Clear();
        }

        private static void EnsurePreviewCameraCallbacks()
        {
            if (!_previewCameraPreCullRegistered)
            {
                Camera.onPreCull += HandlePreviewCameraPreCull;
                _previewCameraPreCullRegistered = true;
            }

            if (!_previewCameraPreRenderRegistered)
            {
                Camera.onPreRender += HandlePreviewCameraPreRender;
                _previewCameraPreRenderRegistered = true;
            }

            if (!_previewCameraPostRenderRegistered)
            {
                Camera.onPostRender += HandlePreviewCameraPostRender;
                _previewCameraPostRenderRegistered = true;
            }
        }

        private static void RemovePreviewCameraCallbacks()
        {
            if (_previewCameraPreCullRegistered)
            {
                Camera.onPreCull -= HandlePreviewCameraPreCull;
                _previewCameraPreCullRegistered = false;
            }

            if (_previewCameraPreRenderRegistered)
            {
                Camera.onPreRender -= HandlePreviewCameraPreRender;
                _previewCameraPreRenderRegistered = false;
            }

            if (_previewCameraPostRenderRegistered)
            {
                Camera.onPostRender -= HandlePreviewCameraPostRender;
                _previewCameraPostRenderRegistered = false;
            }

            RestorePreviewCameraAmbientFill();
        }

        private static void HandlePreviewCameraPreCull(Camera camera)
        {
            if (camera == null || camera != _configuredPreviewCamera || _forcedPreviewRenderTexture == null)
            {
                return;
            }

            var previous = RenderTexture.active;

            try
            {
                RenderTexture.active = _forcedPreviewRenderTexture;
                GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
            }
            catch
            {
                // Best-effort only.
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private static void HandlePreviewCameraPreRender(Camera camera)
        {
            if (!UsePreviewCameraAmbientFillWithoutPreviewLights ||
                camera == null ||
                camera != _configuredPreviewCamera ||
                !_previewCameraAmbientOverrideActive &&
                !IsPreviewTransitionActiveOrOpen)
            {
                return;
            }

            if (_previewCameraAmbientOverrideActive)
            {
                return;
            }

            try
            {
                PreviewCameraAmbientSnapshot.Clear();
                PreviewCameraAmbientSnapshot.Capture();
                _previewCameraAmbientOverrideActive = true;
                _previewCameraWeaponPreviewKeywordWasEnabled = Shader.IsKeywordEnabled("WeaponPreview");

                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientIntensity = 1f;
                RenderSettings.ambientLight = PreviewLightFreeAmbientFill;
                RenderSettings.ambientSkyColor = PreviewLightFreeAmbientFill;
                RenderSettings.ambientEquatorColor = PreviewLightFreeAmbientFill;
                RenderSettings.ambientGroundColor = PreviewLightFreeAmbientFill;
                RenderSettings.fog = false;

                if (ForceWeaponPreviewShaderKeywordDuringLightFreePreview)
                {
                    Shader.EnableKeyword("WeaponPreview");
                }
            }
            catch
            {
                RestorePreviewCameraAmbientFill();
            }
        }

        private static void HandlePreviewCameraPostRender(Camera camera)
        {
            if (camera == null || camera != _configuredPreviewCamera)
            {
                return;
            }

            RestorePreviewCameraAmbientFill();
        }

        private static void RestorePreviewCameraAmbientFill()
        {
            if (!_previewCameraAmbientOverrideActive)
            {
                return;
            }

            try
            {
                if (ForceWeaponPreviewShaderKeywordDuringLightFreePreview && _previewCameraWeaponPreviewKeywordWasEnabled)
                {
                    Shader.EnableKeyword("WeaponPreview");
                }
                else if (ForceWeaponPreviewShaderKeywordDuringLightFreePreview)
                {
                    Shader.DisableKeyword("WeaponPreview");
                }

                PreviewCameraAmbientSnapshot.Restore();
            }
            catch
            {
                // Best-effort only; the per-frame battle settings snapshot will recover the raid values.
            }
            finally
            {
                _previewCameraAmbientOverrideActive = false;
                PreviewCameraAmbientSnapshot.Clear();
                _previewCameraWeaponPreviewKeywordWasEnabled = false;
            }
        }

        private static void TryApplyViewporterRect(CameraViewporter viewporter, Camera previewCamera)
        {
            if (viewporter == null || previewCamera == null)
            {
                return;
            }

            try
            {
                var rectTransform = viewporter.transform as RectTransform;
                var canvas = viewporter.GetComponentInParent<Canvas>();
                if (rectTransform == null || canvas == null)
                {
                    return;
                }

                switch (canvas.renderMode)
                {
                    case RenderMode.ScreenSpaceOverlay:
                    {
                        var min = viewporter.transform.TransformPoint(rectTransform.rect.min);
                        var max = viewporter.transform.TransformPoint(rectTransform.rect.max);
                        var minViewport = new Vector2(min.x / Screen.width, min.y / Screen.height);
                        var maxViewport = new Vector2(max.x / Screen.width, max.y / Screen.height);
                        previewCamera.rect = new Rect(
                            minViewport.x,
                            minViewport.y,
                            Mathf.Max(0.001f, maxViewport.x - minViewport.x),
                            Mathf.Max(0.001f, maxViewport.y - minViewport.y));
                        break;
                    }
                    case RenderMode.ScreenSpaceCamera:
                    {
                        var canvasCamera = canvas.worldCamera;
                        if (canvasCamera == null)
                        {
                            return;
                        }

                        var min = viewporter.transform.TransformPoint(rectTransform.rect.min);
                        var max = viewporter.transform.TransformPoint(rectTransform.rect.max);
                        var minViewport3 = canvasCamera.WorldToViewportPoint(min);
                        var maxViewport3 = canvasCamera.WorldToViewportPoint(max);
                        previewCamera.rect = new Rect(
                            minViewport3.x,
                            minViewport3.y,
                            Mathf.Max(0.001f, maxViewport3.x - minViewport3.x),
                            Mathf.Max(0.001f, maxViewport3.y - minViewport3.y));
                        break;
                    }
                }
            }
            catch
            {
                // If this fails, keep the existing camera rect.
            }
        }

        private static void EnsurePreviewRenderTarget(CameraViewporter viewporter, Camera previewCamera)
        {
            if (viewporter == null || previewCamera == null)
            {
                return;
            }

            var host = viewporter.transform as RectTransform;
            if (host == null)
            {
                return;
            }

            var canvas = viewporter.GetComponentInParent<Canvas>();
            var scaleFactor = canvas != null && canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
            var size = host.rect.size;
            var hostWidth = Mathf.Max(64f, size.x * scaleFactor);
            var hostHeight = Mathf.Max(64f, size.y * scaleFactor);
            var hostAspect = Mathf.Max(0.1f, hostWidth / Mathf.Max(1f, hostHeight));
            var width = Mathf.RoundToInt(hostWidth);
            var height = Mathf.RoundToInt(hostHeight);
            if (width > 2048)
            {
                width = 2048;
                height = Mathf.RoundToInt(width / hostAspect);
            }

            if (height > 2048)
            {
                height = 2048;
                width = Mathf.RoundToInt(height * hostAspect);
            }

            width = Mathf.Clamp(width, 256, 2048);
            height = Mathf.Clamp(height, 256, 2048);

            var hostChanged = _forcedPreviewHost != host;
            var sizeChanged = width != _forcedPreviewWidth || height != _forcedPreviewHeight;
            if (_forcedPreviewRenderTexture == null || hostChanged || sizeChanged)
            {
                DestroyPreviewRenderTarget();

                _forcedPreviewHost = host;
                _forcedPreviewWidth = width;
                _forcedPreviewHeight = height;
                _forcedPreviewRenderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                {
                    name = "ULE Preview Panel RT",
                    antiAliasing = 1,
                    anisoLevel = 0
                };

                var overlay = new GameObject("ULE Preview RT", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                var overlayTransform = (RectTransform)overlay.transform;
                overlayTransform.SetParent(host, false);
                overlayTransform.anchorMin = Vector2.zero;
                overlayTransform.anchorMax = Vector2.one;
                overlayTransform.offsetMin = Vector2.zero;
                overlayTransform.offsetMax = Vector2.zero;
                overlayTransform.localScale = Vector3.one;
                overlayTransform.localRotation = Quaternion.identity;
                overlayTransform.SetAsFirstSibling();

                _forcedPreviewRawImage = overlay.GetComponent<RawImage>();
                _forcedPreviewRawImage.raycastTarget = false;
                _forcedPreviewRawImage.color = Color.white;
                _forcedPreviewRawImage.texture = _forcedPreviewRenderTexture;
                _forcedPreviewRawImage.uvRect = new Rect(0f, 0f, 1f, 1f);

                _forcedPreviewRenderTextureLogged = true;
                LogDebug($"[ULE] Preview RT enabled: {width}x{height}, camera={GetTransformPath(previewCamera.transform)}, cullingMask=0x{previewCamera.cullingMask:X8}.");
            }
            else if (_forcedPreviewRawImage != null && _forcedPreviewRawImage.texture != _forcedPreviewRenderTexture)
            {
                _forcedPreviewRawImage.texture = _forcedPreviewRenderTexture;
            }

            if (_forcedPreviewRawImage != null && !_forcedPreviewRawImage.gameObject.activeSelf)
            {
                _forcedPreviewRawImage.gameObject.SetActive(true);
            }

            if (ForcePreviewIntoRenderTexture)
            {
                previewCamera.targetTexture = _forcedPreviewRenderTexture;
                previewCamera.rect = new Rect(0f, 0f, 1f, 1f);
            }
        }

        private static void SuppressPreviewHostGraphics(RectTransform host)
        {
            if (host == null)
            {
                return;
            }

            var hostSize = host.rect.size;
            foreach (var behaviour in host.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour == null || !behaviour.enabled || !behaviour.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (_forcedPreviewRawImage != null && behaviour.gameObject == _forcedPreviewRawImage.gameObject)
                {
                    continue;
                }

                var typeName = behaviour.GetType().Name;
                if (string.Equals(typeName, "CameraImage", StringComparison.Ordinal))
                {
                    behaviour.enabled = false;
                    if (!SuppressedBackdropGraphics.Contains(behaviour))
                    {
                        SuppressedBackdropGraphics.Add(behaviour);
                    }
                    continue;
                }

                if (!string.Equals(typeName, "RawImage", StringComparison.Ordinal) &&
                    !string.Equals(typeName, "Image", StringComparison.Ordinal))
                {
                    continue;
                }

                var rect = behaviour.GetComponent<RectTransform>();
                if (rect == null)
                {
                    continue;
                }

                var size = rect.rect.size;
                var fillsHost = size.x >= hostSize.x * 0.85f && size.y >= hostSize.y * 0.85f;
                var stretched = rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one;
                if (!fillsHost && !stretched)
                {
                    continue;
                }

                behaviour.enabled = false;
                if (!SuppressedBackdropGraphics.Contains(behaviour))
                {
                    SuppressedBackdropGraphics.Add(behaviour);
                }
            }
        }

        private static void DestroyPreviewRenderTarget()
        {
            if (_forcedPreviewRawImage != null)
            {
                try
                {
                    UnityEngine.Object.DestroyImmediate(_forcedPreviewRawImage.gameObject);
                }
                catch
                {
                    // Ignore cleanup timing issues.
                }
                finally
                {
                    _forcedPreviewRawImage = null;
                }
            }

            if (_forcedPreviewRenderTexture != null)
            {
                if (_forcedPreviewRenderTextureLogged)
                {
                    LogDebug("[ULE] Preview RT released.");
                }

                try
                {
                    _forcedPreviewRenderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(_forcedPreviewRenderTexture);
                }
                catch
                {
                    // Ignore cleanup timing issues.
                }
                finally
                {
                    _forcedPreviewRenderTexture = null;
                    _forcedPreviewRenderTextureLogged = false;
                }
            }

            _forcedPreviewHost = null;
            _forcedPreviewWidth = 0;
            _forcedPreviewHeight = 0;
        }

        private static void SuppressAuxiliaryScreenCameras(Component screen, Camera previewCamera)
        {
            RestoreAuxiliaryCameras();

            if (screen == null)
            {
                return;
            }

            var battleCamera = GetBattleCamera();
            Camera opticCamera = null;
            try
            {
                opticCamera = EFT.CameraControl.CameraManager.Instance?.OpticCameraManager?.Camera;
            }
            catch
            {
                opticCamera = null;
            }

            foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera == null || !camera.enabled || !camera.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (camera == previewCamera || camera == battleCamera || camera == opticCamera)
                {
                    continue;
                }

                if (SuppressedAuxiliaryCameras.Contains(camera))
                {
                    continue;
                }

                var path = GetTransformPath(camera.transform);
                if (SuppressEditBuildGeniusCameraDuringPreview &&
                    path.IndexOf("EditBuildScreen/Genius Camera", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    camera.enabled = false;
                    SuppressedAuxiliaryCameras.Add(camera);
                    if (EnablePreviewDiagnostics)
                    {
                        _log?.LogInfo($"[ULE] Suppressed EditBuild Genius Camera during preview: {path}");
                    }
                    continue;
                }

                var isScreenCamera = screen.transform != null && camera.transform.IsChildOf(screen.transform);
                if (isScreenCamera)
                {
                    continue;
                }

                var fullScreenRect =
                    camera.rect.width >= 0.99f &&
                    camera.rect.height >= 0.99f &&
                    camera.rect.x <= 0.01f &&
                    camera.rect.y <= 0.01f;

                var looksLikeUiOnly = camera.cullingMask == 0x00000020;
                var looksLikePreviewOnly = camera.cullingMask == previewCamera?.cullingMask;
                var hasWorldLayers = camera.cullingMask != 0 && !looksLikeUiOnly && !looksLikePreviewOnly;

                if (!fullScreenRect || !hasWorldLayers || camera.targetTexture != null)
                {
                    continue;
                }

                camera.enabled = false;
                SuppressedAuxiliaryCameras.Add(camera);
                if (EnablePreviewDiagnostics)
                {
                    _log?.LogInfo($"[ULE] Suppressed extra world camera during preview: {path}");
                }
            }
        }

        private static void SuppressPreviewCameraBehaviours(Camera previewCamera)
        {
            RestorePreviewCameraBehaviours();

            if (previewCamera == null)
            {
                return;
            }

            foreach (var behaviour in previewCamera.GetComponents<Behaviour>())
            {
                if (behaviour == null || !behaviour.enabled)
                {
                    continue;
                }

                if (behaviour is Camera)
                {
                    continue;
                }

                behaviour.enabled = false;
                SuppressedPreviewCameraBehaviours.Add(behaviour);
            }
        }

        private static void RestorePreviewCameraBehaviours()
        {
            if (SuppressedPreviewCameraBehaviours.Count == 0)
            {
                return;
            }

            foreach (var behaviour in SuppressedPreviewCameraBehaviours)
            {
                if (behaviour != null)
                {
                    behaviour.enabled = true;
                }
            }

            SuppressedPreviewCameraBehaviours.Clear();
        }

        private static void RestoreAuxiliaryCameras()
        {
            if (SuppressedAuxiliaryCameras.Count == 0)
            {
                return;
            }

            foreach (var camera in SuppressedAuxiliaryCameras)
            {
                if (camera != null)
                {
                    camera.enabled = true;
                }
            }

            SuppressedAuxiliaryCameras.Clear();
        }

        private static void TrySuppressScreenBackground()
        {
            if (!SuppressWeaponModdingScreenBackdrops)
            {
                return;
            }

            if (_backgroundSuppressionAttempted)
            {
                return;
            }

            var screen = GetLiveEditBuildScreen();
            if (screen == null || !screen.gameObject.activeInHierarchy)
            {
                return;
            }

            _backgroundSuppressionAttempted = true;

            var rootRect = screen.transform as RectTransform;
            var rootSize = rootRect != null && rootRect.rect.size.sqrMagnitude > 0.01f
                ? rootRect.rect.size
                : new Vector2(Screen.width, Screen.height);
            var previewCamera = screen.GetComponentInChildren<CameraViewporter>(true)?.TargetCamera;
            var previewHost = screen.GetComponentInChildren<CameraViewporter>(true)?.transform as RectTransform;

            foreach (var cameraImage in screen.GetComponentsInChildren<CameraImage>(true))
            {
                if (cameraImage == null || !cameraImage.enabled)
                {
                    continue;
                }

                if (IsPreviewCameraImage(cameraImage, previewCamera, previewHost))
                {
                    continue;
                }

                TryRestoreCameraImageLighting(cameraImage);
                cameraImage.enabled = false;
                if (!SuppressedBackdropBehaviours.Contains(cameraImage))
                {
                    SuppressedBackdropBehaviours.Add(cameraImage);
                }
            }

            foreach (var rawImage in screen.GetComponentsInChildren<RawImage>(true))
            {
                if (rawImage == null || !rawImage.enabled || rawImage.gameObject == (_forcedPreviewRawImage != null ? _forcedPreviewRawImage.gameObject : null))
                {
                    continue;
                }

                if (previewHost != null && rawImage.transform != null && rawImage.transform.IsChildOf(previewHost))
                {
                    continue;
                }

                var rect = rawImage.transform as RectTransform;
                if (rect == null)
                {
                    continue;
                }

                var size = rect.rect.size;
                var largeEnough = size.x >= rootSize.x * 0.45f && size.y >= rootSize.y * 0.45f;
                var stretched = rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one;
                var hasPreviewTexture = rawImage.texture is RenderTexture ||
                                        (rawImage.texture != null && rawImage.texture.name.IndexOf("CameraImage", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!hasPreviewTexture || (!largeEnough && !stretched))
                {
                    continue;
                }

                rawImage.enabled = false;
                if (!SuppressedBackdropGraphics.Contains(rawImage))
                {
                    SuppressedBackdropGraphics.Add(rawImage);
                }
            }

            foreach (var rect in screen.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect == null || rect == rootRect)
                {
                    continue;
                }

                var gameObject = rect.gameObject;
                if (gameObject == null || !gameObject.activeSelf)
                {
                    continue;
                }

                if (_forcedPreviewRawImage != null && gameObject == _forcedPreviewRawImage.gameObject)
                {
                    continue;
                }

                if (!LooksLikeBackdrop(gameObject, rect, rootSize))
                {
                    continue;
                }

                gameObject.SetActive(false);
                SuppressedBackgroundObjects.Add(gameObject);
            }

            foreach (var behaviour in screen.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour == null || !behaviour.enabled || !behaviour.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var typeName = behaviour.GetType().Name;
                if (!string.Equals(typeName, "CameraImage", StringComparison.Ordinal) &&
                    !string.Equals(typeName, "RawImage", StringComparison.Ordinal) &&
                    !string.Equals(typeName, "Image", StringComparison.Ordinal))
                {
                    continue;
                }

                if (_forcedPreviewRawImage != null && behaviour.gameObject == _forcedPreviewRawImage.gameObject)
                {
                    continue;
                }

                if (!IsScreenBackdropGraphicCandidate(behaviour.gameObject, rootSize))
                {
                    continue;
                }

                behaviour.enabled = false;
                if (!SuppressedBackdropGraphics.Contains(behaviour))
                {
                    SuppressedBackdropGraphics.Add(behaviour);
                }
            }
        }

        private static void TrySuppressCameraBackedScreenBackdrops(EditBuildScreen screen)
        {
            if (!SuppressCameraBackedEditBuildBackdrops ||
                screen == null ||
                !screen.gameObject.activeInHierarchy)
            {
                return;
            }

            var rootRect = screen.transform as RectTransform;
            var rootSize = rootRect != null && rootRect.rect.size.sqrMagnitude > 0.01f
                ? rootRect.rect.size
                : new Vector2(Screen.width, Screen.height);

            foreach (var cameraImage in screen.GetComponentsInChildren<CameraImage>(true))
            {
                if (cameraImage == null || !cameraImage.enabled || !cameraImage.gameObject.activeInHierarchy)
                {
                    continue;
                }

                TryRestoreCameraImageLighting(cameraImage);
                cameraImage.enabled = false;
                if (!SuppressedBackdropBehaviours.Contains(cameraImage))
                {
                    SuppressedBackdropBehaviours.Add(cameraImage);
                }
            }

            foreach (var rawImage in screen.GetComponentsInChildren<RawImage>(true))
            {
                if (rawImage == null ||
                    !rawImage.enabled ||
                    !rawImage.gameObject.activeInHierarchy ||
                    rawImage.gameObject == (_forcedPreviewRawImage != null ? _forcedPreviewRawImage.gameObject : null))
                {
                    continue;
                }

                var rect = rawImage.transform as RectTransform;
                if (rect == null)
                {
                    continue;
                }

                var size = rect.rect.size;
                var largeEnough = size.x >= rootSize.x * 0.45f && size.y >= rootSize.y * 0.45f;
                var stretched = rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one;
                var cameraBackedTexture = rawImage.texture is RenderTexture ||
                                          (rawImage.texture != null &&
                                           rawImage.texture.name.IndexOf("CameraImage", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!cameraBackedTexture || (!largeEnough && !stretched))
                {
                    continue;
                }

                rawImage.enabled = false;
                if (!SuppressedBackdropGraphics.Contains(rawImage))
                {
                    SuppressedBackdropGraphics.Add(rawImage);
                }
            }
        }

        private static bool LooksLikeBackdrop(GameObject gameObject, RectTransform rect, Vector2 rootSize)
        {
            var name = (gameObject.name ?? string.Empty).ToLowerInvariant();
            if (ProtectedNameTokens.Any(name.Contains))
            {
                return false;
            }

            var componentNames = gameObject
                .GetComponents<Component>()
                .Where(component => component != null)
                .Select(component => component.GetType().Name)
                .ToArray();

            if (componentNames.Any(componentName => ProtectedComponentNames.Contains(componentName, StringComparer.Ordinal)))
            {
                return false;
            }

            var namedBackdrop = BackgroundNameTokens.Any(name.Contains);
            var visualBackdrop = componentNames.Any(componentName => BackdropComponentNames.Contains(componentName, StringComparer.Ordinal));
            if (!namedBackdrop && !visualBackdrop)
            {
                return false;
            }

            var size = rect.rect.size;
            var largeEnough = size.x >= rootSize.x * 0.45f && size.y >= rootSize.y * 0.45f;
            var stretched = rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one;
            return largeEnough || stretched;
        }

        private static void RestoreSuppressedBackground()
        {
            if (SuppressedBackgroundObjects.Count > 0)
            {
                foreach (var gameObject in SuppressedBackgroundObjects)
                {
                    if (gameObject != null)
                    {
                        gameObject.SetActive(true);
                    }
                }

                SuppressedBackgroundObjects.Clear();
            }

            _backgroundSuppressionAttempted = false;
        }

        private static void TrySuppressBackdropGraphic(Behaviour behaviour, Vector2 rootSize)
        {
            if (behaviour == null || !behaviour.enabled)
            {
                return;
            }

            if (!IsBackdropGraphicCandidate(behaviour.gameObject, rootSize))
            {
                return;
            }

            behaviour.enabled = false;
            if (!SuppressedBackdropGraphics.Contains(behaviour))
            {
                SuppressedBackdropGraphics.Add(behaviour);
            }
        }

        private static bool IsBackdropGraphicCandidate(GameObject gameObject, Vector2 rootSize)
        {
            if (gameObject == null || !gameObject.activeInHierarchy)
            {
                return false;
            }

            if (HasProtectedAncestorOrSelf(gameObject))
            {
                return false;
            }

            var rect = gameObject.GetComponent<RectTransform>();
            if (rect == null)
            {
                return false;
            }

            return LooksLikeBackdrop(gameObject, rect, rootSize);
        }

        private static bool IsScreenBackdropGraphicCandidate(GameObject gameObject, Vector2 rootSize)
        {
            if (gameObject == null || !gameObject.activeInHierarchy)
            {
                return false;
            }

            if (HasExplicitlyProtectedPreviewAncestor(gameObject.transform))
            {
                return false;
            }

            var rect = gameObject.GetComponent<RectTransform>();
            if (rect == null)
            {
                return false;
            }

            var size = rect.rect.size;
            var largeEnough = size.x >= rootSize.x * 0.45f && size.y >= rootSize.y * 0.45f;
            var stretched = rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one;
            if (!largeEnough && !stretched)
            {
                return false;
            }

            var name = (gameObject.name ?? string.Empty).ToLowerInvariant();
            if (name.Contains("button") ||
                name.Contains("toggle") ||
                name.Contains("slot") ||
                name.Contains("warning") ||
                name.Contains("header") ||
                name.Contains("label") ||
                name.Contains("input") ||
                name.Contains("scroll"))
            {
                return false;
            }

            return true;
        }

        private static void TryRestoreCameraImageLighting(CameraImage cameraImage)
        {
            if (cameraImage == null)
            {
                return;
            }

            try
            {
                var targetCameraField = typeof(CameraImage).GetField("targetCamera", AnyBinding);
                var targetCamera = targetCameraField?.GetValue(cameraImage) as Camera;
                var restoreMethod = typeof(CameraImage).GetMethod(
                    "RestoreRenderSettings",
                    AnyBinding,
                    binder: null,
                    types: new[] { typeof(Camera) },
                    modifiers: null);
                restoreMethod?.Invoke(cameraImage, new object[] { targetCamera });
            }
            catch
            {
                // Best-effort only; disabling the CameraImage still removes future ambient overrides.
            }
        }

        private static void TryLogPreviewContext(Component screen)
        {
            if (_log == null || Time.unscaledTime - _previewOpenedAt < 0.5f)
            {
                return;
            }

            if (_backdropDiagnosticsLogged && _cameraDiagnosticsLogged)
            {
                return;
            }

            try
            {
                var currentController = EFT.UI.Screens.EftScreenManager.Instance.CurrentBaseScreenController;
                var currentScreenType = currentController?.ScreenType.ToString() ?? "<null>";
                var liveScreenState = screen == null ? "screen=<null>" : $"screenActive={screen.gameObject.activeInHierarchy} path={GetTransformPath(screen.transform)}";
                _log.LogInfo($"[ULE] Preview context: currentScreen={currentScreenType}, {liveScreenState}");
            }
            catch
            {
                // Diagnostics only.
            }
        }

        private static void TryLogCameraImageDiagnostics(Component screen)
        {
            if (_cameraImageDiagnosticsLogged || _log == null || screen == null)
            {
                return;
            }

            _cameraImageDiagnosticsLogged = true;

            var lines = new List<string>(16);
            foreach (var cameraImage in screen.GetComponentsInChildren<CameraImage>(true))
            {
                if (cameraImage == null)
                {
                    continue;
                }

                Camera targetCamera = null;
                try
                {
                    var targetCameraField = typeof(CameraImage).GetField("targetCamera", AnyBinding);
                    targetCamera = targetCameraField?.GetValue(cameraImage) as Camera;
                }
                catch
                {
                    // Diagnostics only.
                }

                var rectTransform = cameraImage.transform as RectTransform;
                var sizeText = rectTransform != null ? rectTransform.rect.size.ToString() : "<no-rect>";
                var targetPath = targetCamera != null ? GetTransformPath(targetCamera.transform) : "<null>";
                lines.Add($"{GetTransformPath(cameraImage.transform)} | enabled={cameraImage.enabled} active={cameraImage.gameObject.activeInHierarchy} size={sizeText} target={targetPath}");
            }

            _log.LogInfo("[ULE] Preset preview camera images:");
            if (lines.Count == 0)
            {
                _log.LogInfo("[ULE]   <none>");
                return;
            }

            foreach (var line in lines)
            {
                _log.LogInfo($"[ULE]   {line}");
            }
        }

        private static void TryLogPreviewPanelDiagnostics(Component screen)
        {
            if (_previewPanelDiagnosticsLogged || _log == null || screen == null)
            {
                return;
            }

            _previewPanelDiagnosticsLogged = true;

            try
            {
                var viewporter = screen.GetComponentInChildren<CameraViewporter>(true);
                var previewHost = viewporter?.transform as RectTransform;
                var previewRoot = previewHost != null ? previewHost.parent as RectTransform ?? previewHost : null;
                var previewCamera = viewporter?.TargetCamera;
                var battleCamera = GetBattleCamera();
                var canvas = viewporter != null ? viewporter.GetComponentInParent<Canvas>() : null;

                _log.LogInfo("[ULE] Preset preview panel diagnostics:");
                _log.LogInfo($"[ULE]   screen={GetTransformPath(screen.transform)}");
                _log.LogInfo($"[ULE]   currentScreen={DescribeCurrentScreenState()}");
                _log.LogInfo($"[ULE]   previewHost={(previewHost != null ? DescribeRectTransform(previewHost) : "<null>")}");
                _log.LogInfo($"[ULE]   previewRoot={(previewRoot != null ? DescribeRectTransform(previewRoot) : "<null>")}");
                _log.LogInfo($"[ULE]   canvas={(canvas != null ? DescribeCanvas(canvas) : "<null>")}");
                _log.LogInfo($"[ULE]   previewCamera={(previewCamera != null ? DescribeCamera(previewCamera) : "<null>")}");
                _log.LogInfo($"[ULE]   battleCameraTag={TryGetCameraTag(battleCamera)}");
                _log.LogInfo($"[ULE]   battleCameraVisualDelta={DescribeBattleCameraVisualDelta(battleCamera)}");
                _log.LogInfo($"[ULE]   renderSettings={DescribeRenderSettings()}");
                _log.LogInfo($"[ULE]   enabledLights={CountEnabledLights()}");
                _log.LogInfo($"[ULE]   sceneLightDiagnostics={DescribeSceneLightDiagnostics(screen)}");
                _log.LogInfo($"[ULE]   weaponPreviewKeyword={TryGetWeaponPreviewKeywordState()}");
                _log.LogInfo($"[ULE]   winterSnowKeyword={TryGetShaderKeywordState("WINTER_SNOW")}");
                _log.LogInfo($"[ULE]   snowGlittersKeyword={TryGetShaderKeywordState("SNOW_GLITTERS")}");
                _log.LogInfo($"[ULE]   wetOffMaskUsed={WetRenderer.WetOffMaskUsed}");
                _log.LogInfo($"[ULE]   snowMask={DescribeSnowMaskState()}");
                _log.LogInfo($"[ULE]   rainController={DescribeRainControllerState()}");
                _log.LogInfo($"[ULE]   snowRenderers={DescribeSnowRendererState()}");
                _log.LogInfo($"[ULE]   snowPreCull={DescribeSnowPreCullState()}");
                _log.LogInfo($"[ULE]   snowBufferRebuild={DescribeSnowCommandBufferRebuildState()}");
                _log.LogInfo($"[ULE]   snowCommandBuffers={DescribeSnowCommandBuffers(battleCamera)}");
                _log.LogInfo($"[ULE]   snowRenderTargets={DescribeSnowRenderTargets()}");
                _log.LogInfo($"[ULE]   snowShaderGlobals={DescribeSnowShaderGlobals()}");
                _log.LogInfo($"[ULE]   terrainSeason={DescribeTerrainSeasonState()}");
                _log.LogInfo($"[ULE]   seasonMaterialFix={DescribeSeasonMaterialsPreviewFixState()}");
                _log.LogInfo($"[ULE]   terrainMaterials={DescribeTerrainMaterialState()}");
                _log.LogInfo($"[ULE]   terrainMaterialProps={DescribeTerrainMaterialPropertyState()}");
                _log.LogInfo($"[ULE]   terrainLod={DescribeTerrainLodState()}");
                _log.LogInfo($"[ULE]   gpuInstancer={DescribeGpuInstancerState()}");
                LogGlobalOverlayDiagnostics(screen);

                if (previewRoot == null)
                {
                    _log.LogInfo("[ULE]   previewVisualTree=<null>");
                    return;
                }

                var lines = new List<string>(64);
                CollectPreviewVisualDiagnostics(previewRoot, depth: 0, lines);

                if (lines.Count == 0)
                {
                    _log.LogInfo("[ULE]   previewVisualTree=<none>");
                    return;
                }

                foreach (var line in lines)
                {
                    _log.LogInfo($"[ULE]   {line}");
                }
            }
            catch (Exception ex)
            {
                _log.LogInfo($"[ULE]   previewPanelDiagnosticsFailed={Unwrap(ex).Message}");
            }
        }

        private static void TryLogDelayedPreviewDiagnostics(Component screen)
        {
            if (_delayedPreviewDiagnosticsLogged || _log == null || Time.unscaledTime - _previewOpenedAt < 3f)
            {
                return;
            }

            _delayedPreviewDiagnosticsLogged = true;

            try
            {
                var battleCamera = GetBattleCamera();
                var viewporter = screen != null ? screen.GetComponentInChildren<CameraViewporter>(true) : null;
                var previewCamera = viewporter?.TargetCamera;
                var screenCanvasGroup = screen?.GetComponent<CanvasGroup>();

                _log.LogInfo("[ULE] Preset preview delayed diagnostics:");
                _log.LogInfo($"[ULE]   elapsed={Time.unscaledTime - _previewOpenedAt:F2}s screen={(screen != null ? GetTransformPath(screen.transform) : "<null>")} active={(screen != null && screen.gameObject.activeInHierarchy)}");
                _log.LogInfo($"[ULE]   currentScreen={DescribeCurrentScreenState()}");
                _log.LogInfo($"[ULE]   screenCanvasGroup={(screenCanvasGroup != null ? DescribeCanvasGroup(screenCanvasGroup) : "<null>")}");
                _log.LogInfo($"[ULE]   battleCamera={(battleCamera != null ? DescribeBattleCamera(battleCamera) : "<null>")}");
                _log.LogInfo($"[ULE]   battleCameraVisualDelta={DescribeBattleCameraVisualDelta(battleCamera)}");
                _log.LogInfo($"[ULE]   previewCamera={(previewCamera != null ? DescribeCamera(previewCamera) : "<null>")}");
                _log.LogInfo($"[ULE]   renderSettings={DescribeRenderSettings()}");
                _log.LogInfo($"[ULE]   sceneLightDiagnostics={DescribeSceneLightDiagnostics(screen)}");
                _log.LogInfo($"[ULE]   snowMask={DescribeSnowMaskState()}");
                _log.LogInfo($"[ULE]   rainController={DescribeRainControllerState()}");
                _log.LogInfo($"[ULE]   snowRenderers={DescribeSnowRendererState()}");
                _log.LogInfo($"[ULE]   snowPreCull={DescribeSnowPreCullState()}");
                _log.LogInfo($"[ULE]   snowBufferRebuild={DescribeSnowCommandBufferRebuildState()}");
                _log.LogInfo($"[ULE]   snowCommandBuffers={DescribeSnowCommandBuffers(battleCamera)}");
                _log.LogInfo($"[ULE]   snowRenderTargets={DescribeSnowRenderTargets()}");
                _log.LogInfo($"[ULE]   snowShaderGlobals={DescribeSnowShaderGlobals()}");
                _log.LogInfo($"[ULE]   terrainSeason={DescribeTerrainSeasonState()}");
                _log.LogInfo($"[ULE]   seasonMaterialFix={DescribeSeasonMaterialsPreviewFixState()}");
                _log.LogInfo($"[ULE]   terrainMaterials={DescribeTerrainMaterialState()}");
                _log.LogInfo($"[ULE]   terrainMaterialProps={DescribeTerrainMaterialPropertyState()}");
                _log.LogInfo($"[ULE]   terrainLod={DescribeTerrainLodState()}");
                _log.LogInfo($"[ULE]   gpuInstancer={DescribeGpuInstancerState()}");
                LogGlobalOverlayDiagnostics(screen);
            }
            catch (Exception ex)
            {
                _log.LogInfo($"[ULE]   delayedPreviewDiagnosticsFailed={Unwrap(ex).Message}");
            }
        }

        private static void CollectPreviewVisualDiagnostics(Transform transform, int depth, List<string> lines)
        {
            if (transform == null || lines == null || lines.Count >= 64 || depth > 6)
            {
                return;
            }

            var rect = transform as RectTransform;
            var componentLines = new List<string>(8);

            foreach (var component in transform.GetComponents<Component>())
            {
                switch (component)
                {
                    case Camera camera:
                        componentLines.Add($"Camera {DescribeCamera(camera)}");
                        break;
                    case CameraImage cameraImage:
                        componentLines.Add($"CameraImage {DescribeCameraImage(cameraImage)}");
                        break;
                    case RawImage rawImage:
                        componentLines.Add($"RawImage {DescribeRawImage(rawImage)}");
                        break;
                    case Image image:
                        componentLines.Add($"Image {DescribeImage(image)}");
                        break;
                    case Canvas canvas:
                        componentLines.Add($"Canvas {DescribeCanvas(canvas)}");
                        break;
                    default:
                    {
                        var typeName = component?.GetType().Name;
                        if (string.Equals(typeName, "CameraViewporter", StringComparison.Ordinal))
                        {
                            componentLines.Add("CameraViewporter");
                        }

                        break;
                    }
                }
            }

            if (componentLines.Count > 0 || depth == 0)
            {
                var indent = new string(' ', depth * 2);
                var rectText = rect != null ? $" rect={FormatRect(rect.rect)}" : string.Empty;
                lines.Add($"{indent}{transform.name}{rectText} active={transform.gameObject.activeInHierarchy} sib={transform.GetSiblingIndex()}");
                foreach (var componentLine in componentLines)
                {
                    lines.Add($"{indent}  - {componentLine}");
                }
            }

            foreach (Transform child in transform)
            {
                if (lines.Count >= 64)
                {
                    break;
                }

                CollectPreviewVisualDiagnostics(child, depth + 1, lines);
            }
        }

        private static string DescribeCameraImage(CameraImage cameraImage)
        {
            if (cameraImage == null)
            {
                return "<null>";
            }

            Camera targetCamera = null;
            try
            {
                var targetCameraField = typeof(CameraImage).GetField("targetCamera", AnyBinding);
                targetCamera = targetCameraField?.GetValue(cameraImage) as Camera;
            }
            catch
            {
                // Diagnostics only.
            }

            var rawImage = cameraImage.GetComponent<RawImage>();
            return $"enabled={cameraImage.enabled} target={(targetCamera != null ? GetTransformPath(targetCamera.transform) : "<null>")} raw={(rawImage != null ? DescribeRawImage(rawImage) : "<null>")}";
        }

        private static string DescribeRawImage(RawImage rawImage)
        {
            if (rawImage == null)
            {
                return "<null>";
            }

            var rect = rawImage.transform as RectTransform;
            return $"enabled={rawImage.enabled} rect={(rect != null ? FormatRect(rect.rect) : "<no-rect>")} tex={DescribeTexture(rawImage.texture)} mat={DescribeMaterial(rawImage.material)} uv={FormatRect(rawImage.uvRect)} color={FormatColor(rawImage.color)} raycast={rawImage.raycastTarget}";
        }

        private static string DescribeImage(Image image)
        {
            if (image == null)
            {
                return "<null>";
            }

            var rect = image.transform as RectTransform;
            var spriteName = image.sprite != null ? image.sprite.name : "<null>";
            return $"enabled={image.enabled} rect={(rect != null ? FormatRect(rect.rect) : "<no-rect>")} sprite={spriteName} mat={DescribeMaterial(image.material)} color={FormatColor(image.color)} raycast={image.raycastTarget}";
        }

        private static string DescribeCanvas(Canvas canvas)
        {
            if (canvas == null)
            {
                return "<null>";
            }

            var worldCameraPath = canvas.worldCamera != null ? GetTransformPath(canvas.worldCamera.transform) : "<null>";
            return $"mode={canvas.renderMode} scaleFactor={canvas.scaleFactor:F2} pixelPerfect={canvas.pixelPerfect} sortingLayer={canvas.sortingLayerName}:{canvas.sortingOrder} overrideSorting={canvas.overrideSorting} worldCamera={worldCameraPath}";
        }

        private static string DescribeCamera(Camera camera)
        {
            if (camera == null)
            {
                return "<null>";
            }

            return $"{GetTransformPath(camera.transform)} enabled={camera.enabled} clear={camera.clearFlags} depth={camera.depth:F1} rect={FormatRect(camera.rect)} cull=0x{camera.cullingMask:X8} targetTex={DescribeTexture(camera.targetTexture)} rendering={camera.renderingPath}";
        }

        private static string DescribeBattleCamera(Camera camera)
        {
            if (camera == null)
            {
                return "<null>";
            }

            var transform = camera.transform;
            return $"{DescribeCamera(camera)} rot=({transform.rotation.eulerAngles.x:F2},{transform.rotation.eulerAngles.y:F2},{transform.rotation.eulerAngles.z:F2}) localRot=({transform.localRotation.eulerAngles.x:F2},{transform.localRotation.eulerAngles.y:F2},{transform.localRotation.eulerAngles.z:F2})";
        }

        private static string DescribeBattleCameraVisualState(Camera camera)
        {
            if (camera == null)
            {
                return "<null>";
            }

            var actualRenderingPath = "<unavailable>";
            try
            {
                actualRenderingPath = camera.actualRenderingPath.ToString();
            }
            catch
            {
                // Some Unity camera implementations can throw when queried during transitions.
            }

            return $"{DescribeBattleCamera(camera)} actualRendering={actualRenderingPath} depthMode={camera.depthTextureMode} hdr={camera.allowHDR} msaa={camera.allowMSAA} occlusion={camera.useOcclusionCulling} near={camera.nearClipPlane:F2} far={camera.farClipPlane:F1} fov={camera.fieldOfView:F1}";
        }

        private static string DescribePreviewRelevantShaderKeywords()
        {
            var keywords = new[]
            {
                "WeaponPreview",
                "WINTER_SNOW",
                "SNOW_GLITTERS"
            };

            return string.Join(",", keywords.Select(keyword => $"{keyword}={TryGetShaderKeywordState(keyword)}"));
        }

        private static string DescribeCameraCommandBufferSummary(Camera camera)
        {
            if (camera == null)
            {
                return "<no-camera>";
            }

            try
            {
                var totalCount = 0;
                var parts = new List<string>(16);

                foreach (CameraEvent cameraEvent in Enum.GetValues(typeof(CameraEvent)))
                {
                    var buffers = camera.GetCommandBuffers(cameraEvent);
                    if (buffers == null || buffers.Length == 0)
                    {
                        continue;
                    }

                    totalCount += buffers.Length;
                    var names = buffers
                        .Where(buffer => buffer != null)
                        .Select(buffer => string.IsNullOrEmpty(buffer.name) ? $"<unnamed>:{buffer.sizeInBytes}b" : $"{buffer.name}:{buffer.sizeInBytes}b")
                        .Take(8)
                        .ToArray();

                    parts.Add($"{cameraEvent}:{string.Join("|", names)}");
                }

                return parts.Count == 0
                    ? $"<none> total={totalCount}"
                    : $"{string.Join("; ", parts)} total={totalCount}";
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static string DescribeBattleCameraVisualDelta(Camera camera)
        {
            if (camera == null)
            {
                return "<no-camera>";
            }

            try
            {
                var currentState = DescribeBattleCameraVisualState(camera);
                var currentKeywords = DescribePreviewRelevantShaderKeywords();
                var currentCommandBuffers = DescribeCameraCommandBufferSummary(camera);
                var cameraChanged = !string.Equals(_capturedBattleCameraVisualState, currentState, StringComparison.Ordinal);
                var keywordsChanged = !string.Equals(_capturedBattleCameraShaderKeywords, currentKeywords, StringComparison.Ordinal);
                var commandBuffersChanged = !string.Equals(_capturedBattleCameraCommandBuffers, currentCommandBuffers, StringComparison.Ordinal);
                var behaviourChanges = new List<string>(16);
                var seen = new HashSet<int>();

                foreach (var behaviour in camera.GetComponents<Behaviour>())
                {
                    if (behaviour == null || behaviour is Camera)
                    {
                        continue;
                    }

                    var instanceId = behaviour.GetInstanceID();
                    seen.Add(instanceId);

                    if (!CapturedBattleCameraVisualBehaviours.TryGetValue(instanceId, out var snapshot))
                    {
                        behaviourChanges.Add($"+{behaviour.GetType().Name}={behaviour.enabled}");
                        continue;
                    }

                    if (snapshot.Enabled != behaviour.enabled)
                    {
                        behaviourChanges.Add($"{behaviour.GetType().Name}:{snapshot.Enabled}->{behaviour.enabled}");
                    }
                }

                foreach (var entry in CapturedBattleCameraVisualBehaviours)
                {
                    if (!seen.Contains(entry.Key))
                    {
                        behaviourChanges.Add($"-{entry.Value.TypeName}");
                    }
                }

                var behaviourText = behaviourChanges.Count == 0
                    ? "<none>"
                    : string.Join("|", behaviourChanges.Take(24));

                if (behaviourChanges.Count > 24)
                {
                    behaviourText += $"|+{behaviourChanges.Count - 24} more";
                }

                return $"cameraChanged={cameraChanged} beforeCamera={_capturedBattleCameraVisualState ?? "<none>"} currentCamera={currentState} keywordsChanged={keywordsChanged} beforeKeywords={_capturedBattleCameraShaderKeywords ?? "<none>"} currentKeywords={currentKeywords} commandBuffersChanged={commandBuffersChanged} beforeBuffers={_capturedBattleCameraCommandBuffers ?? "<none>"} currentBuffers={currentCommandBuffers} behaviourChanges={behaviourText}";
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static string DescribeTexture(Texture texture)
        {
            if (texture == null)
            {
                return "<null>";
            }

            return $"{texture.name}({texture.width}x{texture.height})";
        }

        private static string DescribeMaterial(Material material)
        {
            if (material == null)
            {
                return "<null>";
            }

            try
            {
                var shaderName = material.shader != null ? material.shader.name : "<null>";
                var keywords = material.shaderKeywords;
                var keywordText = keywords == null || keywords.Length == 0
                    ? "<none>"
                    : string.Join(",", keywords.Take(8));
                return $"{material.name}|shader={shaderName}|kw={keywordText}|main={DescribeTexture(material.mainTexture)}";
            }
            catch (Exception ex)
            {
                return $"{material.name}|error={Unwrap(ex).Message}";
            }
        }

        private static string DescribeRectTransform(RectTransform rectTransform)
        {
            if (rectTransform == null)
            {
                return "<null>";
            }

            return $"{GetTransformPath(rectTransform)} rect={FormatRect(rectTransform.rect)} anchorMin={rectTransform.anchorMin} anchorMax={rectTransform.anchorMax} pivot={rectTransform.pivot}";
        }

        private static string DescribeRenderSettings()
        {
            var skybox = RenderSettings.skybox != null ? RenderSettings.skybox.name : "<null>";
            return $"ambientMode={RenderSettings.ambientMode} intensity={RenderSettings.ambientIntensity:F2} ambientLight={FormatColor(RenderSettings.ambientLight)} sky={FormatColor(RenderSettings.ambientSkyColor)} equator={FormatColor(RenderSettings.ambientEquatorColor)} ground={FormatColor(RenderSettings.ambientGroundColor)} fog={RenderSettings.fog} fogMode={RenderSettings.fogMode} fogColor={FormatColor(RenderSettings.fogColor)} fogDensity={RenderSettings.fogDensity:F4} reflectionIntensity={RenderSettings.reflectionIntensity:F2} skybox={skybox}";
        }

        private static string CountEnabledLights()
        {
            try
            {
                var allLights = Resources.FindObjectsOfTypeAll<Light>();
                var activeLights = allLights.Count(light => light != null && light.gameObject.activeInHierarchy && light.enabled);
                return $"{activeLights}/{allLights.Length}";
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static string DescribeSceneLightDiagnostics(Component screen)
        {
            try
            {
                var allLights = Resources.FindObjectsOfTypeAll<Light>();
                var battleCamera = GetBattleCamera();
                var battleMask = battleCamera != null ? battleCamera.cullingMask : 0;
                var previewMask = 1 << LayersMaskController.WeaponPreview;
                var activeEnabled = 0;
                var activeEnabledBattleMasked = 0;
                var activeEnabledPreviewOnly = 0;

                foreach (var light in allLights)
                {
                    if (light == null || !light.gameObject.activeInHierarchy || !light.enabled)
                    {
                        continue;
                    }

                    activeEnabled++;
                    if (battleCamera != null && (light.cullingMask & battleMask) != 0)
                    {
                        activeEnabledBattleMasked++;
                    }

                    if (light.cullingMask == previewMask)
                    {
                        activeEnabledPreviewOnly++;
                    }
                }

                var capturedAlive = 0;
                var capturedNowEnabled = 0;
                var capturedDisabled = 0;
                var capturedBattleMasked = 0;
                var capturedPreviewOnly = 0;
                var changedMasks = 0;
                var changedIntensity = 0;
                var changedColor = 0;
                var changedShadows = 0;
                var changedSamples = new List<string>(4);

                foreach (var snapshot in CapturedSceneLightDiagnostics)
                {
                    var light = snapshot.Light;
                    if (light == null)
                    {
                        continue;
                    }

                    capturedAlive++;
                    if (light.enabled)
                    {
                        capturedNowEnabled++;
                    }
                    else
                    {
                        capturedDisabled++;
                    }

                    if (battleCamera != null && (light.cullingMask & battleMask) != 0)
                    {
                        capturedBattleMasked++;
                    }

                    if (light.cullingMask == previewMask)
                    {
                        capturedPreviewOnly++;
                    }

                    var maskChanged = light.cullingMask != snapshot.CullingMask;
                    var intensityChanged = !Approximately(light.intensity, snapshot.Intensity);
                    var colorChanged = !Approximately(light.color, snapshot.Color);
                    var shadowsChanged = light.shadows != snapshot.Shadows;

                    if (maskChanged)
                    {
                        changedMasks++;
                    }

                    if (intensityChanged)
                    {
                        changedIntensity++;
                    }

                    if (colorChanged)
                    {
                        changedColor++;
                    }

                    if (shadowsChanged)
                    {
                        changedShadows++;
                    }

                    if (changedSamples.Count < 4 && (maskChanged || intensityChanged || colorChanged || shadowsChanged || !light.enabled))
                    {
                        changedSamples.Add(snapshot.DescribeCurrentState());
                    }
                }

                var screenLightState = DescribeItemObserveScreenLights(screen, battleMask, battleCamera != null);
                var sampleText = changedSamples.Count > 0 ? $" samples=[{string.Join(" | ", changedSamples)}]" : string.Empty;
                return $"all={allLights.Length} activeEnabled={activeEnabled} activeBattleMasked={activeEnabledBattleMasked} activePreviewOnly={activeEnabledPreviewOnly} captured={CapturedSceneLightDiagnostics.Count} capturedAlive={capturedAlive} capturedEnabledNow={capturedNowEnabled} capturedDisabled={capturedDisabled} capturedBattleMasked={capturedBattleMasked} capturedPreviewOnly={capturedPreviewOnly} changedMasks={changedMasks} changedIntensity={changedIntensity} changedColor={changedColor} changedShadows={changedShadows} screenLight_0={screenLightState}{sampleText}";
            }
            catch (Exception ex)
            {
                return $"<unavailable:{Unwrap(ex).Message}>";
            }
        }

        private static string DescribeItemObserveScreenLights(Component screen, int battleMask, bool hasBattleCamera)
        {
            if (screen == null)
            {
                return "<null-screen>";
            }

            try
            {
                var lightField = screen.GetType().BaseType?.GetField("light_0", AnyBinding);
                if (!(lightField?.GetValue(screen) is IEnumerable lights))
                {
                    return "<none>";
                }

                var total = 0;
                var enabled = 0;
                var activeEnabled = 0;
                var activeBattleMasked = 0;
                foreach (var lightObject in lights)
                {
                    if (!(lightObject is Light light) || light == null)
                    {
                        continue;
                    }

                    total++;
                    if (light.enabled)
                    {
                        enabled++;
                    }

                    if (light.enabled && light.gameObject.activeInHierarchy)
                    {
                        activeEnabled++;
                        if (hasBattleCamera && (light.cullingMask & battleMask) != 0)
                        {
                            activeBattleMasked++;
                        }
                    }
                }

                return $"total={total},enabled={enabled},activeEnabled={activeEnabled},activeBattleMasked={activeBattleMasked}";
            }
            catch (Exception ex)
            {
                return $"<unavailable:{Unwrap(ex).Message}>";
            }
        }

        private static void LogGlobalOverlayDiagnostics(Component screen)
        {
            if (_log == null)
            {
                return;
            }

            try
            {
                _log.LogInfo("[ULE] Preset preview global overlay diagnostics:");

                var environmentLines = CollectEnvironmentShadingDiagnostics();
                if (environmentLines.Count == 0)
                {
                    _log.LogInfo("[ULE]   environmentShading=<none>");
                }
                else
                {
                    foreach (var line in environmentLines)
                    {
                        _log.LogInfo($"[ULE]   environmentShading={line}");
                    }
                }

                var overlayLines = CollectOverlayCandidateDiagnostics(screen);
                if (overlayLines.Count == 0)
                {
                    _log.LogInfo("[ULE]   overlayCandidates=<none>");
                    return;
                }

                foreach (var line in overlayLines)
                {
                    _log.LogInfo($"[ULE]   overlayCandidate={line}");
                }
            }
            catch (Exception ex)
            {
                _log.LogInfo($"[ULE]   globalOverlayDiagnosticsFailed={Unwrap(ex).Message}");
            }
        }

        private static List<string> CollectEnvironmentShadingDiagnostics()
        {
            var lines = new List<string>(8);
            foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour == null)
                {
                    continue;
                }

                var typeName = behaviour.GetType().Name;
                if (string.Equals(typeName, "EnvironmentUI", StringComparison.Ordinal))
                {
                    lines.Add($"{GetTransformPath(behaviour.transform)} active={behaviour.gameObject.activeInHierarchy} enabled={behaviour.enabled} shading={DescribeEnvironmentShading(GetFieldValue(behaviour, "_environmentShading"))}");
                }
                else if (string.Equals(typeName, "EnvironmentShading", StringComparison.Ordinal))
                {
                    lines.Add(DescribeEnvironmentShading(behaviour));
                }

                if (lines.Count >= 8)
                {
                    break;
                }
            }

            return lines;
        }

        private static string DescribeEnvironmentShading(object shading)
        {
            if (!(shading is Component component))
            {
                return "<null>";
            }

            var visible = GetFieldValue(component, "_currentVisibility");
            var currentGroup = GetFieldValue(component, "_currentShading") as CanvasGroup;
            var shadings = GetFieldValue(component, "_environmentShadings");
            var shadingEntries = DescribeEnvironmentShadingEntries(shadings);
            return $"{GetTransformPath(component.transform)} active={component.gameObject.activeInHierarchy} visible={visible ?? "<unknown>"} currentGroup={(currentGroup != null ? DescribeCanvasGroup(currentGroup) : "<null>")} entries={shadingEntries}";
        }

        private static string DescribeEnvironmentShadingEntries(object shadings)
        {
            if (shadings == null)
            {
                return "<null>";
            }

            var entries = new List<string>(4);
            if (shadings is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Value is CanvasGroup canvasGroup)
                    {
                        entries.Add($"{entry.Key}:{DescribeCanvasGroup(canvasGroup)}");
                    }

                    if (entries.Count >= 4)
                    {
                        break;
                    }
                }
            }
            else if (shadings is IEnumerable enumerable)
            {
                foreach (var entry in enumerable)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    var key = entry.GetType().GetProperty("Key")?.GetValue(entry, null);
                    var value = entry.GetType().GetProperty("Value")?.GetValue(entry, null) as CanvasGroup;
                    if (value != null)
                    {
                        entries.Add($"{key}:{DescribeCanvasGroup(value)}");
                    }

                    if (entries.Count >= 4)
                    {
                        break;
                    }
                }
            }

            return entries.Count > 0 ? string.Join("; ", entries.ToArray()) : "<empty>";
        }

        private static List<string> CollectOverlayCandidateDiagnostics(Component screen)
        {
            var rootSize = new Vector2(Screen.width, Screen.height);
            var candidates = new List<Tuple<int, string>>(32);
            var screenTransform = screen != null ? screen.transform : null;

            foreach (var canvasGroup in Resources.FindObjectsOfTypeAll<CanvasGroup>())
            {
                if (canvasGroup == null || !canvasGroup.gameObject.activeInHierarchy || canvasGroup.alpha <= 0.01f)
                {
                    continue;
                }

                var rect = canvasGroup.transform as RectTransform;
                var path = GetTransformPath(canvasGroup.transform);
                if (!LooksLikeOverlayDiagnosticCandidate(canvasGroup.gameObject, rect, rootSize, path))
                {
                    continue;
                }

                var insideScreen = screenTransform != null && canvasGroup.transform.IsChildOf(screenTransform);
                var priority = GetOverlayDiagnosticPriority(path, rect, rootSize, insideScreen, canvasGroup.alpha);
                candidates.Add(Tuple.Create(priority, $"CanvasGroup {DescribeCanvasGroup(canvasGroup)} insideWeaponScreen={insideScreen}"));
            }

            AddGraphicOverlayCandidates<Image>(candidates, screenTransform, rootSize, "Image");
            AddGraphicOverlayCandidates<RawImage>(candidates, screenTransform, rootSize, "RawImage");

            return candidates
                .OrderByDescending(candidate => candidate.Item1)
                .ThenBy(candidate => candidate.Item2)
                .Take(20)
                .Select(candidate => candidate.Item2)
                .ToList();
        }

        private static void AddGraphicOverlayCandidates<TGraphic>(
            List<Tuple<int, string>> candidates,
            Transform screenTransform,
            Vector2 rootSize,
            string label)
            where TGraphic : Graphic
        {
            foreach (var graphic in Resources.FindObjectsOfTypeAll<TGraphic>())
            {
                if (graphic == null || !graphic.gameObject.activeInHierarchy || !graphic.enabled || graphic.color.a <= 0.01f)
                {
                    continue;
                }

                var rect = graphic.transform as RectTransform;
                var path = GetTransformPath(graphic.transform);
                if (!LooksLikeOverlayDiagnosticCandidate(graphic.gameObject, rect, rootSize, path))
                {
                    continue;
                }

                var insideScreen = screenTransform != null && graphic.transform.IsChildOf(screenTransform);
                var priority = GetOverlayDiagnosticPriority(path, rect, rootSize, insideScreen, graphic.color.a);
                candidates.Add(Tuple.Create(priority, $"{label} {DescribeGraphic(graphic)} insideWeaponScreen={insideScreen}"));
            }
        }

        private static bool LooksLikeOverlayDiagnosticCandidate(GameObject gameObject, RectTransform rect, Vector2 rootSize, string path)
        {
            if (gameObject == null || rect == null)
            {
                return false;
            }

            var lowerPath = (path ?? string.Empty).ToLowerInvariant();
            var namedOverlay = BackgroundNameTokens.Any(lowerPath.Contains) ||
                               lowerPath.Contains("fade") ||
                               lowerPath.Contains("substrate") ||
                               lowerPath.Contains("shade");
            var size = rect.rect.size;
            var largeEnough = size.x >= rootSize.x * 0.35f && size.y >= rootSize.y * 0.35f;
            var stretched = rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one;
            return namedOverlay || largeEnough || stretched;
        }

        private static int GetOverlayDiagnosticPriority(string path, RectTransform rect, Vector2 rootSize, bool insideScreen, float alpha)
        {
            var lowerPath = (path ?? string.Empty).ToLowerInvariant();
            var priority = insideScreen ? 0 : 100;
            if (lowerPath.Contains("environment") || lowerPath.Contains("shading") || lowerPath.Contains("overlay"))
            {
                priority += 50;
            }

            if (rect != null)
            {
                var size = rect.rect.size;
                if ((rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one) ||
                    (size.x >= rootSize.x * 0.5f && size.y >= rootSize.y * 0.5f))
                {
                    priority += 25;
                }
            }

            priority += Mathf.RoundToInt(Mathf.Clamp01(alpha) * 10f);
            return priority;
        }

        private static string DescribeCanvasGroup(CanvasGroup canvasGroup)
        {
            if (canvasGroup == null)
            {
                return "<null>";
            }

            var rect = canvasGroup.transform as RectTransform;
            var canvas = canvasGroup.GetComponentInParent<Canvas>();
            return $"{GetTransformPath(canvasGroup.transform)} active={canvasGroup.gameObject.activeInHierarchy} enabled={canvasGroup.enabled} alpha={canvasGroup.alpha:F3} interactable={canvasGroup.interactable} blocksRaycasts={canvasGroup.blocksRaycasts} ignoreParentGroups={canvasGroup.ignoreParentGroups} rect={(rect != null ? FormatRect(rect.rect) : "<no-rect>")} canvas={(canvas != null ? DescribeCanvas(canvas) : "<null>")}";
        }

        private static string DescribeGraphic(Graphic graphic)
        {
            if (graphic == null)
            {
                return "<null>";
            }

            var rect = graphic.transform as RectTransform;
            var canvas = graphic.canvas;
            return $"{GetTransformPath(graphic.transform)} active={graphic.gameObject.activeInHierarchy} enabled={graphic.enabled} color={FormatColor(graphic.color)} rect={(rect != null ? FormatRect(rect.rect) : "<no-rect>")} mat={DescribeMaterial(graphic.material)} canvas={(canvas != null ? DescribeCanvas(canvas) : "<null>")}";
        }

        private static object GetFieldValue(object instance, string fieldName)
        {
            if (instance == null || string.IsNullOrEmpty(fieldName))
            {
                return null;
            }

            try
            {
                foreach (var candidate in GetFieldNameCandidates(fieldName))
                {
                    var field = FindField(instance.GetType(), candidate);
                    if (field != null)
                    {
                        return field.GetValue(instance);
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool SetFieldValue(object instance, string fieldName, object value)
        {
            if (instance == null || string.IsNullOrEmpty(fieldName))
            {
                return false;
            }

            try
            {
                foreach (var candidate in GetFieldNameCandidates(fieldName))
                {
                    var field = FindField(instance.GetType(), candidate);
                    if (field == null)
                    {
                        continue;
                    }

                    field.SetValue(instance, value);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            while (type != null)
            {
                var field = type.GetField(fieldName, AnyBinding);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            return null;
        }

        private static object GetPropertyValue(object instance, string propertyName)
        {
            if (instance == null || string.IsNullOrEmpty(propertyName))
            {
                return null;
            }

            try
            {
                var type = instance.GetType();
                while (type != null)
                {
                    var property = type.GetProperty(propertyName, AnyBinding);
                    if (property != null)
                    {
                        return property.GetValue(instance, null);
                    }

                    type = type.BaseType;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static IEnumerable<string> GetFieldNameCandidates(string fieldName)
        {
            yield return fieldName;

        }

        private static bool TryInvoke(object instance, string methodName, params object[] arguments)
        {
            if (instance == null || string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            try
            {
                var method = instance.GetType()
                    .GetMethods(AnyBinding)
                    .FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, methodName, StringComparison.Ordinal) &&
                        candidate.GetParameters().Length == (arguments?.Length ?? 0));
                if (method == null)
                {
                    return false;
                }

                method.Invoke(instance, arguments);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string TryGetWeaponPreviewKeywordState()
        {
            try
            {
                return Shader.IsKeywordEnabled("WeaponPreview").ToString();
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static string TryGetShaderKeywordState(string keyword)
        {
            try
            {
                return Shader.IsKeywordEnabled(keyword).ToString();
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static string DescribeSnowMaskState()
        {
            try
            {
                var lastRequest = _lastPreviewSnowMaskDisableRequest.HasValue ? _lastPreviewSnowMaskDisableRequest.Value.ToString() : "<none>";
                return $"requests={_previewSnowMaskDisableRequestCount} lastRequest={lastRequest} forces={_snowPresentationForceCount}";
            }
            catch
            {
                return $"requests={_previewSnowMaskDisableRequestCount} lastRequest={(_lastPreviewSnowMaskDisableRequest.HasValue ? _lastPreviewSnowMaskDisableRequest.Value.ToString() : "<none>")} forces={_snowPresentationForceCount}";
            }
        }

        private static string DescribeRainControllerState()
        {
            try
            {
                var captured = _rainStateCaptured
                    ? $" capturedWetting={_capturedRainWetting:F3} capturedOpaqueness={_capturedRainOpaqueness:F3} capturedIntensity={_capturedRainIntensity:F3}"
                    : " captured=<none>";
                var controllerState = "<unavailable>";

                try
                {
                    var controller = Seasons.Controller;
                    if (controller != null)
                    {
                        controllerState = $"{controller.Season}/{controller.Status} snowLevel={controller.SnowLevelOnTerrain:F3}";
                    }
                }
                catch
                {
                    controllerState = "<error>";
                }

                return $"wetting={RainController.Wetting:F3} opaqueness={RainController.Opaqueness:F3} intensity={RainController.Intensity:F3} underRain={RainController.IsCameraUnderRain} controller={controllerState}{captured}";
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static string DescribeSnowRendererState()
        {
            try
            {
                var renderers = UnityEngine.Object.FindObjectsOfType<SnowWetRenderer>();
                var enabledCount = renderers.Count(renderer => renderer != null && renderer.enabled);
                var activeEnabledCount = renderers.Count(renderer => renderer != null && renderer.isActiveAndEnabled);
                var winterVisibleCount = renderers.Count(renderer => renderer != null && renderer.WinterShow);
                var liveRenderers = renderers.Where(renderer => renderer != null).ToArray();
                var wettingText = liveRenderers.Length == 0
                    ? "<none>"
                    : $"{liveRenderers.Min(renderer => renderer.Wetting):F2}-{liveRenderers.Max(renderer => renderer.Wetting):F2}";
                var opaquenessText = liveRenderers.Length == 0
                    ? "<none>"
                    : $"{liveRenderers.Min(renderer => renderer.Opaqueness):F2}-{liveRenderers.Max(renderer => renderer.Opaqueness):F2}";
                var capturedAliveCount = CapturedSnowRenderers.Count(snapshot => snapshot.Renderer != null);
                var capturedActiveCount = CapturedSnowRenderers.Count(snapshot => snapshot.Renderer != null && snapshot.Renderer.isActiveAndEnabled);
                return $"enabled={enabledCount}/{renderers.Length} activeEnabled={activeEnabledCount}/{renderers.Length} winterVisible={winterVisibleCount}/{renderers.Length} wetting={wettingText} opaqueness={opaquenessText} captured={CapturedSnowRenderers.Count} capturedAlive={capturedAliveCount} capturedActive={capturedActiveCount}";
            }
            catch
            {
                return $"captured={CapturedSnowRenderers.Count}";
            }
        }

        private static string DescribeSnowPreCullState()
        {
            var cameraPath = string.IsNullOrEmpty(_lastPreviewSnowPreCullCameraPath) ? "<none>" : _lastPreviewSnowPreCullCameraPath;
            var cameraTag = string.IsNullOrEmpty(_lastPreviewSnowPreCullCameraTag) ? "<none>" : _lastPreviewSnowPreCullCameraTag;
            var overridePath = string.IsNullOrEmpty(_lastSnowCommandBufferCameraOverridePath) ? "<none>" : _lastSnowCommandBufferCameraOverridePath;
            return $"events={_previewSnowPreCullEventCount} accepted={_previewSnowPreCullAcceptedCameraCount} main={_previewSnowPreCullAcceptedMainCameraCount} optic={_previewSnowPreCullAcceptedOpticCameraCount} capturedBattle={_previewSnowPreCullCapturedBattleCameraCount} looksBattle={_previewSnowPreCullLooksBattleCameraCount} preview={_previewSnowPreCullPreviewCameraCount} ui={_previewSnowPreCullUiCameraCount} other={_previewSnowPreCullOtherCameraCount} cmdOverride={_snowCommandBufferCameraOverrideCount} lastOverride={overridePath} cameraClass={DescribeSnowCameraManagerState()} lastCamera={cameraPath} tag={cameraTag} treatedAsBattle={_lastPreviewSnowPreCullTreatedAsBattleCamera}";
        }

        private static string DescribeSnowCommandBufferRebuildState()
        {
            return string.IsNullOrEmpty(_lastSnowCommandBufferRebuildState)
                ? $"attempted=false total={_snowCommandBufferRebuildCount}"
                : _lastSnowCommandBufferRebuildState;
        }

        private static string DescribeSnowCameraManagerState()
        {
            try
            {
                var cameraManager = EFT.CameraControl.CameraManager.Instance;
                var mainCamera = cameraManager?.Camera;
                var opticCamera = cameraManager?.OpticCameraManager?.Camera;
                return $"main={(mainCamera != null ? GetTransformPath(mainCamera.transform) : "<null>")} optic={(opticCamera != null ? GetTransformPath(opticCamera.transform) : "<null>")}";
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static string DescribeSnowCommandBuffers(Camera camera)
        {
            if (camera == null)
            {
                return "<no-camera>";
            }

            try
            {
                var totalCount = 0;
                var parts = new List<string>(8);

                foreach (CameraEvent cameraEvent in Enum.GetValues(typeof(CameraEvent)))
                {
                    var buffers = camera.GetCommandBuffers(cameraEvent);
                    if (buffers == null || buffers.Length == 0)
                    {
                        continue;
                    }

                    totalCount += buffers.Length;
                    var names = buffers
                        .Where(buffer => buffer != null && IsSnowCommandBufferName(buffer.name))
                        .Select(buffer => string.IsNullOrEmpty(buffer.name) ? $"<unnamed>:{buffer.sizeInBytes}b" : $"{buffer.name}:{buffer.sizeInBytes}b")
                        .ToArray();

                    if (names.Length > 0)
                    {
                        parts.Add($"{cameraEvent}:{string.Join("|", names)}");
                    }
                }

                return parts.Count == 0
                    ? $"<none> total={totalCount}"
                    : $"{string.Join("; ", parts)} total={totalCount}";
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static bool IsSnowCommandBufferName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return name.IndexOf("snow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("swamp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("wet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("mask", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("gbuffer", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DescribeSnowRenderTargets()
        {
            try
            {
                var snowRendererManager = SnowRenderer.Instance;
                var entries = GetFieldValue(snowRendererManager, "_data") as IList;
                if (entries == null || entries.Count == 0)
                {
                    return "<none>";
                }

                var parts = new List<string>(entries.Count);
                foreach (var entry in entries)
                {
                    if (entry == null)
                    {
                        parts.Add("<null>");
                        continue;
                    }

                    var camera = GetPropertyValue(entry, "Camera") as Camera;
                    var data = GetPropertyValue(entry, "RendererData");
                    var owners = GetPropertyValue(entry, "Owners") as IList;
                    parts.Add($"camera={(camera != null ? GetTransformPath(camera.transform) : "<null>")} hdr={(camera != null ? camera.allowHDR.ToString() : "<null>")} owners={owners?.Count ?? 0} shadow={DescribeTexture(GetFieldValue(data, "ShadowMap") as Texture)} disableMask={DescribeTexture(GetFieldValue(data, "DisableSnowMask") as Texture)}");
                }

                return string.Join("; ", parts);
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static string DescribeSnowShaderGlobals()
        {
            try
            {
                var parts = new List<string>(
                    SnowShaderFloatGlobalNames.Length +
                    SnowShaderVectorGlobalNames.Length +
                    SnowShaderColorGlobalNames.Length +
                    SnowShaderTextureGlobalNames.Length);

                foreach (var name in SnowShaderFloatGlobalNames)
                {
                    parts.Add($"{name}={Shader.GetGlobalFloat(Shader.PropertyToID(name)):F3}");
                }

                foreach (var name in SnowShaderVectorGlobalNames)
                {
                    parts.Add($"{name}={FormatVector4(Shader.GetGlobalVector(Shader.PropertyToID(name)))}");
                }

                foreach (var name in SnowShaderColorGlobalNames)
                {
                    parts.Add($"{name}={FormatColor(Shader.GetGlobalColor(Shader.PropertyToID(name)))}");
                }

                foreach (var name in SnowShaderTextureGlobalNames)
                {
                    parts.Add($"{name}={DescribeTexture(Shader.GetGlobalTexture(Shader.PropertyToID(name)))}");
                }

                return string.Join(" ", parts);
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static string DescribeTerrainSeasonState()
        {
            return string.IsNullOrEmpty(_lastTerrainSeasonState)
                ? $"<not checked> forced={_terrainSeasonForceCount}"
                : _lastTerrainSeasonState;
        }

        private static string DescribeSeasonMaterialsPreviewFixState()
        {
            return string.IsNullOrEmpty(_lastSeasonMaterialsPreviewState)
                ? $"disabled started={_seasonMaterialsPreviewLoadStarted} applied={_seasonMaterialsPreviewFixApplied} fixes={_seasonMaterialsPreviewFixCount}"
                : _lastSeasonMaterialsPreviewState;
        }

        private static void CaptureTerrainMaterialPropertiesBeforePreview()
        {
            CapturedTerrainMaterialPropertyStates.Clear();
            _lastTerrainMaterialPropertySnapshotState = null;

            try
            {
                foreach (var pair in CollectTerrainMaterialDiagnostics(16))
                {
                    CapturedTerrainMaterialPropertyStates[pair.Key] = DescribeTerrainMaterialProperties(pair.Value);
                }

                _lastTerrainMaterialPropertySnapshotState = $"captured={CapturedTerrainMaterialPropertyStates.Count}";
            }
            catch (Exception ex)
            {
                _lastTerrainMaterialPropertySnapshotState = $"captureError={Unwrap(ex).Message}";
            }
        }

        private static string DescribeTerrainMaterialPropertyState()
        {
            try
            {
                var samples = CollectTerrainMaterialDiagnostics(10);
                if (samples.Count == 0)
                {
                    return $"{_lastTerrainMaterialPropertySnapshotState ?? "captured=0"} current=0";
                }

                var parts = new List<string>(samples.Count);
                foreach (var pair in samples)
                {
                    var current = DescribeTerrainMaterialProperties(pair.Value);
                    var comparison = "uncaptured";
                    if (CapturedTerrainMaterialPropertyStates.TryGetValue(pair.Key, out var captured))
                    {
                        comparison = string.Equals(captured, current, StringComparison.Ordinal)
                            ? "same"
                            : $"changed before={Abbreviate(captured, 360)}";
                    }

                    parts.Add($"{pair.Key} {comparison} current={Abbreviate(current, 520)}");
                }

                return $"{_lastTerrainMaterialPropertySnapshotState ?? $"captured={CapturedTerrainMaterialPropertyStates.Count}"} current={samples.Count} samples=[{string.Join(" || ", parts)}]";
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static List<KeyValuePair<string, Material>> CollectTerrainMaterialDiagnostics(int maxSamples)
        {
            var samples = new List<KeyValuePair<string, Material>>(Mathf.Max(1, maxSamples));
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                var terrainType = Type.GetType("UnityEngine.Terrain, UnityEngine.TerrainModule", throwOnError: false);
                if (terrainType != null)
                {
                    foreach (var terrain in UnityEngine.Object.FindObjectsOfType(terrainType).Take(3))
                    {
                        AddTerrainMaterialDiagnosticSample(
                            samples,
                            seenKeys,
                            $"{DescribeObjectPath(terrain)}|Terrain.materialTemplate",
                            GetMemberValue(terrain, "materialTemplate") as Material,
                            maxSamples);
                    }
                }
            }
            catch
            {
                // Diagnostics only.
            }

            try
            {
                var terrainType = Type.GetType("JBooth.MicroSplat.MicroSplatTerrain, JBooth.MicroSplat.Core", throwOnError: false);
                if (terrainType == null)
                {
                    return samples;
                }

                var terrainField = GetFieldInHierarchy(terrainType, "terrain");
                var matInstanceField = GetFieldInHierarchy(terrainType, "matInstance");
                var templateMaterialField = GetFieldInHierarchy(terrainType, "templateMaterial");
                foreach (var microSplatTerrain in UnityEngine.Object.FindObjectsOfType(terrainType).Take(3))
                {
                    var component = microSplatTerrain as Component;
                    var path = component != null ? GetTransformPath(component.transform) : DescribeObjectPath(microSplatTerrain);
                    var terrain = terrainField?.GetValue(microSplatTerrain);
                    AddTerrainMaterialDiagnosticSample(
                        samples,
                        seenKeys,
                        $"{path}|MicroSplat.terrain.materialTemplate",
                        GetMemberValue(terrain, "materialTemplate") as Material,
                        maxSamples);
                    AddTerrainMaterialDiagnosticSample(
                        samples,
                        seenKeys,
                        $"{path}|MicroSplat.matInstance",
                        matInstanceField?.GetValue(microSplatTerrain) as Material,
                        maxSamples);
                    AddTerrainMaterialDiagnosticSample(
                        samples,
                        seenKeys,
                        $"{path}|MicroSplat.templateMaterial",
                        templateMaterialField?.GetValue(microSplatTerrain) as Material,
                        maxSamples);
                }
            }
            catch
            {
                // Diagnostics only.
            }

            return samples;
        }

        private static void AddTerrainMaterialDiagnosticSample(
            List<KeyValuePair<string, Material>> samples,
            HashSet<string> seenKeys,
            string key,
            Material material,
            int maxSamples)
        {
            if (samples == null || seenKeys == null || samples.Count >= maxSamples || string.IsNullOrEmpty(key))
            {
                return;
            }

            if (!seenKeys.Add(key))
            {
                return;
            }

            samples.Add(new KeyValuePair<string, Material>(key, material));
        }

        private static string DescribeTerrainMaterialProperties(Material material)
        {
            if (material == null)
            {
                return "<null>";
            }

            try
            {
                var parts = new List<string>(4) { DescribeMaterial(material) };
                var textures = new List<string>(TerrainMaterialTexturePropertyNames.Length);
                foreach (var propertyName in TerrainMaterialTexturePropertyNames)
                {
                    if (TryDescribeMaterialTexture(material, propertyName, out var value))
                    {
                        textures.Add(value);
                    }
                }

                var floats = new List<string>(TerrainMaterialFloatPropertyNames.Length);
                foreach (var propertyName in TerrainMaterialFloatPropertyNames)
                {
                    if (TryDescribeMaterialFloat(material, propertyName, out var value))
                    {
                        floats.Add(value);
                    }
                }

                var colors = new List<string>(TerrainMaterialColorPropertyNames.Length);
                foreach (var propertyName in TerrainMaterialColorPropertyNames)
                {
                    if (TryDescribeMaterialColor(material, propertyName, out var value))
                    {
                        colors.Add(value);
                    }
                }

                if (textures.Count > 0)
                {
                    parts.Add($"tex={string.Join(",", textures)}");
                }

                if (floats.Count > 0)
                {
                    parts.Add($"float={string.Join(",", floats)}");
                }

                if (colors.Count > 0)
                {
                    parts.Add($"color={string.Join(",", colors)}");
                }

                return string.Join("|", parts);
            }
            catch (Exception ex)
            {
                return $"{DescribeMaterial(material)}|propsError={Unwrap(ex).Message}";
            }
        }

        private static bool TryDescribeMaterialTexture(Material material, string propertyName, out string value)
        {
            value = null;
            try
            {
                if (material == null || !material.HasProperty(propertyName))
                {
                    return false;
                }

                value = $"{propertyName}={DescribeTexture(material.GetTexture(propertyName))}";
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDescribeMaterialFloat(Material material, string propertyName, out string value)
        {
            value = null;
            try
            {
                if (material == null || !material.HasProperty(propertyName))
                {
                    return false;
                }

                value = $"{propertyName}={material.GetFloat(propertyName):F3}";
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDescribeMaterialColor(Material material, string propertyName, out string value)
        {
            value = null;
            try
            {
                if (material == null || !material.HasProperty(propertyName))
                {
                    return false;
                }

                value = $"{propertyName}={FormatColor(material.GetColor(propertyName))}";
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string DescribeTerrainMaterialState()
        {
            try
            {
                var terrainType = Type.GetType("UnityEngine.Terrain, UnityEngine.TerrainModule", throwOnError: false);
                if (terrainType == null)
                {
                    return "<terrain-type-missing>";
                }

                var parts = new List<string>(8);
                var terrains = UnityEngine.Object.FindObjectsOfType(terrainType);
                var activeTerrain = terrainType.GetProperty("activeTerrain", AnyBinding)?.GetValue(null, null);
                parts.Add($"terrains={terrains.Length} active={DescribeObjectPath(activeTerrain)}");

                var terrain = activeTerrain ?? terrains.FirstOrDefault();
                if (terrain != null)
                {
                    parts.Add($"terrain={DescribeTerrainObject(terrain)}");
                    parts.Add($"details={DescribeTerrainDetails(terrain)}");
                }

                parts.Add($"winterScript={DescribeWinterScriptState()}");
                parts.Add($"seasonFixer={DescribeSeasonMaterialFixerState()}");
                parts.Add($"microSplat={DescribeMicroSplatTerrainState()}");
                return string.Join(" | ", parts);
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static string DescribeTerrainLodState()
        {
            try
            {
                var terrainLodType = Type.GetType("TerrainLod, Assembly-CSharp", throwOnError: false);
                if (terrainLodType == null)
                {
                    return "<type-missing>";
                }

                var lods = Resources.FindObjectsOfTypeAll(terrainLodType);
                if (lods == null || lods.Length == 0)
                {
                    return "all=0";
                }

                var activeCount = lods.Count(lod => lod is Component component && component.gameObject.activeInHierarchy);
                var samples = new List<string>(Mathf.Min(lods.Length, 4));
                var terrainField = GetFieldInHierarchy(terrainLodType, "_terrain");
                var terrainLodField = GetFieldInHierarchy(terrainLodType, "_terrainLod");
                var visibleField = GetFieldInHierarchy(terrainLodType, "_terrainIsVisible");

                foreach (var lod in lods.Take(4))
                {
                    var component = lod as Component;
                    var terrain = terrainField?.GetValue(lod);
                    var terrainComponent = terrain as Component;
                    var terrainLodObject = terrainLodField?.GetValue(lod) as GameObject;
                    var drawHeightmap = terrain != null ? GetMemberValue(terrain, "drawHeightmap") : null;
                    var drawFoliage = terrain != null ? GetMemberValue(terrain, "drawTreesAndFoliage") : null;
                    samples.Add($"{(component != null ? GetTransformPath(component.transform) : "<null>")} active={(component != null && component.gameObject.activeInHierarchy)} terrain={(terrainComponent != null ? GetTransformPath(terrainComponent.transform) : "<null>")} visible={visibleField?.GetValue(lod) ?? "<null>"} drawHeightmap={drawHeightmap ?? "<null>"} drawFoliage={drawFoliage ?? "<null>"} lodObject={(terrainLodObject != null ? $"{terrainLodObject.name}:active={terrainLodObject.activeSelf}" : "<null>")}");
                }

                return $"all={lods.Length} active={activeCount} samples=[{string.Join(" || ", samples)}]";
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static string DescribeGpuInstancerState()
        {
            try
            {
                var detailManagerType = Type.GetType("GPUInstancer.GPUInstancerDetailManager, Assembly-CSharp", throwOnError: false);
                if (detailManagerType == null)
                {
                    return "<type-missing>";
                }

                var managers = Resources.FindObjectsOfTypeAll(detailManagerType);
                if (managers == null || managers.Length == 0)
                {
                    return "details=0";
                }

                var activeCount = managers.Count(manager => manager is Component component && component.gameObject.activeInHierarchy);
                var enabledCount = managers.Count(manager => manager is Behaviour behaviour && behaviour.enabled);
                var samples = new List<string>(Mathf.Min(managers.Length, 4));

                foreach (var manager in managers.Take(4))
                {
                    var component = manager as Component;
                    var behaviour = manager as Behaviour;
                    var terrain = GetMemberValue(manager, "terrain");
                    var terrainComponent = terrain as Component;
                    var terrainSettings = GetMemberValue(manager, "terrainSettings");
                    var maxDistance = terrainSettings != null ? GetMemberValue(terrainSettings, "maxDetailDistanceLegacy") : null;
                    var prototypeList = GetMemberValue(manager, "prototypeList");
                    var runtimeDataList = GetMemberValue(manager, "runtimeDataList");
                    var camera = GetMemberValue(manager, "Camera") as Camera;
                    var detailLayer = GetFieldInHierarchy(manager.GetType(), "detailLayer")?.GetValue(manager);
                    var isInitialized = GetFieldInHierarchy(manager.GetType(), "isInitialized")?.GetValue(manager);
                    var replacingInstances = GetFieldInHierarchy(manager.GetType(), "replacingInstances")?.GetValue(manager);
                    var initializingInstances = GetFieldInHierarchy(manager.GetType(), "initalizingInstances")?.GetValue(manager);
                    var isCulled = GetMemberValue(manager, "isCulled");

                    samples.Add($"{(component != null ? GetTransformPath(component.transform) : "<null>")} active={(component != null && component.gameObject.activeInHierarchy)} enabled={(behaviour != null && behaviour.enabled)} terrain={(terrainComponent != null ? GetTransformPath(terrainComponent.transform) : "<null>")} detailDistance={FormatObjectFloat(terrain != null ? GetMemberValue(terrain, "detailObjectDistance") : null)} density={FormatObjectFloat(terrain != null ? GetMemberValue(terrain, "detailObjectDensity") : null)} maxDistance={FormatObjectFloat(maxDistance)} detailLayer={detailLayer ?? "<null>"} initialized={isInitialized ?? "<null>"} replacing={replacingInstances ?? "<null>"} initializing={initializingInstances ?? "<null>"} culled={isCulled ?? "<null>"} prototypes={CountEnumerable(prototypeList)} runtime={CountEnumerable(runtimeDataList)} camera={(camera != null ? GetTransformPath(camera.transform) : "<null>")}");
                }

                return $"details={managers.Length} active={activeCount} enabled={enabledCount} samples=[{string.Join(" || ", samples)}]";
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static string DescribeTerrainObject(object terrain)
        {
            if (terrain == null)
            {
                return "<null>";
            }

            try
            {
                var component = terrain as Component;
                var data = GetMemberValue(terrain, "terrainData") as UnityEngine.Object;
                var material = GetMemberValue(terrain, "materialTemplate") as Material;
                var enabled = GetMemberValue(terrain, "enabled");
                var drawHeightmap = GetMemberValue(terrain, "drawHeightmap");
                var drawFoliage = GetMemberValue(terrain, "drawTreesAndFoliage");
                var density = GetMemberValue(terrain, "detailObjectDensity");
                return $"path={DescribeObjectPath(terrain)} enabled={enabled ?? "<null>"} active={(component != null && component.gameObject.activeInHierarchy)} drawHeightmap={drawHeightmap ?? "<null>"} drawFoliage={drawFoliage ?? "<null>"} density={FormatObjectFloat(density)} material={DescribeMaterial(material)} data={(data != null ? data.name : "<null>")}";
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static string DescribeTerrainDetails(object terrain)
        {
            try
            {
                var terrainData = terrain != null ? GetMemberValue(terrain, "terrainData") : null;
                var prototypes = terrainData != null ? GetMemberValue(terrainData, "detailPrototypes") as Array : null;
                if (prototypes == null)
                {
                    return "<null>";
                }

                var samples = new List<string>(Mathf.Min(prototypes.Length, 5));
                for (var index = 0; index < prototypes.Length && index < 5; index++)
                {
                    var prototype = prototypes.GetValue(index);
                    var texture = GetMemberValue(prototype, "prototypeTexture") as UnityEngine.Object;
                    var mesh = GetMemberValue(prototype, "prototype") as UnityEngine.Object;
                    var dryColor = GetMemberValue(prototype, "dryColor");
                    var healthyColor = GetMemberValue(prototype, "healthyColor");
                    var renderMode = GetMemberValue(prototype, "renderMode");
                    samples.Add($"{index}:{(texture != null ? texture.name : mesh != null ? mesh.name : "<none>")} dry={FormatObjectColor(dryColor)} healthy={FormatObjectColor(healthyColor)} mode={renderMode ?? "<null>"}");
                }

                return $"count={prototypes.Length} samples=[{string.Join("; ", samples)}]";
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static string DescribeObjectPath(object value)
        {
            var component = value as Component;
            if (component != null)
            {
                return GetTransformPath(component.transform);
            }

            var unityObject = value as UnityEngine.Object;
            return unityObject != null ? unityObject.name : "<null>";
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            var type = instance.GetType();
            var property = type.GetProperty(memberName, AnyBinding);
            if (property != null)
            {
                return property.GetValue(instance, null);
            }

            return GetFieldInHierarchy(type, memberName)?.GetValue(instance);
        }

        private static bool TrySetMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            try
            {
                var type = instance.GetType();
                var property = type.GetProperty(memberName, AnyBinding);
                if (property != null)
                {
                    var setter = property.GetSetMethod(nonPublic: true);
                    if (setter != null)
                    {
                        setter.Invoke(instance, new[] { value });
                        return true;
                    }
                }

                var field = GetFieldInHierarchy(type, memberName);
                if (field != null)
                {
                    field.SetValue(instance, value);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool TryInvokeParameterless(object instance, string methodName)
        {
            if (instance == null || string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            try
            {
                var method = instance.GetType().GetMethod(methodName, AnyBinding, null, Type.EmptyTypes, null);
                if (method == null)
                {
                    return false;
                }

                method.Invoke(instance, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int CountEnumerable(object value)
        {
            if (value == null)
            {
                return -1;
            }

            if (value is System.Collections.ICollection collection)
            {
                return collection.Count;
            }

            if (value is IEnumerable enumerable)
            {
                try
                {
                    return enumerable.Cast<object>().Count();
                }
                catch
                {
                    return -1;
                }
            }

            return -1;
        }

        private static bool MaterialsAppearEquivalent(Material current, Material expected)
        {
            if (current == null || expected == null)
            {
                return false;
            }

            if (ReferenceEquals(current, expected))
            {
                return true;
            }

            return string.Equals(NormalizeMaterialName(current.name), NormalizeMaterialName(expected.name), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeMaterialName(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
            {
                return string.Empty;
            }

            return materialName
                .Replace(" (Instance)", string.Empty)
                .Replace("(Instance)", string.Empty)
                .Trim();
        }

        private static string FormatObjectFloat(object value)
        {
            if (value is float single)
            {
                return single.ToString("F2");
            }

            if (value is double dbl)
            {
                return dbl.ToString("F2");
            }

            return value?.ToString() ?? "<null>";
        }

        private static string FormatObjectColor(object value)
        {
            if (value is Color color)
            {
                return FormatColor(color);
            }

            return value?.ToString() ?? "<null>";
        }

        private static string DescribeWinterScriptState()
        {
            try
            {
                var winterScriptType = Type.GetType("WinterScript, Assembly-CSharp", throwOnError: false);
                if (winterScriptType == null)
                {
                    return "<type-missing>";
                }

                var active = UnityEngine.Object.FindObjectsOfType(winterScriptType);
                var all = Resources.FindObjectsOfTypeAll(winterScriptType);
                if (all == null || all.Length == 0)
                {
                    return "active=0 all=0";
                }

                var script = active.FirstOrDefault() ?? all.FirstOrDefault();
                var component = script as Component;
                var terrainMaterial = GetFieldInHierarchy(winterScriptType, "TerrainMaterial")?.GetValue(script) as Material;
                var repaint = GetFieldInHierarchy(winterScriptType, "_terrainDetailsRepaint")?.GetValue(script);
                var details = GetFieldInHierarchy(winterScriptType, "TerrainDetails")?.GetValue(script) as Texture2D[];
                return $"active={active.Length} all={all.Length} sample={(component != null ? GetTransformPath(component.transform) : "<null>")} enabled={(component is Behaviour behaviour && behaviour.enabled)} repaint={(repaint != null ? repaint.GetType().Name : "<null>")} terrainMaterial={DescribeMaterial(terrainMaterial)} detailTex=[{string.Join(",", (details ?? Array.Empty<Texture2D>()).Take(4).Select(texture => texture != null ? texture.name : "<null>"))}]";
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static string DescribeSeasonMaterialFixerState()
        {
            try
            {
                var fixerType = Type.GetType("SeasonsMaterialsFixer, Assembly-CSharp", throwOnError: false);
                if (fixerType == null)
                {
                    return "<type-missing>";
                }

                var fixers = Resources.FindObjectsOfTypeAll(fixerType);
                var fixer = fixers.FirstOrDefault();
                var map = fixer != null ? GetFieldInHierarchy(fixerType, "_materialsMap")?.GetValue(fixer) : null;
                var group = fixer != null ? GetFieldInHierarchy(fixerType, "seasonsMaterialsGroup_0")?.GetValue(fixer) : null;
                var materialsField = group != null ? GetFieldInHierarchy(group.GetType(), "Materials") : null;
                var materials = materialsField?.GetValue(group) as IEnumerable;
                var materialCount = materials != null ? materials.Cast<object>().Count() : -1;

                return $"fixers={(fixers != null ? fixers.Length : 0)} map={(map as UnityEngine.Object != null ? ((UnityEngine.Object)map).name : map != null ? map.GetType().Name : "<null>")} runtimeGroup={(group != null ? materialCount.ToString() : "<null>")} seasonComponents={CountObjectsOfType("SeasonsMaterialsTerrain, Assembly-CSharp")}/{CountObjectsOfType("SeasonsMaterialsGrassInitialization, Assembly-CSharp")}/{CountObjectsOfType("SeasonsMaterialsSlice, Assembly-CSharp")}";
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static string DescribeMicroSplatTerrainState()
        {
            try
            {
                var terrainType = Type.GetType("JBooth.MicroSplat.MicroSplatTerrain, JBooth.MicroSplat.Core", throwOnError: false);
                var objectType = Type.GetType("JBooth.MicroSplat.MicroSplatObject, JBooth.MicroSplat.Core", throwOnError: false);
                if (terrainType == null || objectType == null)
                {
                    return "<type-missing>";
                }

                var currentSeason = objectType.GetField("currentSeason", AnyBinding)?.GetValue(null);
                var currentQuality = objectType.GetField("currentQuality", AnyBinding)?.GetValue(null);
                var terrains = UnityEngine.Object.FindObjectsOfType(terrainType);
                var samples = new List<string>(Mathf.Min(terrains.Length, 3));
                var terrainField = GetFieldInHierarchy(terrainType, "terrain");
                var matInstanceField = GetFieldInHierarchy(terrainType, "matInstance");
                var templateMaterialField = GetFieldInHierarchy(terrainType, "templateMaterial");
                var highField = GetFieldInHierarchy(terrainType, "templateMaterialHigh");
                var normalField = GetFieldInHierarchy(terrainType, "templateMaterialNormal");
                var lowField = GetFieldInHierarchy(terrainType, "templateMaterialLow");

                foreach (var terrainObject in terrains.Take(3))
                {
                    var component = terrainObject as Component;
                    var terrain = terrainField?.GetValue(terrainObject);
                    var terrainMaterial = terrain != null ? GetMemberValue(terrain, "materialTemplate") as Material : null;
                    var matInstance = matInstanceField?.GetValue(terrainObject) as Material;
                    var templateMaterial = templateMaterialField?.GetValue(terrainObject) as Material;
                    samples.Add($"{(component != null ? GetTransformPath(component.transform) : "<null>")} active={(component != null && component.gameObject.activeInHierarchy)} terrainMat={DescribeMaterial(terrainMaterial)} instance={DescribeMaterial(matInstance)} template={DescribeMaterial(templateMaterial)} sets={DescribeMicroSplatMaterialSets(highField?.GetValue(terrainObject), normalField?.GetValue(terrainObject), lowField?.GetValue(terrainObject))}");
                }

                return $"count={terrains.Length} season={currentSeason} quality={currentQuality} samples=[{string.Join(" || ", samples)}]";
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static string DescribeMicroSplatMaterialSets(object high, object normal, object low)
        {
            return $"high({DescribeMicroSplatMaterialSet(high)}) normal({DescribeMicroSplatMaterialSet(normal)}) low({DescribeMicroSplatMaterialSet(low)})";
        }

        private static string DescribeMicroSplatMaterialSet(object set)
        {
            if (set == null)
            {
                return "<null>";
            }

            try
            {
                var type = set.GetType();
                var winter = GetFieldInHierarchy(type, "WinterMaterial")?.GetValue(set) as Material;
                var summer = GetFieldInHierarchy(type, "SummerMaterial")?.GetValue(set) as Material;
                var springEarly = GetFieldInHierarchy(type, "SpringEarlyMaterial")?.GetValue(set) as Material;
                return $"winter={DescribeMaterialName(winter)} summer={DescribeMaterialName(summer)} springEarly={DescribeMaterialName(springEarly)}";
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static string DescribeMaterialName(Material material)
        {
            return material != null ? material.name : "<null>";
        }

        private static int CountObjectsOfType(string typeName)
        {
            try
            {
                var type = Type.GetType(typeName, throwOnError: false);
                return type != null ? Resources.FindObjectsOfTypeAll(type).Length : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static IEnumerable<FieldInfo> GetFieldsInHierarchy(Type type)
        {
            while (type != null)
            {
                foreach (var field in type.GetFields(AnyBinding | BindingFlags.DeclaredOnly))
                {
                    yield return field;
                }

                type = type.BaseType;
            }
        }

        private static FieldInfo GetFieldInHierarchy(Type type, string fieldName)
        {
            while (type != null)
            {
                var field = type.GetField(fieldName, AnyBinding);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            return null;
        }

        private static string DescribeCurrentScreenState()
        {
            try
            {
                var manager = EFT.UI.Screens.EftScreenManager.Instance;
                var current = manager?.CurrentScreenController;
                var currentBase = manager?.CurrentBaseScreenController;
                var currentType = current != null ? current.ScreenType.ToString() : "<null>";
                var baseType = currentBase != null ? currentBase.ScreenType.ToString() : "<null>";
                var previousType = _previewPreviousScreenController != null ? _previewPreviousScreenController.ScreenType.ToString() : "<null>";
                var restored = _battleScreenContextRestored ? "true" : "false";
                var shell = _battleUiShellApplied ? "true" : "false";
                return $"current={currentType} base={baseType} previous={previousType} battleRestored={restored} shell={shell}";
            }
            catch (Exception ex)
            {
                return $"error={Unwrap(ex).Message}";
            }
        }

        private static string TryGetCameraTag(Camera camera)
        {
            if (camera == null)
            {
                return "<null>";
            }

            try
            {
                return camera.tag;
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static void ResetPreviewSnowDiagnostics()
        {
            _previewSnowPreCullEventCount = 0;
            _previewSnowPreCullAcceptedCameraCount = 0;
            _previewSnowPreCullAcceptedMainCameraCount = 0;
            _previewSnowPreCullAcceptedOpticCameraCount = 0;
            _previewSnowPreCullCapturedBattleCameraCount = 0;
            _previewSnowPreCullLooksBattleCameraCount = 0;
            _previewSnowPreCullPreviewCameraCount = 0;
            _previewSnowPreCullUiCameraCount = 0;
            _previewSnowPreCullOtherCameraCount = 0;
            _lastPreviewSnowPreCullCameraPath = null;
            _lastPreviewSnowPreCullCameraTag = null;
            _lastPreviewSnowPreCullTreatedAsBattleCamera = false;
            _snowCommandBufferCameraOverride = null;
            _snowCommandBufferCameraOverrideCount = 0;
            _lastSnowCommandBufferCameraOverridePath = null;
            _previewSnowMaskDisableRequestCount = 0;
            _lastPreviewSnowMaskDisableRequest = null;
            _lastManualSnowRenderFrame = -1;
            _terrainSeasonForceCount = 0;
            _terrainSeasonRefreshCount = 0;
            _terrainSeasonMaterialFixCount = 0;
            _lastTerrainSeasonState = null;
            _terrainSeasonRefreshAttempted = false;
            _nextTerrainSeasonRefreshAt = 0f;
            _nextSnowPresentationForceAt = 0f;
            _snowPresentationForceCount = 0;
            _snowCommandBuffersRebuiltForPreview = false;
            _snowCommandBufferRebuildCount = 0;
            _lastSnowCommandBufferRebuildState = null;
        }

        private static string FormatRect(Rect rect)
        {
            return $"({rect.x:F3},{rect.y:F3},{rect.width:F3},{rect.height:F3})";
        }

        private static string FormatColor(Color color)
        {
            return $"({color.r:F3},{color.g:F3},{color.b:F3},{color.a:F3})";
        }

        private static string FormatVector4(Vector4 vector)
        {
            return $"({vector.x:F3},{vector.y:F3},{vector.z:F3},{vector.w:F3})";
        }

        private static string Abbreviate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || maxLength <= 0 || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength) + "...";
        }

        private static bool Approximately(float left, float right)
        {
            return Mathf.Abs(left - right) <= 0.001f;
        }

        private static bool Approximately(Color left, Color right)
        {
            return Approximately(left.r, right.r) &&
                   Approximately(left.g, right.g) &&
                   Approximately(left.b, right.b) &&
                   Approximately(left.a, right.a);
        }

        private static bool HasExplicitlyProtectedPreviewAncestor(Transform transform)
        {
            for (var current = transform; current != null; current = current.parent)
            {
                var currentName = (current.gameObject.name ?? string.Empty).ToLowerInvariant();
                if (currentName.Contains("viewport") ||
                    currentName.Contains("weaponpreview") ||
                    currentName.Contains("weapon preview") ||
                    currentName.Contains("slot") ||
                    currentName.Contains("dropdown") ||
                    currentName.Contains("characteristic"))
                {
                    return true;
                }

                foreach (var component in current.GetComponents<Component>())
                {
                    var componentName = component?.GetType().Name;
                    if (string.Equals(componentName, "CameraViewporter", StringComparison.Ordinal) ||
                        string.Equals(componentName, "WeaponPreview", StringComparison.Ordinal) ||
                        string.Equals(componentName, "ModdingScreenSlotView", StringComparison.Ordinal) ||
                        string.Equals(componentName, "DropDownMenu", StringComparison.Ordinal) ||
                        string.Equals(componentName, "CharacteristicsPanel", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasProtectedAncestorOrSelf(GameObject gameObject)
        {
            for (var current = gameObject.transform; current != null; current = current.parent)
            {
                var currentName = (current.gameObject.name ?? string.Empty).ToLowerInvariant();
                if (ProtectedNameTokens.Any(currentName.Contains))
                {
                    return true;
                }

                var componentNames = current
                    .GetComponents<Component>()
                    .Where(component => component != null)
                    .Select(component => component.GetType().Name);

                if (componentNames.Any(componentName => ProtectedComponentNames.Contains(componentName, StringComparer.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RestoreSuppressedBackdropVisuals()
        {
            if (SuppressedBackdropBehaviours.Count > 0)
            {
                foreach (var behaviour in SuppressedBackdropBehaviours)
                {
                    if (behaviour != null)
                    {
                        behaviour.enabled = true;
                    }
                }

                SuppressedBackdropBehaviours.Clear();
            }

            if (SuppressedBackdropGraphics.Count > 0)
            {
                foreach (var graphic in SuppressedBackdropGraphics)
                {
                    if (graphic != null)
                    {
                        graphic.enabled = true;
                    }
                }

                SuppressedBackdropGraphics.Clear();
            }
        }

        private static void TryLogRemainingBackdropCandidates()
        {
            if (_backdropDiagnosticsLogged || _log == null)
            {
                return;
            }

            var rootSize = new Vector2(Screen.width, Screen.height);
            var candidateLines = new List<string>(8);

            foreach (var cameraImage in Resources.FindObjectsOfTypeAll<CameraImage>())
            {
                if (cameraImage == null || !cameraImage.gameObject.activeInHierarchy || !cameraImage.enabled)
                {
                    continue;
                }

                if (HasProtectedAncestorOrSelf(cameraImage.gameObject))
                {
                    continue;
                }

                candidateLines.Add($"CameraImage: {GetTransformPath(cameraImage.transform)}");
                if (candidateLines.Count >= 8)
                {
                    break;
                }
            }

            if (candidateLines.Count < 8)
            {
                foreach (var canvas in EnumerateCanvasRoots())
                {
                    foreach (var behaviour in canvas.GetComponentsInChildren<Behaviour>(true))
                    {
                        var typeName = behaviour?.GetType().Name;
                        if (!string.Equals(typeName, "RawImage", StringComparison.Ordinal) ||
                            behaviour == null ||
                            !behaviour.gameObject.activeInHierarchy ||
                            !behaviour.enabled)
                        {
                            continue;
                        }

                        if (!IsBackdropGraphicCandidate(behaviour.gameObject, rootSize))
                        {
                            continue;
                        }

                        candidateLines.Add($"RawImage: {GetTransformPath(behaviour.transform)}");
                        if (candidateLines.Count >= 8)
                        {
                            break;
                        }
                    }

                    if (candidateLines.Count >= 8)
                    {
                        break;
                    }
                }
            }

            if (candidateLines.Count < 8)
            {
                foreach (var canvas in EnumerateCanvasRoots())
                {
                    foreach (var behaviour in canvas.GetComponentsInChildren<Behaviour>(true))
                    {
                        var typeName = behaviour?.GetType().Name;
                        if (!string.Equals(typeName, "Image", StringComparison.Ordinal) ||
                            behaviour == null ||
                            !behaviour.gameObject.activeInHierarchy ||
                            !behaviour.enabled)
                        {
                            continue;
                        }

                        if (!IsBackdropGraphicCandidate(behaviour.gameObject, rootSize))
                        {
                            continue;
                        }

                        candidateLines.Add($"Image: {GetTransformPath(behaviour.transform)}");
                        if (candidateLines.Count >= 8)
                        {
                            break;
                        }
                    }

                    if (candidateLines.Count >= 8)
                    {
                        break;
                    }
                }
            }

            _backdropDiagnosticsLogged = true;

            _log.LogInfo("[ULE] Preset preview remaining backdrop candidates:");

            if (candidateLines.Count > 0)
            {
                foreach (var line in candidateLines)
                {
                    _log.LogInfo($"[ULE]   {line}");
                }
            }
            else
            {
                _log.LogInfo("[ULE]   <none>");
            }
        }

        private static void TryLogCameraDiagnostics()
        {
            if (_cameraDiagnosticsLogged || _log == null)
            {
                return;
            }

            _cameraDiagnosticsLogged = true;

            var cameraLines = new List<string>(16);
            var screen = GetLiveEditBuildScreen();
            if (screen != null)
            {
                foreach (var camera in screen.GetComponentsInChildren<Camera>(true))
                {
                    if (camera == null)
                    {
                        continue;
                    }

                    cameraLines.Add(
                        $"{GetTransformPath(camera.transform)} | enabled={camera.enabled} clear={camera.clearFlags} depth={camera.depth:F1} rect={camera.rect} cull=0x{camera.cullingMask:X8} targetTex={(camera.targetTexture != null ? camera.targetTexture.name : "<null>")}");
                }
            }

            if (cameraLines.Count < 16)
            {
                foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>())
                {
                    if (camera == null || !camera.gameObject.activeInHierarchy || !camera.enabled)
                    {
                        continue;
                    }

                    var path = GetTransformPath(camera.transform);
                    if (!path.Contains("Common UI") &&
                        !path.Contains("EnvironmentUI") &&
                        !path.Contains("Weapon") &&
                        !path.Contains("Preview") &&
                        !path.Contains("Battle") &&
                        !path.Contains("Menu"))
                    {
                        continue;
                    }

                    if (cameraLines.Contains(
                            $"{path} | enabled={camera.enabled} clear={camera.clearFlags} depth={camera.depth:F1} rect={camera.rect} cull=0x{camera.cullingMask:X8} targetTex={(camera.targetTexture != null ? camera.targetTexture.name : "<null>")}"))
                    {
                        continue;
                    }

                    cameraLines.Add(
                        $"{path} | enabled={camera.enabled} clear={camera.clearFlags} depth={camera.depth:F1} rect={camera.rect} cull=0x{camera.cullingMask:X8} targetTex={(camera.targetTexture != null ? camera.targetTexture.name : "<null>")}");

                    if (cameraLines.Count >= 16)
                    {
                        break;
                    }
                }
            }

            _log.LogInfo("[ULE] Preset preview active cameras:");

            if (cameraLines.Count > 0)
            {
                foreach (var line in cameraLines)
                {
                    _log.LogInfo($"[ULE]   {line}");
                }
            }
            else
            {
                _log.LogInfo("[ULE]   <none>");
            }

            try
            {
                var battleCamera = GetBattleCamera();
                _log.LogInfo($"[ULE] Preset preview battle camera: {(battleCamera != null ? DescribeBattleCamera(battleCamera) : "<null>")}");
                TryLogBattleCameraComponentDiagnostics(battleCamera);
            }
            catch
            {
                // Diagnostics only.
            }

            try
            {
                var allActiveCameras = Resources.FindObjectsOfTypeAll<Camera>()
                    .Where(camera => camera != null && camera.enabled && camera.gameObject.activeInHierarchy)
                    .OrderBy(camera => camera.depth)
                    .ThenBy(camera => GetTransformPath(camera.transform))
                    .Take(32)
                    .Select(camera => DescribeBattleCamera(camera))
                    .ToList();

                _log.LogInfo("[ULE] Preset preview all active cameras:");
                if (allActiveCameras.Count == 0)
                {
                    _log.LogInfo("[ULE]   <none>");
                }
                else
                {
                    foreach (var line in allActiveCameras)
                    {
                        _log.LogInfo($"[ULE]   {line}");
                    }
                }
            }
            catch
            {
                // Diagnostics only.
            }
        }

        private static void TryLogBattleCameraComponentDiagnostics(Camera battleCamera)
        {
            if (_battleCameraComponentDiagnosticsLogged || _log == null || battleCamera == null)
            {
                return;
            }

            _battleCameraComponentDiagnosticsLogged = true;

            try
            {
                var lines = new List<string>(32);
                foreach (var component in battleCamera.GetComponents<Component>())
                {
                    if (component == null)
                    {
                        continue;
                    }

                    var type = component.GetType();
                    var enabledText = component is Behaviour behaviour ? $" enabled={behaviour.enabled}" : string.Empty;
                    var onRenderImage = type.GetMethod(
                        "OnRenderImage",
                        AnyBinding,
                        binder: null,
                        types: new[] { typeof(RenderTexture), typeof(RenderTexture) },
                        modifiers: null) != null;
                    var onPreCull = type.GetMethod(
                        "OnPreCull",
                        AnyBinding,
                        binder: null,
                        types: Type.EmptyTypes,
                        modifiers: null) != null;
                    var onPreRender = type.GetMethod(
                        "OnPreRender",
                        AnyBinding,
                        binder: null,
                        types: Type.EmptyTypes,
                        modifiers: null) != null;

                    var callbackText = string.Empty;
                    if (onRenderImage || onPreCull || onPreRender)
                    {
                        var callbacks = new List<string>(3);
                        if (onRenderImage)
                        {
                            callbacks.Add("OnRenderImage");
                        }

                        if (onPreCull)
                        {
                            callbacks.Add("OnPreCull");
                        }

                        if (onPreRender)
                        {
                            callbacks.Add("OnPreRender");
                        }

                        callbackText = $" callbacks={string.Join(",", callbacks)}";
                    }

                    lines.Add($"{type.FullName}{enabledText}{callbackText}");
                }

                _log.LogInfo("[ULE] Preset preview battle camera components:");
                foreach (var line in lines)
                {
                    _log.LogInfo($"[ULE]   {line}");
                }
            }
            catch
            {
                // Diagnostics only.
            }
        }

        private static IEnumerable<Component> EnumerateCanvasRoots()
        {
            if (_canvasType == null)
            {
                _canvasType = Type.GetType(CanvasTypeName, throwOnError: false);
            }

            if (_canvasType == null)
            {
                yield break;
            }

            var objects = Resources.FindObjectsOfTypeAll(_canvasType);
            if (objects == null)
            {
                yield break;
            }

            foreach (var obj in objects)
            {
                if (obj is Component component)
                {
                    yield return component;
                }
            }
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            var stack = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
            {
                stack.Push(current.name);
            }

            return string.Join("/", stack.ToArray());
        }

        internal static bool ShouldBypassCameraImageEffects(Component component)
        {
            return IsPreviewTransitionActiveOrOpen &&
                   component != null &&
                   component.gameObject.activeInHierarchy;
        }

        internal static bool ShouldBypassCameraImageAmbientEffects(Component component)
        {
            return ShouldBypassCameraImageEffects(component);
        }

        private static bool IsPreviewCameraImage(CameraImage cameraImage, Camera previewCamera, RectTransform previewHost)
        {
            if (cameraImage == null)
            {
                return false;
            }

            try
            {
                var targetCameraField = typeof(CameraImage).GetField("targetCamera", AnyBinding);
                var targetCamera = targetCameraField?.GetValue(cameraImage) as Camera;
                if (previewCamera != null && targetCamera == previewCamera)
                {
                    return true;
                }

                if (previewHost != null && cameraImage.transform != null && cameraImage.transform.IsChildOf(previewHost))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore lookup issues and fall through.
            }

            return false;
        }

        private sealed class PreviewLightSnapshot
        {
            public readonly Light Light;
            public readonly bool Enabled;
            public readonly float Intensity;

            public PreviewLightSnapshot(Light light)
            {
                Light = light;
                Enabled = light != null && light.enabled;
                Intensity = light != null ? light.intensity : 0f;
            }
        }

        private sealed class PreviewRendererMaterialSnapshot
        {
            public readonly Renderer Renderer;
            public readonly int MaterialIndex;
            public readonly MaterialPropertyBlock PropertyBlock;

            public PreviewRendererMaterialSnapshot(Renderer renderer, int materialIndex, MaterialPropertyBlock propertyBlock)
            {
                Renderer = renderer;
                MaterialIndex = materialIndex;
                PropertyBlock = propertyBlock;
            }
        }

        private sealed class LightDiagnosticsSnapshot
        {
            public readonly Light Light;
            public readonly string Path;
            public readonly int CullingMask;
            public readonly float Intensity;
            public readonly Color Color;
            public readonly LightShadows Shadows;

            public LightDiagnosticsSnapshot(Light light)
            {
                Light = light;
                Path = light != null ? GetTransformPath(light.transform) : "<null>";
                CullingMask = light != null ? light.cullingMask : 0;
                Intensity = light != null ? light.intensity : 0f;
                Color = light != null ? light.color : Color.black;
                Shadows = light != null ? light.shadows : LightShadows.None;
            }

            public string DescribeCurrentState()
            {
                if (Light == null)
                {
                    return $"{Path} destroyed";
                }

                return $"{Path} enabled {true}->{Light.enabled} mask 0x{CullingMask:X8}->0x{Light.cullingMask:X8} intensity {Intensity:F2}->{Light.intensity:F2} color {FormatColor(Color)}->{FormatColor(Light.color)} shadows {Shadows}->{Light.shadows}";
            }
        }

        private sealed class RenderSettingsSnapshot
        {
            private bool _captured;
            private Color _ambientEquatorColor;
            private Color _ambientGroundColor;
            private float _ambientIntensity;
            private Color _ambientLight;
            private AmbientMode _ambientMode;
            private Color _ambientSkyColor;
            private bool _fog;

            public void Capture()
            {
                _ambientEquatorColor = RenderSettings.ambientEquatorColor;
                _ambientGroundColor = RenderSettings.ambientGroundColor;
                _ambientIntensity = RenderSettings.ambientIntensity;
                _ambientLight = RenderSettings.ambientLight;
                _ambientMode = RenderSettings.ambientMode;
                _ambientSkyColor = RenderSettings.ambientSkyColor;
                _fog = RenderSettings.fog;
                _captured = true;
            }

            public void Restore()
            {
                if (!_captured)
                {
                    return;
                }

                RenderSettings.ambientEquatorColor = _ambientEquatorColor;
                RenderSettings.ambientGroundColor = _ambientGroundColor;
                RenderSettings.ambientIntensity = _ambientIntensity;
                RenderSettings.ambientLight = _ambientLight;
                RenderSettings.ambientMode = _ambientMode;
                RenderSettings.ambientSkyColor = _ambientSkyColor;
                RenderSettings.fog = _fog;
            }

            public void Clear()
            {
                _captured = false;
            }
        }

        private sealed class EditOpenTimingSession
        {
            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
            private readonly List<EditOpenTimingStep> _steps = new List<EditOpenTimingStep>(64);
            private readonly BepInEx.Logging.ManualLogSource _log;
            private readonly string _tpl;
            private readonly string _presetName;
            private readonly int _nodeCount;
            private readonly bool _applyOnClose;

            public EditOpenTimingSession(BepInEx.Logging.ManualLogSource log, LootItem item, bool applyOnClose)
            {
                _log = log;
                _tpl = item?.Tpl ?? "<null>";
                _presetName = string.IsNullOrWhiteSpace(item?.PresetName) ? "<none>" : item.PresetName;
                _nodeCount = CountLootNodes(item);
                _applyOnClose = applyOnClose;
            }

            public bool Reported { get; private set; }

            public float ElapsedSeconds => (float)_stopwatch.Elapsed.TotalSeconds;

            public void Mark(string name)
            {
                if (Reported || string.IsNullOrWhiteSpace(name))
                {
                    return;
                }

                lock (_steps)
                {
                    if (Reported)
                    {
                        return;
                    }

                    _steps.Add(new EditOpenTimingStep(name, _stopwatch.Elapsed.TotalMilliseconds));
                }
            }

            public void Report(string reason)
            {
                if (Reported)
                {
                    return;
                }

                List<EditOpenTimingStep> snapshot;
                lock (_steps)
                {
                    if (Reported)
                    {
                        return;
                    }

                    Reported = true;
                    snapshot = _steps.ToList();
                }

                var log = _log ?? PresetPreviewBridge._log;
                if (log == null)
                {
                    return;
                }

                var totalMs = _stopwatch.Elapsed.TotalMilliseconds;
                var tryOpenReturnMs = FindStepTime(snapshot, "TryOpen.return true");
                var loadingStartMs = FindStepTime(snapshot, "WeaponPreview.onLoadingStart.end");
                var loadingFinishedMs = FindStepTime(snapshot, "WeaponPreview.onLoadingFinished.begin");
                var slotIconsEndMs = FindStepTime(snapshot, "ItemObserveScreen.CreateModSlotViews.end");
                var bundleWaitMs = SpanBetween(loadingStartMs, loadingFinishedMs);
                var postReturnMs = SpanBetween(tryOpenReturnMs, slotIconsEndMs >= 0d ? slotIconsEndMs : totalMs);

                log.LogInfo($"[ULE] EditBuild timing: total={totalMs:F1} ms reason={reason} tpl={_tpl} preset={_presetName} nodes={_nodeCount} applyOnClose={_applyOnClose}");
                log.LogInfo(
                    "[ULE]   spans: " +
                    $"runtimeBuild={FormatTiming(FindDuration(snapshot, "BuildRuntimePresetItem"))}, " +
                    $"showQueued={FormatTiming(FindDuration(snapshot, "RaidEditBuildController.TryShowQueued"))}, " +
                    $"immediatePresentation={FormatTiming(FindDuration(snapshot, "ApplyImmediateEditBuildPresentation"))}, " +
                    $"setupPreviewCall={FormatTiming(FindDuration(snapshot, "WeaponPreview.SetupItemPreview"))}, " +
                    $"bundleWait={FormatTiming(bundleWaitMs)}, " +
                    $"prefab={FormatTiming(FindDuration(snapshot, "PoolManager.CreateCleanLootPrefab"))}, " +
                    $"slotIcons={FormatTiming(FindDuration(snapshot, "ItemObserveScreen.CreateModSlotViews"))}, " +
                    $"postReturn={FormatTiming(postReturnMs)}");
                log.LogInfo(
                    "[ULE]   wrapper: " +
                    $"reflection={FormatTiming(FindDuration(snapshot, "EnsureReflection"))}, " +
                    $"session={FormatTiming(FindDuration(snapshot, "ResolveBackendSession"))}, " +
                    $"battlePresentation={FormatTiming(FindDuration(snapshot, "ApplyBattleSafePresentation"))}, " +
                    $"refreshWeapon={FormatTiming(FindDuration(snapshot, "ItemObserveScreen.RefreshWeapon"))}");
                log.LogInfo(
                    "[ULE]   immediate: " +
                    $"context={FormatTiming(FindDuration(snapshot, "Immediate.ContextAndUiRepair"))}, " +
                    $"battleCamera={FormatTiming(FindDuration(snapshot, "Immediate.BattleCameraRepair"))}, " +
                    $"previewCamera={FormatTiming(FindDuration(snapshot, "Immediate.PreviewCameraSetup"))}, " +
                    $"previewLight={FormatTiming(FindDuration(snapshot, "Immediate.PreviewLighting"))}, " +
                    $"snowSeason={FormatTiming(FindDuration(snapshot, "Immediate.SnowSeasonRepair"))}, " +
                    $"snow={FormatTiming(FindDuration(snapshot, "Immediate.SnowPresentation"))}, " +
                    $"seasonMaterials={FormatTiming(FindDuration(snapshot, "Immediate.SeasonMaterials"))}, " +
                    $"screenPrep={FormatTiming(FindDuration(snapshot, "Immediate.ScreenPrepAndBackdrops"))}, " +
                    $"renderSettings={FormatTiming(FindDuration(snapshot, "Immediate.RenderSettingsRestore"))}");
                try
                {
                    var battleCamera = GetBattleCamera();
                    log.LogInfo($"[ULE]   state: currentScreen={DescribeCurrentScreenState()} | battleCamera={(battleCamera != null ? DescribeBattleCamera(battleCamera) : "<null>")} | terrainLod={DescribeTerrainLodState()} | gpuInstancer={DescribeGpuInstancerState()}");
                }
                catch (Exception ex)
                {
                    log.LogInfo($"[ULE]   timingStateSnapshotFailed={Unwrap(ex).Message}");
                }
            }

            private static double FindDuration(List<EditOpenTimingStep> steps, string stagePrefix)
            {
                foreach (var step in steps)
                {
                    if (step.Name.StartsWith(stagePrefix, StringComparison.Ordinal) &&
                        step.Name.IndexOf(".end (", StringComparison.Ordinal) >= 0 &&
                        TryExtractDuration(step.Name, out var durationMs))
                    {
                        return durationMs;
                    }
                }

                return -1d;
            }

            private static double FindStepTime(List<EditOpenTimingStep> steps, string stepPrefix)
            {
                foreach (var step in steps)
                {
                    if (step.Name.StartsWith(stepPrefix, StringComparison.Ordinal))
                    {
                        return step.ElapsedMs;
                    }
                }

                return -1d;
            }

            private static double SpanBetween(double startMs, double endMs)
            {
                if (startMs < 0d || endMs < 0d || endMs < startMs)
                {
                    return -1d;
                }

                return endMs - startMs;
            }

            private static bool TryExtractDuration(string stepName, out double durationMs)
            {
                durationMs = -1d;
                var start = stepName.LastIndexOf('(');
                var end = stepName.LastIndexOf(" ms)", StringComparison.Ordinal);
                if (start < 0 || end <= start)
                {
                    return false;
                }

                var value = stepName.Substring(start + 1, end - start - 1);
                return double.TryParse(
                    value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out durationMs);
            }

            private static string FormatTiming(double valueMs)
            {
                return valueMs >= 0d ? $"{valueMs:F1}ms" : "n/a";
            }

            private static int CountLootNodes(LootItemNode node)
            {
                if (node == null)
                {
                    return 0;
                }

                var total = 1;
                foreach (var child in node.Children ?? Enumerable.Empty<LootItemNode>())
                {
                    total += CountLootNodes(child);
                }

                return total;
            }
        }

        private sealed class EditOpenTimingStep
        {
            public EditOpenTimingStep(string name, double elapsedMs)
            {
                Name = name;
                ElapsedMs = elapsedMs;
            }

            public string Name { get; }

            public double ElapsedMs { get; }
        }

        private sealed class RaidEditBuildController : EditBuildScreen.EditBuildScreenController
        {
            public RaidEditBuildController(Item item, InventoryController inventoryController, EFT.IEftSession session)
                : base(item, inventoryController, session)
            {
            }

            public EditBuildScreen ScreenInstance => Screen;

            public override EStateSwitcher MenuChatBarVisibility => EStateSwitcher.Disabled;

            public override EStateSwitcher TaskBarButtonsAvailability => EStateSwitcher.Disabled;

            public override EStateSwitcher ShowEnvironment => EStateSwitcher.Disabled;

            public override EStateSwitcher ShowEnvironmentCamera => EStateSwitcher.Disabled;

            public override EStateSwitcher EnvironmentOverlay => EStateSwitcher.Disabled;

            public override EStateSwitcher CameraBlur => EStateSwitcher.Disabled;

            public override bool MainEnvironment => false;

            public override bool RotateEnvironment => false;

            public override bool QueuePreviousScreen => false;

            public override Task<bool> CloseScreenInterruption(bool moveForward)
            {
                return method_11(moveForward);
            }

            public bool TryShowQueued(out string error)
            {
                error = null;

                try
                {
                    CloseAllWindows();
                    ShowScreen(EScreenState.Queued);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Failed to show edit-build screen: {Unwrap(ex).Message}";
                    return false;
                }
            }
        }

        private sealed class UleRuntimeAttachmentManipulation : EFT.InventoryLogic.EditBuildManipulation
        {
            private readonly BepInEx.Logging.ManualLogSource _log;
            private readonly HashSet<string> _loggedSlots = new HashSet<string>(StringComparer.Ordinal);

            public UleRuntimeAttachmentManipulation(
                InventoryController controller,
                CompoundItem[] manipulationCollections,
                BepInEx.Logging.ManualLogSource log,
                int modCount,
                int armorPlateCount,
                int slotCandidateCount,
                int addedCount,
                int failedCount)
                : base(controller, manipulationCollections, null, null, false)
            {
                _log = log;
                ModCount = modCount;
                ArmorPlateCount = armorPlateCount;
                SlotCandidateCount = slotCandidateCount;
                AddedCount = addedCount;
                FailedCount = failedCount;
            }

            public int ModCount { get; }

            public int ArmorPlateCount { get; }

            public int SlotCandidateCount { get; }

            public int AddedCount { get; }

            public int FailedCount { get; }

            public override IEnumerable<Item> GetItemCollections(Slot slot)
            {
                if (slot == null)
                {
                    return Enumerable.Empty<Item>();
                }

                var items = base.GetItemCollections(slot).ToArray();
                LogSlotChoices(slot, items.Length);
                return items;
            }

            private void LogSlotChoices(Slot slot, int itemCount)
            {
                if (!EnablePreviewDiagnostics || _log == null || slot == null || _loggedSlots.Count >= 32)
                {
                    return;
                }

                var containedTemplateId = slot.ContainedItem != null
                    ? slot.ContainedItem.TemplateId.ToString()
                    : "<empty>";
                var key = $"{slot.ID}|{containedTemplateId}";
                if (!_loggedSlots.Add(key))
                {
                    return;
                }

                _log.LogInfo($"[ULE] Runtime attachment choices for slot '{slot.ID}': shown={itemCount}, poolMods={ModCount}, poolArmorPlates={ArmorPlateCount}, poolSlotCandidates={SlotCandidateCount}, poolAdded={AddedCount}, poolFailed={FailedCount}.");
            }
        }

        private sealed class RuntimeFlatRecord
        {
            public string Id;
            public string ParentId;
            public string Tpl;
            public string SlotId;
            public string LocationJson;
            public string UpdJson;
            public int? StackCount;
            public readonly List<RuntimeFlatRecord> Children = new List<RuntimeFlatRecord>();

            public static RuntimeFlatRecord FromFlatItem(JsonType.FlatItem flatItem)
            {
                if (flatItem == null)
                {
                    return null;
                }

                var updJson = ToRawJson(flatItem.upd);
                return new RuntimeFlatRecord
                {
                    Id = flatItem._id.ToString(),
                    ParentId = flatItem.parentId.HasValue ? flatItem.parentId.Value.ToString() : string.Empty,
                    Tpl = flatItem._tpl.ToString(),
                    SlotId = flatItem.slotId ?? string.Empty,
                    LocationJson = ToRawJson(flatItem.location),
                    UpdJson = updJson,
                    StackCount = ReadStackCountFromUpd(updJson)
                };
            }

            private static string ToRawJson(UnparsedData value)
            {
                try
                {
                    return value?.JToken?.ToString(Newtonsoft.Json.Formatting.None);
                }
                catch
                {
                    return null;
                }
            }

            private static int? ReadStackCountFromUpd(string updJson)
            {
                if (string.IsNullOrWhiteSpace(updJson))
                {
                    return null;
                }

                try
                {
                    var token = JToken.Parse(updJson);
                    var stackToken = token["StackObjectsCount"];
                    var value = stackToken?.Value<int?>();
                    return value.HasValue && value.Value > 0 ? value.Value : (int?)null;
                }
                catch
                {
                    return null;
                }
            }
        }

        private sealed class FlatRecord
        {
            public string Id;
            public string ParentId;
            public string Tpl;
            public string SlotId;
            public string LocationJson;
            public string UpdJson;
        }
    }
}
