#region Plugin.cs
using System;
using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UltimateLootEditor.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ULE.SpawnEditor
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = ModConstants.ClientPluginGuid;
        public const string PluginName = ModConstants.ModDisplayName;
        public const string PluginVersion = ModConstants.Version;

        internal const int VisualizerBuildBudget = 24;
        internal const float VisualizerRenderDistance = 30f;
        internal const bool IncludeUnsafeSearchItems = false;
        internal const int SearchMaxRankedMatches = 200;

        internal static ConfigEntry<KeyCode> OpenEditorKey;
        internal static ConfigEntry<KeyCode> ToggleVizKey;
        internal static ConfigEntry<float> SphereScale;
        internal static ConfigEntry<float> SelectRadius;
        internal static ConfigEntry<float> SelectionGroupRadius;
        internal static ConfigEntry<bool> DrawWorldLabels;
        internal static ConfigEntry<bool> UseTarkovUIForEditor;

        internal static bool DebugLoggingEnabled => false;

        private GameObject _manager;
        private string _activeMapId;
        private float _nextAutoDetectPollAt;
        private float _nextBootstrapProbeLogAt;
        private int _activeRaidWorldMissCount;
        private static bool _isInitializing;
        private Harmony _harmony;
        private Coroutine _seasonMaterialWarmupCoroutine;

        private void Awake()
        {
            OpenEditorKey = Config.Bind("Input", "OpenEditorKey", KeyCode.Mouse4, MakeConfigDescription("Open the loot editor when near a sphere.", 100));
            ToggleVizKey = Config.Bind("Input", "ToggleVisualizer", KeyCode.F9, MakeConfigDescription("Toggle visibility of spawn spheres.", 90));
            SphereScale = Config.Bind("Visualizer", "SphereScale", 0.15f, MakeConfigDescription("Sphere diameter (m).", 80));
            SelectRadius = Config.Bind("Visualizer", "SelectRadius", 2.0f, MakeConfigDescription("Max distance to open editor (m).", 70));
            SelectionGroupRadius = Config.Bind("Visualizer", "SelectionGroupRadius", 5.0f, MakeConfigDescription("When a spawn is opened, nearby spawns within this radius can be cycled with the editor arrows.", 60));
            DrawWorldLabels = Config.Bind("Visualizer", "DrawWorldLabels", true, MakeConfigDescription("Draw labels above the nearest visible spheres.", 50));
            UseTarkovUIForEditor = Config.Bind("Editor", "UseTarkovUIForEditor", true, MakeConfigDescription("Enable this to use the Tarkov UI for the editor, or disable to use IMGUI", 40));

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");

            RemoveLegacyConfigEntries();
            DbPaths.CleanupPluginRuntimeArtifacts();
            DbPaths.EnsureAllMapEditFolders();
            TplCache.BuildIfMissing(Logger);

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TearDownManager();
            _activeRaidWorldMissCount = 0;
            _nextAutoDetectPollAt = Time.unscaledTime + 0.25f;
            _nextBootstrapProbeLogAt = Time.unscaledTime + 1f;
            LogDebug($"[ULE] Scene loaded: '{scene.name}' ({mode}). Waiting for raid world detection.");
        }

        private void Update()
        {
            try
            {
                PresetPreviewBridge.Tick();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[ULE] Preview bridge tick failed: {ex.Message}");
            }

            if (_isInitializing)
            {
                return;
            }

            if (_manager == null && Input.GetKeyDown(ToggleVizKey.Value))
            {
                LogDebug($"[ULE] ToggleVisualizer ({ToggleVizKey.Value}) pressed before manager bootstrap.");
                LogBootstrapProbe(force: true);
            }

            if (_manager != null)
            {
                if (Time.unscaledTime >= _nextAutoDetectPollAt)
                {
                    _nextAutoDetectPollAt = Time.unscaledTime + 1f;
                    if (!TryGetLiveRaidWorldMapId(out var liveMapId))
                    {
                        _activeRaidWorldMissCount++;
                        if (_activeRaidWorldMissCount >= 3)
                        {
                            LogDebug("[ULE] Raid world no longer detected. Releasing manager and in-memory loot data.");
                            TearDownManager();
                        }
                    }
                    else
                    {
                        _activeRaidWorldMissCount = 0;
                        if (!string.Equals(_activeMapId, liveMapId, StringComparison.OrdinalIgnoreCase))
                        {
                            LogDebug($"[ULE] Raid map changed from '{_activeMapId}' to '{liveMapId}'. Releasing current manager.");
                            TearDownManager();
                        }
                    }
                }

                return;
            }

            LogBootstrapProbe(force: false);

            if (Time.unscaledTime < _nextAutoDetectPollAt)
            {
                return;
            }

            _nextAutoDetectPollAt = Time.unscaledTime + 1f;

            if (!TryGetLiveRaidWorldMapId(out var detectedMapId))
            {
                if (_manager != null)
                {
                    TearDownManager();
                }

                return;
            }

            if (_manager == null || !string.Equals(_activeMapId, detectedMapId, StringComparison.OrdinalIgnoreCase))
            {
                _isInitializing = true;
                StartCoroutine(BootstrapMap(detectedMapId, $"[ULE] Auto-detected map '{detectedMapId}'."));
            }
        }

        private bool TryGetLiveRaidWorldMapId(out string mapId)
        {
            mapId = null;

            try
            {
                var world = Singleton<GameWorld>.Instantiated
                    ? Singleton<GameWorld>.Instance
                    : FindObjectOfType<GameWorld>();
                if (world != null)
                {
                    var detected = DbPaths.NormalizeMapId(world.LocationId);
                    if (!string.IsNullOrEmpty(detected))
                    {
                        mapId = detected;
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore timing issues during scene transitions and retry on the next tick.
            }

            return false;
        }

        private void LogBootstrapProbe(bool force)
        {
            if (!DebugLoggingEnabled)
            {
                return;
            }

            if (!force && Time.unscaledTime < _nextBootstrapProbeLogAt)
            {
                return;
            }

            _nextBootstrapProbeLogAt = Time.unscaledTime + 10f;

            string sceneName;
            try
            {
                var activeScene = SceneManager.GetActiveScene();
                sceneName = activeScene.IsValid() ? activeScene.name : "<invalid>";
            }
            catch
            {
                sceneName = "<unknown>";
            }

            bool singletonWorldInstantiated;
            GameWorld world;
            try
            {
                singletonWorldInstantiated = Singleton<GameWorld>.Instantiated;
                world = singletonWorldInstantiated
                    ? Singleton<GameWorld>.Instance
                    : FindObjectOfType<GameWorld>();
            }
            catch (Exception ex)
            {
                LogDebug($"[ULE] Bootstrap probe: scene='{sceneName}', world lookup failed: {ex.Message}");
                return;
            }

            var rawLocationId = world?.LocationId;
            var normalizedMapId = DbPaths.NormalizeMapId(rawLocationId);
            var nextPollDelay = Mathf.Max(0f, _nextAutoDetectPollAt - Time.unscaledTime);
            LogDebug(
                $"[ULE] Bootstrap probe: scene='{sceneName}', singletonWorld={singletonWorldInstantiated}, foundWorld={(world != null)}, " +
                $"locationId='{rawLocationId ?? "<null>"}', normalized='{normalizedMapId ?? "<null>"}', nextPollIn={nextPollDelay:0.00}s.");
        }

        private IEnumerator BootstrapMap(string mapId, string logLine)
        {
            _isInitializing = true;

            if (string.IsNullOrEmpty(mapId) || mapId == "off" || !DbPaths.IsKnownMapId(mapId))
            {
                _isInitializing = false;
                yield break;
            }

            try
            {
                LogDebug(logLine);

                if (_manager != null)
                {
                    Destroy(_manager);
                }

                _manager = new GameObject("ULE_Manager");
                DontDestroyOnLoad(_manager);

                var visualizer = _manager.AddComponent<SpawnVisualizer>();
                visualizer.Init(mapId, Logger, this);

                var gui = _manager.AddComponent<LootEditorGUI>();
                gui.Init(visualizer, Logger);

                var tarkovUi = _manager.AddComponent<TarkovLootEditorUI>();
                tarkovUi.Init(visualizer, Logger);

                _activeMapId = mapId;
                _activeRaidWorldMissCount = 0;
                Logger.LogInfo($"[ULE] Editor ready for map '{mapId}'.");

                if (_seasonMaterialWarmupCoroutine != null)
                {
                    StopCoroutine(_seasonMaterialWarmupCoroutine);
                }

                _seasonMaterialWarmupCoroutine = StartCoroutine(WarmRaidSeasonMaterialsForPreview(mapId));
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[ULE] Failed to bootstrap manager for map '{mapId}': {ex}");
                TearDownManager();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private IEnumerator WarmRaidSeasonMaterialsForPreview(string mapId)
        {
            const int maxAttempts = 20;
            const float initialDelaySeconds = 1.5f;
            const float retryDelaySeconds = 0.5f;

            yield return new WaitForSecondsRealtime(initialDelaySeconds);

            string lastState = null;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (_manager == null || !string.Equals(_activeMapId, mapId, StringComparison.OrdinalIgnoreCase))
                {
                    yield break;
                }

                if (!TryGetLiveRaidWorldMapId(out var liveMapId) ||
                    !string.Equals(liveMapId, mapId, StringComparison.OrdinalIgnoreCase))
                {
                    lastState = $"raidMap={(liveMapId ?? "<unavailable>")} expected={mapId}";
                    yield return new WaitForSecondsRealtime(retryDelaySeconds);
                    continue;
                }

                if (PresetPreviewBridge.TryWarmRaidSeasonMaterialsForPreview(out lastState))
                {
                    LogDebug($"[ULE] Warmed raid season-material preview repair in background after {attempt} attempt(s): {lastState}");
                    _seasonMaterialWarmupCoroutine = null;
                    yield break;
                }

                yield return new WaitForSecondsRealtime(retryDelaySeconds);
            }

            LogDebug($"[ULE] Skipped raid season-material preview warmup after {maxAttempts} attempts: {lastState ?? "<no-state>"}");
            _seasonMaterialWarmupCoroutine = null;
        }

        private void TearDownManager()
        {
            if (_seasonMaterialWarmupCoroutine != null)
            {
                StopCoroutine(_seasonMaterialWarmupCoroutine);
                _seasonMaterialWarmupCoroutine = null;
            }

            PresetPreviewBridge.ResetRaidVisualCaches();

            if (_manager != null)
            {
                Destroy(_manager);
            }

            _manager = null;
            _activeMapId = null;
            _activeRaidWorldMissCount = 0;
            LootEditorGUI.Open = false;
        }

        private void RemoveLegacyConfigEntries()
        {
            var removed = false;
            removed |= Config.Remove(new ConfigDefinition("General", "AutoDetectMap"));
            removed |= Config.Remove(new ConfigDefinition("General", "SelectedMap"));
            removed |= Config.Remove(new ConfigDefinition("General", "Reload Selected Map"));
            removed |= Config.Remove(new ConfigDefinition("Performance", "RenderDistance"));
            removed |= Config.Remove(new ConfigDefinition("Visualizer", "RenderDistance"));
            removed |= Config.Remove(new ConfigDefinition("Performance", "BuildBudget"));
            removed |= Config.Remove(new ConfigDefinition("Advanced", "ShowUnsafeItemsInSearch"));
            removed |= Config.Remove(new ConfigDefinition("Advanced", "SearchMaxRankedMatches"));
            removed |= Config.Remove(new ConfigDefinition("Advanced", "BlurPresetPreviewBackground"));
            removed |= Config.Remove(new ConfigDefinition("Input", "PreviewTarkovUiKey"));
            removed |= Config.Remove(new ConfigDefinition("Editor", "UseTarkovStyleEditor"));
            removed |= Config.Remove(new ConfigDefinition("Diagnostics", "DebugLogging"));

            if (removed)
            {
                Config.Save();
            }
        }

        private void LogDebug(string message)
        {
            if (DebugLoggingEnabled)
            {
                Logger.LogInfo(message);
            }
        }

        private static ConfigDescription MakeConfigDescription(string description, int order, bool advanced = false)
        {
            return new ConfigDescription(
                description,
                null,
                new ConfigurationManagerAttributes
                {
                    Order = order,
                    IsAdvanced = advanced
                });
        }
    }

    internal sealed class ConfigurationManagerAttributes
    {
        public int? Order { get; set; }
        public bool? IsAdvanced { get; set; }
    }
}
#endregion
