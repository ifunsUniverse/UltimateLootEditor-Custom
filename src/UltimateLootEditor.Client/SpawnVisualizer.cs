#region SpawnVisualizer.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.UI.Screens;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ULE.SpawnEditor
{
    internal class SpawnVisualizer : MonoBehaviour
    {
        private const float SpatialCellSize = 20f;
        private const int MaxWorldLabels = 10;
        private const float VisibilityRefreshInterval = 0.75f;
        private const float WorldLabelHeightOffset = 0.28f;
        private const int WorldLabelCanvasSortingOrder = -1000;
        private const int WorldLabelFontSize = 14;
        private const float WorldLabelScreenWidth = 180f;
        private const float WorldLabelScreenHeight = 22f;
        private const float WorldLabelScreenYOffset = 3f;
        private const float PlacementMoveSpeed = 1.0f;
        private const float PlacementFastMoveMultiplier = 5.0f;
        private const float PlacementSlowMoveMultiplier = 0.2f;
        private const float PlacementRotateSpeed = 45f;

        private List<SpawnPointData> _spawns = new List<SpawnPointData>();
        private readonly Dictionary<string, SpawnPointData> _spawnById = new Dictionary<string, SpawnPointData>(StringComparer.Ordinal);
        private readonly Dictionary<string, SpawnPointData> _vanillaSpawnById = new Dictionary<string, SpawnPointData>(StringComparer.Ordinal);
        private readonly Dictionary<Vector2Int, List<SpawnPointData>> _spatialBuckets = new Dictionary<Vector2Int, List<SpawnPointData>>();
        private readonly List<SpawnPointData> _nearbySpawns = new List<SpawnPointData>(128);
        private readonly List<SpawnPointData> _visibleSpawns = new List<SpawnPointData>(64);
        private readonly List<WorldLabelEntry> _worldLabels = new List<WorldLabelEntry>(MaxWorldLabels);
        private readonly List<string> _editorCandidateSpawnIds = new List<string>();
        private readonly Dictionary<string, SpawnPointData> _editorSessionDraftsById = new Dictionary<string, SpawnPointData>(StringComparer.Ordinal);
        private readonly HashSet<string> _editorSessionDirtySpawnIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _editorSessionCreatedSpawnIds = new HashSet<string>(StringComparer.Ordinal);
        private BepInEx.Logging.ManualLogSource _log;
        private string _mapId;
        private string _looseLootPath;
        private MapEdits _edits;
        private SpawnPointData _spawnClipboard;
        private string _spawnClipboardSourceId = string.Empty;
        private SpawnPointData _placementSpawn;
        private PresetPreviewBridge.WorldPreviewObject _itemPlacementPreview;
        private Coroutine _itemPlacementPreviewCoroutine;
        private Transform _itemPlacementPreviewPivot;
        private Transform _itemPlacementPreviewHolder;
        private Vector3 _itemPlacementPreviewLocalOffset = Vector3.zero;
        private Vector3 _itemPlacementPreviewPrefabLocalOffset = Vector3.zero;
        private string _itemPlacementPreviewKey = string.Empty;

        private bool _visible;
        private bool _spawnIndexReady;
        private string _spawnIndexLoadError = string.Empty;
        private float _nextVisibilityRefreshAt;
        private float _nextVisibilityHintAt;
        private Vector3 _lastVisibilityOrigin;
        private bool _hasLastVisibilityOrigin;

        private Task<SpawnIndexLoadResult> _pendingSpawnIndexLoadTask;
        private Task<SpawnDetailLoadResult> _pendingDetailLoadTask;
        private string _pendingDetailSpawnId = string.Empty;
        private string _activeSpawnLoadErrorSpawnId = string.Empty;
        private string _activeSpawnLoadError = string.Empty;
        private SpawnPointData _activeSourceSpawn;
        private bool _activeSpawnDirty;
        private readonly object _saveQueueLock = new object();
        private MapEdits _queuedSaveSnapshot;
        private Task _saveTask;
        private int _editorCandidateIndex = -1;
        private GameObject _worldLabelCanvasRoot;
        private RectTransform _worldLabelCanvasRect;
        private Font _worldLabelFont;

        public SpawnPointData ActiveSpawn { get; private set; }

        public bool ActiveSpawnHasUnsavedChanges => _activeSpawnDirty;

        public bool IsActiveSpawnLoading =>
            ActiveSpawn != null &&
            !ActiveSpawn.DetailsLoaded &&
            !string.IsNullOrWhiteSpace(_pendingDetailSpawnId) &&
            string.Equals(_pendingDetailSpawnId, ActiveSpawn.Id, StringComparison.Ordinal) &&
            _pendingDetailLoadTask != null;

        public bool IsActiveVanillaLoading =>
            ActiveSpawn != null &&
            !string.IsNullOrWhiteSpace(_pendingDetailSpawnId) &&
            string.Equals(_pendingDetailSpawnId, ActiveSpawn.Id, StringComparison.Ordinal) &&
            _pendingDetailLoadTask != null &&
            (ActiveVanillaSpawn == null || !ActiveVanillaSpawn.DetailsLoaded);

        public SpawnPointData ActiveVanillaSpawn
        {
            get
            {
                if (ActiveSpawn == null || string.IsNullOrWhiteSpace(ActiveSpawn.Id))
                {
                    return null;
                }

                return _vanillaSpawnById.TryGetValue(ActiveSpawn.Id, out var vanilla)
                    ? vanilla
                    : null;
            }
        }

        public string ActiveSpawnLoadError =>
            ActiveSpawn != null &&
            string.Equals(_activeSpawnLoadErrorSpawnId, ActiveSpawn.Id, StringComparison.Ordinal)
                ? _activeSpawnLoadError
                : string.Empty;

        public int EditorCandidateCount => _editorCandidateSpawnIds.Count;

        public int ActiveEditorCandidateIndex => _editorCandidateIndex;

        public bool HasSpawnClipboard => _spawnClipboard != null && _spawnClipboard.DetailsLoaded;

        public string SpawnClipboardSourceId => _spawnClipboardSourceId;

        public bool IsPlacementModeActive => _placementSpawn != null;

        public bool IsVisualizationVisible => _visible;

        public string ActiveItemPlacementPreviewKey => _itemPlacementPreviewKey;

        public bool CanNavigateEditorPrevious => _editorCandidateSpawnIds.Count > 1 && _editorCandidateIndex > 0;

        public bool CanNavigateEditorNext => _editorCandidateSpawnIds.Count > 1 && _editorCandidateIndex >= 0 && _editorCandidateIndex < _editorCandidateSpawnIds.Count - 1;

        public void Init(string mapId, BepInEx.Logging.ManualLogSource log, Plugin plugin)
        {
            _mapId = mapId;
            _log = log;
            _edits = SaveManager.LoadMapEdits(mapId);

            BeginLoadSpawnIndex();
            LogDebug($"[ULE] Spawn visualizer initialized for map '{mapId}'. Loose loot data loading in the background.");
        }

        private void OnDestroy()
        {
            var mapId = _mapId;
            FlushPendingMapEditsSave();

            ActiveSpawn = null;
            _activeSourceSpawn = null;
            _placementSpawn = null;
            _pendingSpawnIndexLoadTask = null;
            _pendingDetailLoadTask = null;
            _pendingDetailSpawnId = string.Empty;

            _spawns.Clear();
            _spawnById.Clear();
            _vanillaSpawnById.Clear();
            _spatialBuckets.Clear();
            _nearbySpawns.Clear();
            _visibleSpawns.Clear();
            _editorCandidateSpawnIds.Clear();
            _editorSessionDraftsById.Clear();
            _editorSessionDirtySpawnIds.Clear();
            _editorSessionCreatedSpawnIds.Clear();
            _spawnClipboard = null;
            _spawnClipboardSourceId = string.Empty;
            ClearItemPlacementPreview();
            DestroyWorldLabels();

            _spawnIndexReady = false;
            _looseLootPath = null;
            _mapId = null;

            if (!string.IsNullOrWhiteSpace(mapId))
            {
                LogDebug($"[ULE] Released in-memory loose loot data for map '{mapId}'.");
            }
        }

        private void Update()
        {
            PollPendingMapEditsSave();
            PollPendingSpawnIndexLoad();
            PollPendingDetailLoad();

            if (_spawnIndexReady &&
                !EditorWindowState.Open &&
                !IsAnyTextInputFocused() &&
                IsCreateSpawnPointPressed())
            {
                if (TryCreateSpawnPlacement(out var message))
                {
                    _log?.LogInfo($"[ULE] {message}");
                }
                else if (!string.IsNullOrWhiteSpace(message))
                {
                    _log?.LogWarning($"[ULE] {message}");
                }
            }

            if (_spawnIndexReady && _placementSpawn != null)
            {
                UpdatePlacementMode();
            }

            if (!_visible || !_spawnIndexReady)
            {
                return;
            }

            if (Time.unscaledTime >= _nextVisibilityRefreshAt)
            {
                _nextVisibilityRefreshAt = Time.unscaledTime + VisibilityRefreshInterval;
                RefreshVisibleSpawns(forceRefresh: false);
            }

            if (Input.GetMouseButtonDown(0))
            {
                TrySelectActiveSpawnFromMouse();
            }

            if (Input.GetKeyDown(Plugin.OpenEditorKey.Value))
            {
                if (_placementSpawn != null)
                {
                    OpenEditorForSpawn(_placementSpawn);
                    _placementSpawn = null;
                    return;
                }

                var near = FindBestAimedSpawnWithinRadius(GetReferencePosition(), Plugin.SelectRadius.Value);
                if (near != null)
                {
                    OpenEditorForSpawn(near);
                }
            }
        }

        public void SetVisualizationVisible(bool visible)
        {
            _visible = visible;
            _nextVisibilityRefreshAt = 0f;
            _nextVisibilityHintAt = 0f;

            if (_visible)
            {
                BeginLoadSpawnIndex();
                _log?.LogInfo(_spawnIndexReady
                    ? $"[ULE] Visualizer enabled. Range={GetEffectiveRenderDistance():0.#}m, max visible={GetVisibleSphereCap()}."
                    : "[ULE] Visualizer enabled. Loose loot data is still loading in the background.");
                RefreshVisibleSpawns(forceRefresh: true);
                return;
            }

            _visibleSpawns.Clear();
            _hasLastVisibilityOrigin = false;
            HideWorldLabels();
            _log?.LogInfo("[ULE] Visualizer disabled.");
        }

        private void LateUpdate()
        {
            if (!_spawnIndexReady)
            {
                return;
            }

            if (!_visible || _visibleSpawns.Count == 0)
            {
                HideWorldLabels();
                return;
            }

            var mesh = Util.GetOrCreateSphereMesh();
            if (mesh == null)
            {
                return;
            }

            var scale = Vector3.one * Mathf.Max(0.02f, Plugin.SphereScale.Value);
            if (_itemPlacementPreview != null)
            {
                PositionItemPlacementPreview();
            }

            if (_visible)
            {
                foreach (var spawn in _visibleSpawns)
                {
                    Graphics.DrawMesh(
                        mesh,
                        Matrix4x4.TRS(GetDisplayPosition(spawn), Quaternion.identity, scale),
                        GetRenderMaterial(spawn),
                        0);
                }
            }

            UpdateWorldLabels(GetActiveCamera());
        }

        public void CommitEdits(SpawnPointData edited)
        {
            if (edited == null)
            {
                return;
            }

            var target = ResolveSourceSpawnForEditor(edited);
            if (target == null)
            {
                return;
            }

            var previousPosition = target.Position;
            var targetIsActiveDraft = ReferenceEquals(target, edited);
            if (!targetIsActiveDraft)
            {
                target.CopyFrom(edited);
            }

            UpdateSpawnSpatialBucket(target, previousPosition);
            target.ItemCountSummary = target.Items?.Count ?? 0;
            target.DetailsLoaded = true;
            target.DataVersion++;

            var vanilla = GetVanillaSpawn(target.Id);
            if (!target.IsUserCreated && AreSpawnsEquivalent(target, vanilla))
            {
                _edits.BySpawnId.Remove(target.Id);
            }
            else
            {
                var se = new SpawnEdit
                {
                    SpawnChance = target.SpawnChance,
                    IsAlwaysSpawn = target.HasAlwaysSpawnFlag ? target.IsAlwaysSpawn : (bool?)null,
                    UseGravity = target.IsUserCreated || vanilla == null || target.UseGravity != vanilla.UseGravity
                        ? target.UseGravity
                        : (bool?)null,
                    IsCreated = target.IsUserCreated ? true : (bool?)null,
                    Name = string.IsNullOrWhiteSpace(target.Name) ? null : target.Name.Trim(),
                    Position = target.IsUserCreated || !AreVectorsClose(target.Position, vanilla?.Position ?? target.Position)
                        ? SavedVector3.FromUnity(target.Position)
                        : null,
                    Rotation = target.IsUserCreated || !AreVectorsClose(NormalizeEuler(target.Rotation), NormalizeEuler(vanilla?.Rotation ?? target.Rotation))
                        ? SavedVector3.FromUnity(NormalizeEuler(target.Rotation))
                        : null,
                    Items = targetIsActiveDraft ? CloneLootItemList(target.Items) : target.Items
                };

                _edits.BySpawnId[target.Id] = se;
            }

            QueueMapEditsSave(SaveManager.ShallowSnapshotMapEdits(_edits));
            _activeSourceSpawn = target;
            _activeSpawnDirty = false;
            if (_placementSpawn != null && string.Equals(_placementSpawn.Id, target.Id, StringComparison.Ordinal))
            {
                _placementSpawn = null;
            }

            _editorSessionCreatedSpawnIds.Remove(target.Id);
            _editorSessionDirtySpawnIds.Remove(target.Id);
        }

        private void QueueMapEditsSave(MapEdits snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            lock (_saveQueueLock)
            {
                _queuedSaveSnapshot = snapshot;
                if (_saveTask == null)
                {
                    StartNextMapEditsSaveLocked();
                }
            }
        }

        private void StartNextMapEditsSaveLocked()
        {
            if (_queuedSaveSnapshot == null)
            {
                return;
            }

            var snapshot = _queuedSaveSnapshot;
            _queuedSaveSnapshot = null;
            _saveTask = Task.Run(() => SaveManager.SaveMapEdits(snapshot));
        }

        private void PollPendingMapEditsSave()
        {
            Task finishedTask = null;

            lock (_saveQueueLock)
            {
                if (_saveTask == null || !_saveTask.IsCompleted)
                {
                    return;
                }

                finishedTask = _saveTask;
                _saveTask = null;

                if (_queuedSaveSnapshot != null)
                {
                    StartNextMapEditsSaveLocked();
                }
            }

            LogMapEditsSaveFailure(finishedTask);
        }

        private void FlushPendingMapEditsSave()
        {
            while (true)
            {
                Task activeTask;
                lock (_saveQueueLock)
                {
                    if (_saveTask == null)
                    {
                        if (_queuedSaveSnapshot == null)
                        {
                            return;
                        }

                        StartNextMapEditsSaveLocked();
                    }

                    activeTask = _saveTask;
                }

                if (activeTask == null)
                {
                    return;
                }

                try
                {
                    activeTask.Wait(2000);
                }
                catch
                {
                    // Failure is logged below after the task transitions to a terminal state.
                }

                if (!activeTask.IsCompleted)
                {
                    return;
                }

                PollPendingMapEditsSave();
            }
        }

        private void LogMapEditsSaveFailure(Task task)
        {
            if (task == null || !task.IsFaulted)
            {
                return;
            }

            var message = task.Exception?.GetBaseException()?.Message;
            _log?.LogWarning($"[ULE] Failed to save map edits: {message ?? "unknown error"}");
        }

        public void CloseEditorWithoutSaving()
        {
            ClearItemPlacementPreview();
            _placementSpawn = null;
            RemoveUnsavedSessionCreatedSpawns();
            ResetEditorCandidateGroup();
            ResetEditorDraftSession();
            ActiveSpawn = null;
            _activeSourceSpawn = null;
            _activeSpawnDirty = false;
        }

        public void MarkActiveUnsaved()
        {
            _activeSpawnDirty = true;
            if (ActiveSpawn != null && !string.IsNullOrWhiteSpace(ActiveSpawn.Id))
            {
                _editorSessionDirtySpawnIds.Add(ActiveSpawn.Id);
            }
        }

        public bool RevertActiveToVanilla()
        {
            if (ActiveSpawn == null)
            {
                return false;
            }

            var vanilla = GetVanillaSpawn(ActiveSpawn.Id);
            if (vanilla == null || !vanilla.DetailsLoaded)
            {
                return false;
            }

            ActiveSpawn.CopyFrom(vanilla);
            ActiveSpawn.DataVersion++;
            _activeSpawnDirty = true;
            _editorSessionDirtySpawnIds.Add(ActiveSpawn.Id);
            return true;
        }

        public bool UpdateActiveSpawnName(string name)
        {
            if (ActiveSpawn == null || !ActiveSpawn.IsUserCreated || !ActiveSpawn.DetailsLoaded)
            {
                return false;
            }

            var normalized = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
            if (string.Equals(ActiveSpawn.Name ?? string.Empty, normalized, StringComparison.Ordinal))
            {
                return false;
            }

            ActiveSpawn.Name = normalized;
            ActiveSpawn.DataVersion++;
            MarkActiveUnsaved();
            return true;
        }

        public bool UpdateActiveSpawnPosition(Vector3 position)
        {
            if (ActiveSpawn == null || !ActiveSpawn.DetailsLoaded)
            {
                return false;
            }

            if (AreVectorsClose(ActiveSpawn.Position, position))
            {
                return false;
            }

            ActiveSpawn.Position = position;
            ActiveSpawn.DataVersion++;
            MarkActiveUnsaved();
            PositionItemPlacementPreview();
            return true;
        }

        public bool UpdateActiveSpawnRotation(Vector3 rotation)
        {
            if (ActiveSpawn == null || !ActiveSpawn.DetailsLoaded)
            {
                return false;
            }

            var normalized = NormalizeEuler(rotation);
            if (AreVectorsClose(NormalizeEuler(ActiveSpawn.Rotation), normalized))
            {
                return false;
            }

            ActiveSpawn.Rotation = normalized;
            ActiveSpawn.DataVersion++;
            MarkActiveUnsaved();
            PositionItemPlacementPreview();
            return true;
        }

        public bool UpdateActiveSpawnUseGravity(bool useGravity)
        {
            if (ActiveSpawn == null || !ActiveSpawn.DetailsLoaded)
            {
                return false;
            }

            if (ActiveSpawn.UseGravity == useGravity)
            {
                return false;
            }

            ActiveSpawn.UseGravity = useGravity;
            ActiveSpawn.DataVersion++;
            MarkActiveUnsaved();
            return true;
        }

        public bool PreviewActiveSpawnItem(LootItem item, out string message)
        {
            message = string.Empty;
            if (ActiveSpawn == null || !ActiveSpawn.DetailsLoaded)
            {
                message = "Open a loaded spawn point before previewing an item.";
                return false;
            }

            if (!PresetPreviewBridge.CanCreateWorldPreview(item))
            {
                message = "This item cannot be previewed in the world.";
                return false;
            }

            var previewKey = BuildItemPlacementPreviewKey(item);
            if (!string.IsNullOrWhiteSpace(previewKey) &&
                string.Equals(_itemPlacementPreviewKey, previewKey, StringComparison.Ordinal))
            {
                ClearItemPlacementPreview();
                message = "Item preview hidden.";
                return true;
            }

            ClearItemPlacementPreview();
            _itemPlacementPreviewKey = previewKey;
            _itemPlacementPreviewCoroutine = StartCoroutine(CreateItemPlacementPreviewCoroutine(item.Clone(), ActiveSpawn.Id, previewKey));
            message = "Loading item preview at the spawn point.";
            return true;
        }

        public bool IsPreviewingActiveSpawnItem(LootItem item)
        {
            var previewKey = BuildItemPlacementPreviewKey(item);
            return !string.IsNullOrWhiteSpace(previewKey) &&
                   string.Equals(_itemPlacementPreviewKey, previewKey, StringComparison.Ordinal);
        }

        public bool NavigateEditorSelection(int direction)
        {
            if (_editorCandidateSpawnIds.Count <= 1 || direction == 0)
            {
                return false;
            }

            var nextIndex = _editorCandidateIndex + Math.Sign(direction);
            if (nextIndex < 0 || nextIndex >= _editorCandidateSpawnIds.Count)
            {
                return false;
            }

            StoreActiveEditorDraft();

            var nextId = _editorCandidateSpawnIds[nextIndex];
            if (string.IsNullOrWhiteSpace(nextId) || !_spawnById.TryGetValue(nextId, out var nextSource))
            {
                return false;
            }

            _editorCandidateIndex = nextIndex;
            ActivateEditorSpawn(nextSource);
            return true;
        }

        public void RetryActiveSpawnDetails()
        {
            if (_activeSourceSpawn == null)
            {
                return;
            }

            BeginLoadSpawnDetails(_activeSourceSpawn, forceReload: true);
        }

        public void EnsureActiveVanillaDetailsLoaded()
        {
            if (_activeSourceSpawn == null ||
                ActiveSpawn == null ||
                string.IsNullOrWhiteSpace(ActiveSpawn.Id))
            {
                return;
            }

            var vanilla = GetVanillaSpawn(ActiveSpawn.Id);
            if (vanilla != null && vanilla.DetailsLoaded)
            {
                return;
            }

            BeginLoadSpawnDetails(_activeSourceSpawn, forceReload: true);
        }

        public bool TryCreateSpawnPlacement(out string message)
        {
            message = string.Empty;
            if (!_spawnIndexReady)
            {
                message = "Loose loot spawn data is still loading.";
                return false;
            }

            var position = GetSpawnCreationPosition();
            var rotation = GetSpawnCreationRotation();
            var id = $"ULE Spawn [{Guid.NewGuid():D}]";
            var spawn = new SpawnPointData
            {
                Id = id,
                DetailKey = id,
                Name = "New Spawn",
                Position = position,
                Rotation = rotation,
                SpawnChance = 1f,
                HasAlwaysSpawnFlag = true,
                IsAlwaysSpawn = true,
                UseGravity = true,
                ItemCountSummary = 0,
                DetailsLoaded = true,
                DataVersion = 1,
                IsUserCreated = true,
                Items = new List<LootItem>()
            };

            if (_placementSpawn != null && _placementSpawn.IsUserCreated)
            {
                RemoveSpawnById(_placementSpawn.Id);
                _editorSessionCreatedSpawnIds.Remove(_placementSpawn.Id);
                _editorSessionDirtySpawnIds.Remove(_placementSpawn.Id);
            }

            StoreActiveEditorDraft();
            RegisterSpawn(spawn, includeVanilla: false);
            _editorSessionCreatedSpawnIds.Add(spawn.Id);
            _editorSessionDirtySpawnIds.Add(spawn.Id);

            _placementSpawn = spawn;
            _visible = true;
            RefreshVisibleSpawns(forceRefresh: true);

            message = "New spawn point placement started. Move it with arrow keys, / down, and ' up; press the open editor key when ready.";
            return true;
        }

        public bool CopyActiveSpawnToClipboard(out string message)
        {
            message = string.Empty;
            if (ActiveSpawn == null || !ActiveSpawn.DetailsLoaded)
            {
                message = "Spawn details must be loaded before copying.";
                return false;
            }

            _spawnClipboard = ActiveSpawn.Clone();
            _spawnClipboardSourceId = ActiveSpawn.Id ?? string.Empty;
            message = string.IsNullOrWhiteSpace(_spawnClipboardSourceId)
                ? "Copied spawn data."
                : $"Copied spawn data from {_spawnClipboardSourceId}.";
            return true;
        }

        public bool PasteClipboardToActiveSpawn(out string message)
        {
            message = string.Empty;
            if (!TryValidateSpawnClipboardTarget("pasting", out message))
            {
                return false;
            }

            var pastedItems = CloneLootItemList(_spawnClipboard.Items, regenerateComposedKeys: true);
            if (pastedItems.Count == 0)
            {
                message = "Copied spawn has no items to paste.";
                return false;
            }

            ActiveSpawn.Items = ActiveSpawn.Items ?? new List<LootItem>();
            ActiveSpawn.Items.AddRange(pastedItems);
            ActiveSpawn.ItemCountSummary = ActiveSpawn.Items.Count;
            ActiveSpawn.DetailsLoaded = true;
            ActiveSpawn.DataVersion++;

            MarkActiveUnsaved();
            message = string.IsNullOrWhiteSpace(_spawnClipboardSourceId)
                ? $"Pasted {pastedItems.Count} copied item(s)."
                : $"Pasted {pastedItems.Count} item(s) from {_spawnClipboardSourceId}.";
            return true;
        }

        public bool ReplaceActiveSpawnWithClipboard(out string message)
        {
            message = string.Empty;
            if (!TryValidateSpawnClipboardTarget("replacing", out message))
            {
                return false;
            }

            var targetId = ActiveSpawn.Id;
            var targetDetailKey = ActiveSpawn.DetailKey;
            var targetName = ActiveSpawn.Name;
            var targetPosition = ActiveSpawn.Position;
            var targetRotation = ActiveSpawn.Rotation;
            var targetIsCreated = ActiveSpawn.IsUserCreated;

            ActiveSpawn.SpawnChance = _spawnClipboard.SpawnChance;
            ActiveSpawn.HasAlwaysSpawnFlag = _spawnClipboard.HasAlwaysSpawnFlag;
            ActiveSpawn.IsAlwaysSpawn = _spawnClipboard.IsAlwaysSpawn;
            ActiveSpawn.UseGravity = _spawnClipboard.UseGravity;
            ActiveSpawn.Items = CloneLootItemList(_spawnClipboard.Items, regenerateComposedKeys: true);
            ActiveSpawn.ItemCountSummary = ActiveSpawn.Items.Count;
            ActiveSpawn.DetailsLoaded = true;
            ActiveSpawn.DataVersion++;
            ActiveSpawn.Id = targetId;
            ActiveSpawn.DetailKey = targetDetailKey;
            ActiveSpawn.Name = targetName;
            ActiveSpawn.Position = targetPosition;
            ActiveSpawn.Rotation = targetRotation;
            ActiveSpawn.IsUserCreated = targetIsCreated;

            MarkActiveUnsaved();
            message = string.IsNullOrWhiteSpace(_spawnClipboardSourceId)
                ? "Replaced spawn data with copied spawn data."
                : $"Replaced spawn data with data from {_spawnClipboardSourceId}.";
            return true;
        }

        private bool TryValidateSpawnClipboardTarget(string action, out string message)
        {
            message = string.Empty;
            if (!HasSpawnClipboard)
            {
                message = "No copied spawn data is available.";
                return false;
            }

            if (ActiveSpawn == null || !ActiveSpawn.DetailsLoaded)
            {
                message = $"Target spawn details must be loaded before {action}.";
                return false;
            }

            return true;
        }

        private void BeginLoadSpawnIndex()
        {
            if (_spawnIndexReady || _pendingSpawnIndexLoadTask != null || string.IsNullOrWhiteSpace(_mapId))
            {
                return;
            }

            var mapId = _mapId;
            _spawnIndexLoadError = string.Empty;

            _pendingSpawnIndexLoadTask = Task.Run(() =>
            {
                try
                {
                    if (RuntimeLooseLootProvider.TryLoadSpawnIndex(mapId, null, out var runtimeSpawns, out var runtimeSource, out var runtimeError))
                    {
                        return new SpawnIndexLoadResult
                        {
                            Spawns = runtimeSpawns,
                            SpatialBuckets = BuildSpatialBuckets(runtimeSpawns),
                            SourceLabel = runtimeSource
                        };
                    }

                    var sourcePath = DbPaths.ResolveLooseLootPath(mapId, null);
                    if (string.IsNullOrWhiteSpace(sourcePath))
                    {
                        return new SpawnIndexLoadResult
                        {
                            Error = string.IsNullOrWhiteSpace(runtimeError)
                                ? $"looseLoot.json was not found for map '{mapId}'."
                                : $"Runtime loose loot unavailable ({runtimeError}); looseLoot.json was not found for map '{mapId}'."
                        };
                    }

                    var spawns = LooseLootParser.LoadSpawnIndex(mapId, sourcePath, null);
                    return new SpawnIndexLoadResult
                    {
                        Spawns = spawns,
                        SpatialBuckets = BuildSpatialBuckets(spawns),
                        SourceLabel = sourcePath,
                        Warning = runtimeError
                    };
                }
                catch (Exception ex)
                {
                    return new SpawnIndexLoadResult
                    {
                        Error = ex.ToString()
                    };
                }
            });
        }

        private void PollPendingSpawnIndexLoad()
        {
            if (_pendingSpawnIndexLoadTask == null || !_pendingSpawnIndexLoadTask.IsCompleted)
            {
                return;
            }

            var finishedTask = _pendingSpawnIndexLoadTask;
            _pendingSpawnIndexLoadTask = null;

            SpawnIndexLoadResult result;
            try
            {
                result = finishedTask.Result;
            }
            catch (Exception ex)
            {
                result = new SpawnIndexLoadResult { Error = ex.ToString() };
            }

            if (result?.Spawns == null)
            {
                _spawnIndexLoadError = string.IsNullOrWhiteSpace(result?.Error)
                    ? "Failed to load the spawn index."
                    : result.Error;
                _log?.LogWarning($"[ULE] {_spawnIndexLoadError}");
                return;
            }

            _spawns = result.Spawns;
            _spawnById.Clear();
            _vanillaSpawnById.Clear();
            foreach (var spawn in _spawns)
            {
                if (spawn == null || string.IsNullOrWhiteSpace(spawn.Id))
                {
                    continue;
                }

                _spawnById[spawn.Id] = spawn;
                _vanillaSpawnById[spawn.Id] = spawn.Clone();
            }

            _spatialBuckets.Clear();
            foreach (var kv in result.SpatialBuckets)
            {
                _spatialBuckets[kv.Key] = kv.Value;
            }

            ApplySavedEditsOntoParsed();
            _spawnIndexReady = true;
            _spawnIndexLoadError = string.Empty;
            _looseLootPath = result.SourceLabel != null && result.SourceLabel.EndsWith("looseLoot.json", StringComparison.OrdinalIgnoreCase)
                ? result.SourceLabel
                : _looseLootPath;

            if (!string.IsNullOrWhiteSpace(result.Warning))
            {
                LogDebug($"[ULE] Runtime loose loot route was unavailable; using disk looseLoot.json fallback. Reason: {result.Warning}");
            }

            var sourceSuffix = string.IsNullOrWhiteSpace(result.SourceLabel)
                ? string.Empty
                : $" Source={result.SourceLabel}.";
            _log?.LogInfo($"[ULE] Loaded {_spawns.Count} spawn points for map '{_mapId}'.{sourceSuffix}");

            if (_visible)
            {
                RefreshVisibleSpawns(forceRefresh: true);
            }
        }

        private void ApplySavedEditsOntoParsed()
        {
            if (_edits?.BySpawnId == null || _edits.BySpawnId.Count == 0)
            {
                return;
            }

            foreach (var pair in _edits.BySpawnId)
            {
                var spawnId = pair.Key;
                var edit = pair.Value;
                if (string.IsNullOrWhiteSpace(spawnId) ||
                    edit?.IsCreated != true ||
                    _spawnById.ContainsKey(spawnId))
                {
                    continue;
                }

                var created = new SpawnPointData
                {
                    Id = spawnId,
                    DetailKey = spawnId,
                    Name = edit.Name,
                    Position = edit.Position != null ? edit.Position.ToUnity() : Vector3.zero,
                    Rotation = edit.Rotation != null ? edit.Rotation.ToUnity() : Vector3.zero,
                    SpawnChance = edit.SpawnChance.HasValue ? Mathf.Clamp01(edit.SpawnChance.Value) : 1f,
                    HasAlwaysSpawnFlag = true,
                    IsAlwaysSpawn = edit.IsAlwaysSpawn ?? true,
                    UseGravity = edit.UseGravity ?? true,
                    ItemCountSummary = edit.Items?.Count ?? 0,
                    DetailsLoaded = true,
                    DataVersion = 1,
                    IsUserCreated = true,
                    Items = CloneLootItemList(edit.Items)
                };

                RegisterSpawn(created, includeVanilla: false);
            }

            foreach (var spawn in _spawns)
            {
                if (spawn == null || !_edits.BySpawnId.TryGetValue(spawn.Id, out var edit) || edit == null)
                {
                    continue;
                }

                if (edit.SpawnChance.HasValue)
                {
                    spawn.SpawnChance = Mathf.Clamp01(edit.SpawnChance.Value);
                    if (spawn.HasAlwaysSpawnFlag && !edit.IsAlwaysSpawn.HasValue)
                    {
                        spawn.IsAlwaysSpawn = spawn.SpawnChance >= 0.999f;
                    }
                }

                if (edit.IsAlwaysSpawn.HasValue)
                {
                    spawn.HasAlwaysSpawnFlag = true;
                    spawn.IsAlwaysSpawn = edit.IsAlwaysSpawn.Value;
                }

                if (edit.UseGravity.HasValue)
                {
                    spawn.UseGravity = edit.UseGravity.Value;
                    spawn.DataVersion++;
                }

                if (!string.IsNullOrWhiteSpace(edit.Name))
                {
                    spawn.Name = edit.Name.Trim();
                    spawn.DataVersion++;
                }

                if (edit.Position != null)
                {
                    spawn.Position = edit.Position.ToUnity();
                    spawn.DataVersion++;
                }

                if (edit.Rotation != null)
                {
                    spawn.Rotation = NormalizeEuler(edit.Rotation.ToUnity());
                    spawn.DataVersion++;
                }

                if (edit.Items != null)
                {
                    spawn.Items = CloneLootItemList(edit.Items);

                    spawn.ItemCountSummary = spawn.Items.Count;
                    spawn.DetailsLoaded = true;
                    spawn.DataVersion++;
                }
            }

            RebuildSpatialBuckets();
        }

        private void OpenEditorForSpawn(SpawnPointData spawn)
        {
            ResetEditorDraftSession();
            BuildEditorCandidateGroup(spawn);
            ActivateEditorSpawn(spawn);
            EditorWindowState.Open = true;
        }

        private void BeginLoadSpawnDetails(SpawnPointData spawn, bool forceReload)
        {
            if (spawn == null || string.IsNullOrWhiteSpace(_mapId))
            {
                return;
            }

            if (spawn.DetailsLoaded && !forceReload)
            {
                return;
            }

            if (_pendingDetailLoadTask != null && !_pendingDetailLoadTask.IsCompleted)
            {
                if (string.Equals(_pendingDetailSpawnId, spawn.Id, StringComparison.Ordinal))
                {
                    return;
                }

                return;
            }

            _pendingDetailSpawnId = spawn.Id;
            _activeSpawnLoadErrorSpawnId = string.Empty;
            _activeSpawnLoadError = string.Empty;

            var mapId = _mapId;
            var spawnId = spawn.Id;
            var sourcePath = _looseLootPath;
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                sourcePath = DbPaths.ResolveLooseLootPath(mapId, _log);
                _looseLootPath = sourcePath;
            }

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                _activeSpawnLoadErrorSpawnId = spawnId;
                _activeSpawnLoadError = $"looseLoot.json was not found for map '{mapId}'.";
                _log?.LogWarning($"[ULE] {_activeSpawnLoadError}");
                return;
            }

            _pendingDetailLoadTask = Task.Run(() =>
            {
                if (LooseLootParser.TryLoadSpawnDetails(sourcePath, spawnId, out var loaded, out var error))
                {
                    return new SpawnDetailLoadResult { Spawn = loaded };
                }

                return new SpawnDetailLoadResult
                {
                    Error = string.IsNullOrWhiteSpace(error)
                        ? $"Failed to load spawn details for '{spawnId}'."
                        : error
                };
            });
        }

        private void PollPendingDetailLoad()
        {
            if (_pendingDetailLoadTask == null || !_pendingDetailLoadTask.IsCompleted)
            {
                return;
            }

            var finishedTask = _pendingDetailLoadTask;
            var requestedSpawnId = _pendingDetailSpawnId;

            _pendingDetailLoadTask = null;
            _pendingDetailSpawnId = string.Empty;

            SpawnDetailLoadResult result;
            try
            {
                result = finishedTask.Result;
            }
            catch (Exception ex)
            {
                result = new SpawnDetailLoadResult { Error = ex.Message };
            }

            if (result?.Spawn != null)
            {
                ApplyLoadedSpawnDetails(result.Spawn);
                return;
            }

            if (!string.IsNullOrWhiteSpace(requestedSpawnId))
            {
                _activeSpawnLoadErrorSpawnId = requestedSpawnId;
                _activeSpawnLoadError = string.IsNullOrWhiteSpace(result?.Error)
                    ? $"Failed to load spawn details for '{requestedSpawnId}'."
                    : result.Error;
                _log?.LogWarning($"[ULE] {_activeSpawnLoadError}");
            }
        }

        private void ApplyLoadedSpawnDetails(SpawnPointData loaded)
        {
            if (loaded == null || string.IsNullOrWhiteSpace(loaded.Id))
            {
                return;
            }

            if (_vanillaSpawnById.TryGetValue(loaded.Id, out var vanillaTarget))
            {
                vanillaTarget.CopyFrom(loaded);
            }
            else
            {
                _vanillaSpawnById[loaded.Id] = loaded.Clone();
            }

            if (!_spawnById.TryGetValue(loaded.Id, out var target))
            {
                target = loaded;
                _spawns.Add(target);
                _spawnById[loaded.Id] = target;
            }

            if (_edits?.BySpawnId != null && _edits.BySpawnId.ContainsKey(loaded.Id))
            {
                return;
            }

            var previousPosition = target.Position;
            target.Position = loaded.Position;
            target.Rotation = loaded.Rotation;
            target.SpawnChance = loaded.SpawnChance;
            target.UseGravity = loaded.UseGravity;
            target.Items = CloneLootItemList(loaded.Items);
            target.ItemCountSummary = loaded.ItemCountSummary;
            target.DetailsLoaded = true;
            target.DataVersion++;
            UpdateSpawnSpatialBucket(target, previousPosition);

            if (_activeSourceSpawn != null &&
                string.Equals(_activeSourceSpawn.Id, target.Id, StringComparison.Ordinal) &&
                (!_activeSpawnDirty || ActiveSpawn == null || !ActiveSpawn.DetailsLoaded))
            {
                ActiveSpawn = target.Clone();
                _activeSourceSpawn = target;
                _activeSpawnDirty = false;
            }
        }

        private SpawnPointData ResolveSourceSpawnForEditor(SpawnPointData edited)
        {
            if (_activeSourceSpawn != null &&
                edited != null &&
                string.Equals(_activeSourceSpawn.Id, edited.Id, StringComparison.Ordinal))
            {
                return _activeSourceSpawn;
            }

            if (edited != null &&
                !string.IsNullOrWhiteSpace(edited.Id) &&
                _spawnById.TryGetValue(edited.Id, out var known))
            {
                return known;
            }

            return edited;
        }

        private SpawnPointData GetVanillaSpawn(string spawnId)
        {
            if (string.IsNullOrWhiteSpace(spawnId))
            {
                return null;
            }

            return _vanillaSpawnById.TryGetValue(spawnId, out var vanilla)
                ? vanilla
                : null;
        }

        private void ActivateEditorSpawn(SpawnPointData sourceSpawn)
        {
            if (sourceSpawn == null)
            {
                return;
            }

            if (ActiveSpawn == null || !string.Equals(ActiveSpawn.Id, sourceSpawn.Id, StringComparison.Ordinal))
            {
                ClearItemPlacementPreview();
            }

            _activeSourceSpawn = sourceSpawn;
            ActiveSpawn = GetEditorDraftOrClone(sourceSpawn);
            _activeSpawnDirty = ActiveSpawn != null &&
                !string.IsNullOrWhiteSpace(ActiveSpawn.Id) &&
                _editorSessionDirtySpawnIds.Contains(ActiveSpawn.Id);
            _activeSpawnLoadErrorSpawnId = string.Empty;
            _activeSpawnLoadError = string.Empty;

            var needsEditedDetails = !sourceSpawn.DetailsLoaded;

            if (needsEditedDetails)
            {
                BeginLoadSpawnDetails(sourceSpawn, forceReload: false);
            }
        }

        private void BuildEditorCandidateGroup(SpawnPointData selectedSpawn)
        {
            ResetEditorCandidateGroup();
            if (selectedSpawn == null)
            {
                return;
            }

            var radius = Mathf.Max(Plugin.SelectionGroupRadius.Value, 0.5f);
            CollectNearbySpawns(selectedSpawn.Position, radius, _nearbySpawns);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var camera = GetActiveCamera();
            var selectedId = selectedSpawn.Id ?? string.Empty;

            var ordered = _nearbySpawns
                .Where(spawn => spawn != null && !string.IsNullOrWhiteSpace(spawn.Id) && seen.Add(spawn.Id))
                .OrderBy(spawn => string.Equals(spawn.Id, selectedId, StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(spawn => GetScreenSortX(camera, spawn))
                .ThenByDescending(spawn => GetScreenSortY(camera, spawn))
                .ThenBy(spawn => (spawn.Position - selectedSpawn.Position).sqrMagnitude)
                .ThenBy(spawn => spawn.Id, StringComparer.Ordinal)
                .ToList();

            if (!ordered.Any(spawn => string.Equals(spawn.Id, selectedId, StringComparison.Ordinal)))
            {
                ordered.Insert(0, selectedSpawn);
            }

            _editorCandidateSpawnIds.AddRange(ordered.Select(spawn => spawn.Id));
            _editorCandidateIndex = _editorCandidateSpawnIds.FindIndex(id => string.Equals(id, selectedId, StringComparison.Ordinal));
            if (_editorCandidateIndex < 0)
            {
                _editorCandidateIndex = 0;
            }
        }

        private void ResetEditorCandidateGroup()
        {
            _editorCandidateSpawnIds.Clear();
            _editorCandidateIndex = -1;
        }

        private void ResetEditorDraftSession()
        {
            _editorSessionDraftsById.Clear();
            _editorSessionDirtySpawnIds.Clear();
            _activeSpawnDirty = false;
        }

        private void StoreActiveEditorDraft()
        {
            if (ActiveSpawn == null || string.IsNullOrWhiteSpace(ActiveSpawn.Id))
            {
                return;
            }

            _editorSessionDraftsById[ActiveSpawn.Id] = ActiveSpawn.Clone();
            if (_activeSpawnDirty)
            {
                _editorSessionDirtySpawnIds.Add(ActiveSpawn.Id);
            }
            else
            {
                _editorSessionDirtySpawnIds.Remove(ActiveSpawn.Id);
            }
        }

        private IEnumerator CreateItemPlacementPreviewCoroutine(LootItem item, string spawnId, string previewKey)
        {
            var task = PresetPreviewBridge.CreateWorldPreviewObjectAsync(item, _log);
            while (task != null && !task.IsCompleted)
            {
                yield return null;
            }

            _itemPlacementPreviewCoroutine = null;

            PresetPreviewBridge.WorldPreviewObject preview = null;
            try
            {
                preview = task?.Result;
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to load item placement preview: {ex.GetBaseException().Message}");
                yield break;
            }

            if (preview == null || !preview.Succeeded)
            {
                _log?.LogWarning($"[ULE] {preview?.Error ?? "Failed to load item placement preview."}");
                if (string.Equals(_itemPlacementPreviewKey, previewKey, StringComparison.Ordinal))
                {
                    ClearItemPlacementPreview();
                }

                yield break;
            }

            if (ActiveSpawn == null ||
                !string.Equals(ActiveSpawn.Id, spawnId, StringComparison.Ordinal) ||
                !string.Equals(_itemPlacementPreviewKey, previewKey, StringComparison.Ordinal))
            {
                preview.Release();
                yield break;
            }

            _itemPlacementPreview = preview;
            ConfigureItemPlacementPreview(preview.Prefab);
            PositionItemPlacementPreview();
        }

        private void ConfigureItemPlacementPreview(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            prefab.name = "ULE_ItemPlacementPreview";
            if (_itemPlacementPreviewPivot == null)
            {
                var pivotObject = new GameObject("ULE_ItemPlacementPreviewPivot");
                pivotObject.transform.SetParent(transform, worldPositionStays: false);
                _itemPlacementPreviewPivot = pivotObject.transform;
            }

            if (_itemPlacementPreviewHolder == null)
            {
                var holderObject = new GameObject("ULE_ItemPlacementPreviewHolder");
                holderObject.transform.SetParent(_itemPlacementPreviewPivot, worldPositionStays: false);
                _itemPlacementPreviewHolder = holderObject.transform;
            }

            _itemPlacementPreviewHolder.localPosition = Vector3.zero;
            _itemPlacementPreviewHolder.localRotation = Quaternion.identity;
            _itemPlacementPreviewHolder.localScale = Vector3.one;

            prefab.transform.SetParent(_itemPlacementPreviewHolder, worldPositionStays: false);
            prefab.transform.localPosition = Vector3.zero;
            prefab.transform.localRotation = Quaternion.identity;
            prefab.transform.localScale = Vector3.one;
            prefab.SetActive(true);

            _itemPlacementPreviewLocalOffset = Vector3.zero;
            _itemPlacementPreviewPrefabLocalOffset = CalculatePrefabLocalPivotOffset(prefab);

            _itemPlacementPreviewHolder.localPosition = _itemPlacementPreviewLocalOffset;
            prefab.transform.localPosition = _itemPlacementPreviewPrefabLocalOffset;

            foreach (var collider in prefab.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }

            foreach (var rigidbody in prefab.GetComponentsInChildren<Rigidbody>(true))
            {
                rigidbody.isKinematic = true;
                rigidbody.useGravity = false;
            }

        }

        private static Vector3 CalculatePrefabLocalPivotOffset(GameObject prefab)
        {
            if (prefab == null)
            {
                return Vector3.zero;
            }

            var previewPivot = prefab.GetComponent<PreviewPivot>();
            if (previewPivot != null && IsFinite(previewPivot.pivotPosition))
            {
                return -previewPivot.pivotPosition;
            }

            return CalculatePreviewLocalCenterOffset(prefab);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static Vector3 CalculatePreviewLocalCenterOffset(GameObject prefab)
        {
            var root = prefab != null ? prefab.transform : null;
            if (root == null)
            {
                return Vector3.zero;
            }

            if (!TryGetRendererLocalBounds(root, out var bounds))
            {
                return Vector3.zero;
            }

            return -bounds.center;
        }

        private static bool TryGetRendererLocalBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null)
            {
                return false;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!renderer.enabled)
                {
                    continue;
                }

                if (!renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var rendererTypeName = renderer.GetType().Name;
                if (rendererTypeName.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                var localBounds = renderer.localBounds;
                var min = localBounds.min;
                var max = localBounds.max;
                var corners = new[]
                {
                    new Vector3(min.x, min.y, min.z),
                    new Vector3(min.x, min.y, max.z),
                    new Vector3(min.x, max.y, min.z),
                    new Vector3(min.x, max.y, max.z),
                    new Vector3(max.x, min.y, min.z),
                    new Vector3(max.x, min.y, max.z),
                    new Vector3(max.x, max.y, min.z),
                    new Vector3(max.x, max.y, max.z),
                };

                foreach (var corner in corners)
                {
                    var localPoint = root.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (!hasBounds)
                    {
                        bounds = new Bounds(localPoint, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(localPoint);
                    }
                }
            }

            return hasBounds;
        }

        private void PositionItemPlacementPreview()
        {
            var prefab = _itemPlacementPreview?.Prefab;
            if (prefab == null || ActiveSpawn == null || _itemPlacementPreviewPivot == null || _itemPlacementPreviewHolder == null)
            {
                return;
            }

            _itemPlacementPreviewPivot.position = ActiveSpawn.Position;
            _itemPlacementPreviewPivot.rotation = Quaternion.Euler(NormalizeEuler(ActiveSpawn.Rotation));

            _itemPlacementPreviewHolder.localPosition = _itemPlacementPreviewLocalOffset;
            _itemPlacementPreviewHolder.localRotation = Quaternion.identity;
            _itemPlacementPreviewHolder.localScale = Vector3.one;
            prefab.transform.localPosition = _itemPlacementPreviewPrefabLocalOffset;
            prefab.transform.localRotation = Quaternion.identity;
            prefab.transform.localScale = Vector3.one;
        }

        private void ClearItemPlacementPreview()
        {
            if (_itemPlacementPreviewCoroutine != null)
            {
                StopCoroutine(_itemPlacementPreviewCoroutine);
                _itemPlacementPreviewCoroutine = null;
            }

            if (_itemPlacementPreview == null)
            {
                _itemPlacementPreviewKey = string.Empty;
                _itemPlacementPreviewLocalOffset = Vector3.zero;
                _itemPlacementPreviewPrefabLocalOffset = Vector3.zero;
                if (_itemPlacementPreviewPivot != null)
                {
                    Destroy(_itemPlacementPreviewPivot.gameObject);
                    _itemPlacementPreviewPivot = null;
                    _itemPlacementPreviewHolder = null;
                }

                return;
            }

            _itemPlacementPreview.Release();
            _itemPlacementPreview = null;
            _itemPlacementPreviewKey = string.Empty;
            _itemPlacementPreviewLocalOffset = Vector3.zero;
            _itemPlacementPreviewPrefabLocalOffset = Vector3.zero;
            if (_itemPlacementPreviewPivot != null)
            {
                Destroy(_itemPlacementPreviewPivot.gameObject);
                _itemPlacementPreviewPivot = null;
                _itemPlacementPreviewHolder = null;
            }
        }

        private SpawnPointData GetEditorDraftOrClone(SpawnPointData sourceSpawn)
        {
            if (sourceSpawn == null)
            {
                return null;
            }

            if (_editorSessionDraftsById.TryGetValue(sourceSpawn.Id, out var existingDraft))
            {
                if (_editorSessionDirtySpawnIds.Contains(sourceSpawn.Id))
                {
                    return existingDraft.Clone();
                }

                if (!existingDraft.DetailsLoaded && sourceSpawn.DetailsLoaded)
                {
                    var refreshed = sourceSpawn.Clone();
                    _editorSessionDraftsById[sourceSpawn.Id] = refreshed.Clone();
                    return refreshed;
                }

                return existingDraft.Clone();
            }

            return sourceSpawn.Clone();
        }

        private static bool AreSpawnsEquivalent(SpawnPointData left, SpawnPointData right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (Math.Abs(left.SpawnChance - right.SpawnChance) > 0.0001f)
            {
                return false;
            }

            if (!AreVectorsClose(left.Position, right.Position) ||
                !AreVectorsClose(NormalizeEuler(left.Rotation), NormalizeEuler(right.Rotation)))
            {
                return false;
            }

            if ((left.HasAlwaysSpawnFlag || right.HasAlwaysSpawnFlag) &&
                left.IsAlwaysSpawn != right.IsAlwaysSpawn)
            {
                return false;
            }

            if (left.UseGravity != right.UseGravity)
            {
                return false;
            }

            var leftItems = left.Items ?? new List<LootItem>();
            var rightItems = right.Items ?? new List<LootItem>();
            if (leftItems.Count != rightItems.Count)
            {
                return false;
            }

            for (var i = 0; i < leftItems.Count; i++)
            {
                var leftItem = leftItems[i];
                var rightItem = rightItems[i];
                if (leftItem == null || rightItem == null)
                {
                    if (!ReferenceEquals(leftItem, rightItem))
                    {
                        return false;
                    }

                    continue;
                }

                if (!string.Equals(leftItem.Tpl, rightItem.Tpl, StringComparison.Ordinal) ||
                    !string.Equals(leftItem.ComposedKey, rightItem.ComposedKey, StringComparison.Ordinal) ||
                    Math.Abs(leftItem.Weight - rightItem.Weight) > 0.0001f ||
                    !string.Equals(leftItem.PresetId, rightItem.PresetId, StringComparison.Ordinal) ||
                    !string.Equals(leftItem.PresetName, rightItem.PresetName, StringComparison.Ordinal) ||
                    !AreLootItemNodesEquivalent(leftItem, rightItem))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<LootItem> CloneLootItemList(IEnumerable<LootItem> items)
        {
            return CloneLootItemList(items, regenerateComposedKeys: false);
        }

        private static List<LootItem> CloneLootItemList(IEnumerable<LootItem> items, bool regenerateComposedKeys)
        {
            var clones = items?.Select(item => item?.Clone())
                .Where(item => item != null)
                .ToList() ?? new List<LootItem>();

            if (regenerateComposedKeys)
            {
                foreach (var item in clones)
                {
                    item.ComposedKey = Util.GenerateComposedKey();
                }
            }

            return clones;
        }

        private static bool AreLootItemNodesEquivalent(LootItemNode left, LootItemNode right)
        {
            if (left == null || right == null)
            {
                return ReferenceEquals(left, right);
            }

            if (!string.Equals(left.Tpl, right.Tpl, StringComparison.Ordinal) ||
                !string.Equals(left.SlotId, right.SlotId, StringComparison.Ordinal) ||
                !string.Equals(left.LocationJson, right.LocationJson, StringComparison.Ordinal) ||
                !string.Equals(left.UpdJson, right.UpdJson, StringComparison.Ordinal) ||
                left.StackMin != right.StackMin ||
                left.StackMax != right.StackMax)
            {
                return false;
            }

            return AreLootItemNodeListsEquivalent(left.Children, right.Children);
        }

        private static bool AreLootItemNodeListsEquivalent(List<LootItemNode> left, List<LootItemNode> right)
        {
            var leftNodes = left ?? new List<LootItemNode>();
            var rightNodes = right ?? new List<LootItemNode>();
            if (leftNodes.Count != rightNodes.Count)
            {
                return false;
            }

            for (var i = 0; i < leftNodes.Count; i++)
            {
                if (!AreLootItemNodesEquivalent(leftNodes[i], rightNodes[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private void RefreshVisibleSpawns(bool forceRefresh)
        {
            if (!_visible || !_spawnIndexReady)
            {
                return;
            }

            var origin = GetReferencePosition();
            if (!forceRefresh &&
                _hasLastVisibilityOrigin &&
                (origin - _lastVisibilityOrigin).sqrMagnitude < 1f)
            {
                return;
            }

            _lastVisibilityOrigin = origin;
            _hasLastVisibilityOrigin = true;
            _visibleSpawns.Clear();
            var maxDistance = GetEffectiveRenderDistance();
            CollectNearbySpawns(origin, maxDistance, _nearbySpawns);

            if (_nearbySpawns.Count > 1)
            {
                _nearbySpawns.Sort((left, right) =>
                    (left.Position - origin).sqrMagnitude.CompareTo((right.Position - origin).sqrMagnitude));
            }

            var cap = Mathf.Min(GetVisibleSphereCap(), _nearbySpawns.Count);
            for (int i = 0; i < cap; i++)
            {
                _visibleSpawns.Add(_nearbySpawns[i]);
            }

            if (_visibleSpawns.Count == 0 && Time.unscaledTime >= _nextVisibilityHintAt)
            {
                _nextVisibilityHintAt = Time.unscaledTime + 5f;
                var nearestDistance = GetNearestSpawnDistance(origin);
                var nearestSuffix = nearestDistance >= 0f
                    ? $" Nearest indexed spawn is {nearestDistance:0.#}m away."
                    : string.Empty;
                if (forceRefresh)
                {
                    _log?.LogInfo($"[ULE] Visualizer refresh: visible=0, range={maxDistance:0.#}m.{nearestSuffix}");
                }
                else
                {
                    LogDebug($"[ULE] Visualizer is enabled, but no spheres are within the current range of {maxDistance:0.#}m.{nearestSuffix}");
                }
            }
            else if (forceRefresh)
            {
                _log?.LogInfo($"[ULE] Visualizer refresh: visible={_visibleSpawns.Count}, range={maxDistance:0.#}m.");
            }
        }

        private void LogDebug(string message)
        {
            if (Plugin.DebugLoggingEnabled)
            {
                _log?.LogInfo(message);
            }
        }

        private int GetVisibleSphereCap()
        {
            return Mathf.Clamp(Plugin.VisualizerBuildBudget, 1, 64);
        }

        private float GetEffectiveRenderDistance()
        {
            return Plugin.VisualizerRenderDistance;
        }

        private Vector3 GetReferencePosition()
        {
            if (TryGetMainPlayerPosition(out var playerPosition))
            {
                return playerPosition;
            }

            var activeCamera = GetActiveCamera();
            return activeCamera != null ? activeCamera.transform.position : Vector3.zero;
        }

        private static bool TryGetMainPlayerPosition(out Vector3 position)
        {
            position = Vector3.zero;

            try
            {
                var world = Singleton<GameWorld>.Instantiated
                    ? Singleton<GameWorld>.Instance
                    : FindObjectOfType<GameWorld>();
                var player = world?.MainPlayer;
                if (player == null)
                {
                    return false;
                }

                position = player.Position;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Camera GetActiveCamera()
        {
            try
            {
                if (EFT.CameraControl.CameraManager.Exist)
                {
                    var battleCamera = EFT.CameraControl.CameraManager.Instance.Camera;
                    if (battleCamera != null && battleCamera.isActiveAndEnabled)
                    {
                        return battleCamera;
                    }

                    var opticCamera = EFT.CameraControl.CameraManager.Instance.OpticCameraManager?.Camera;
                    if (opticCamera != null && opticCamera.isActiveAndEnabled)
                    {
                        return opticCamera;
                    }
                }
            }
            catch
            {
                // Ignore camera-manager timing issues and fall back to Unity camera discovery.
            }

            if (Camera.main != null && Camera.main.isActiveAndEnabled)
            {
                return Camera.main;
            }

            var cameras = Camera.allCameras;
            if (cameras == null)
            {
                return null;
            }

            foreach (var camera in cameras)
            {
                if (camera == null || !camera.isActiveAndEnabled)
                {
                    continue;
                }

                return camera;
            }

            return null;
        }

        private Material GetRenderMaterial(SpawnPointData spawn)
        {
            if (IsItemPlacementPreviewForSpawn(spawn))
            {
                var opacity = Mathf.Clamp01(Plugin.ItemPreviewSphereOpacity?.Value ?? 0.08f);
                return Util.GetOrCreateSphereMaterial(
                    new Color(Util.Cyan.r, Util.Cyan.g, Util.Cyan.b, opacity),
                    $"ULE_Sphere_ItemPreview_{opacity:0.###}");
            }

            if (_placementSpawn != null && string.Equals(_placementSpawn.Id, spawn.Id, StringComparison.Ordinal))
            {
                return Util.GetOrCreateSphereMaterial(Util.QuestOrange, "ULE_Sphere_Placement");
            }

            if (ActiveSpawn != null && string.Equals(ActiveSpawn.Id, spawn.Id, StringComparison.Ordinal))
            {
                return Util.GetOrCreateSphereMaterial(Util.Cyan, "ULE_Sphere_Cyan");
            }

            if (_edits?.BySpawnId != null && _edits.BySpawnId.ContainsKey(spawn.Id))
            {
                return Util.GetOrCreateSphereMaterial(Util.Green, "ULE_Sphere_Green");
            }

            return Util.GetOrCreateSphereMaterial(Util.Yellow, "ULE_Sphere_Yellow");
        }

        private Vector3 GetDisplayPosition(SpawnPointData spawn)
        {
            if (spawn == null)
            {
                return Vector3.zero;
            }

            if (ActiveSpawn != null && string.Equals(ActiveSpawn.Id, spawn.Id, StringComparison.Ordinal))
            {
                return ActiveSpawn.Position;
            }

            if (_placementSpawn != null && string.Equals(_placementSpawn.Id, spawn.Id, StringComparison.Ordinal))
            {
                return _placementSpawn.Position;
            }

            return spawn.Position;
        }

        private void UpdatePlacementMode()
        {
            if (_placementSpawn == null)
            {
                return;
            }

            if (!_spawnById.ContainsKey(_placementSpawn.Id))
            {
                _placementSpawn = null;
                return;
            }

            EnsurePlacementSpawnVisible();

            if (IsAnyTextInputFocused())
            {
                return;
            }

            var dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            var moveSpeed = PlacementMoveSpeed;
            var rotateSpeed = PlacementRotateSpeed;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                moveSpeed *= PlacementFastMoveMultiplier;
                rotateSpeed *= PlacementFastMoveMultiplier;
            }
            else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                moveSpeed *= PlacementSlowMoveMultiplier;
                rotateSpeed *= PlacementSlowMoveMultiplier;
            }

            var delta = GetPlacementMoveDirection() * moveSpeed * dt;
            if (delta.sqrMagnitude > 0.0000001f)
            {
                var nextPosition = _placementSpawn.Position + delta;
                if (ReferenceEquals(_placementSpawn, ActiveSpawn))
                {
                    _placementSpawn.Position = nextPosition;
                    _placementSpawn.DataVersion++;
                    MarkActiveUnsaved();
                    PositionItemPlacementPreview();
                }
                else
                {
                    MoveIndexedSpawn(_placementSpawn, nextPosition);
                    if (ActiveSpawn != null && string.Equals(ActiveSpawn.Id, _placementSpawn.Id, StringComparison.Ordinal))
                    {
                        ActiveSpawn.Position = _placementSpawn.Position;
                        ActiveSpawn.DataVersion++;
                        MarkActiveUnsaved();
                        PositionItemPlacementPreview();
                    }
                }
            }

            var yawDirection = 0f;
            if (Input.GetKey(KeyCode.Q))
            {
                yawDirection -= 1f;
            }

            if (Input.GetKey(KeyCode.E))
            {
                yawDirection += 1f;
            }

            if (Mathf.Abs(yawDirection) > 0.001f)
            {
                _placementSpawn.Rotation = NormalizeEuler(_placementSpawn.Rotation + new Vector3(0f, yawDirection * rotateSpeed * dt, 0f));
                if (ActiveSpawn != null && string.Equals(ActiveSpawn.Id, _placementSpawn.Id, StringComparison.Ordinal))
                {
                    ActiveSpawn.Rotation = _placementSpawn.Rotation;
                    ActiveSpawn.DataVersion++;
                    MarkActiveUnsaved();
                    PositionItemPlacementPreview();
                }
            }
        }

        private Vector3 GetPlacementMoveDirection()
        {
            var activeCamera = GetActiveCamera();
            var forward = Vector3.forward;
            var right = Vector3.right;
            if (activeCamera != null)
            {
                forward = Vector3.ProjectOnPlane(activeCamera.transform.forward, Vector3.up);
                right = Vector3.ProjectOnPlane(activeCamera.transform.right, Vector3.up);
                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = Vector3.forward;
                }

                if (right.sqrMagnitude < 0.0001f)
                {
                    right = Vector3.right;
                }

                forward.Normalize();
                right.Normalize();
            }

            var direction = Vector3.zero;
            if (Input.GetKey(KeyCode.UpArrow))
            {
                direction += forward;
            }

            if (Input.GetKey(KeyCode.DownArrow))
            {
                direction -= forward;
            }

            if (Input.GetKey(KeyCode.RightArrow))
            {
                direction += right;
            }

            if (Input.GetKey(KeyCode.LeftArrow))
            {
                direction -= right;
            }

            if (Input.GetKey(KeyCode.Slash))
            {
                direction -= Vector3.up;
            }

            if (Input.GetKey(KeyCode.Quote))
            {
                direction += Vector3.up;
            }

            return direction.sqrMagnitude > 1f ? direction.normalized : direction;
        }

        private void EnsurePlacementSpawnVisible()
        {
            if (_placementSpawn == null)
            {
                return;
            }

            if (!_visibleSpawns.Any(spawn => spawn != null && string.Equals(spawn.Id, _placementSpawn.Id, StringComparison.Ordinal)))
            {
                _visibleSpawns.Add(_placementSpawn);
            }
        }

        private bool IsItemPlacementPreviewForSpawn(SpawnPointData spawn)
        {
            return spawn != null &&
                   _itemPlacementPreview != null &&
                   ActiveSpawn != null &&
                   string.Equals(ActiveSpawn.Id, spawn.Id, StringComparison.Ordinal);
        }

        private string BuildItemPlacementPreviewKey(LootItem item)
        {
            if (ActiveSpawn == null || item == null)
            {
                return string.Empty;
            }

            var itemKey = !string.IsNullOrWhiteSpace(item.ComposedKey)
                ? item.ComposedKey
                : item.Tpl ?? string.Empty;
            return string.IsNullOrWhiteSpace(itemKey)
                ? string.Empty
                : $"{ActiveSpawn.Id}|{itemKey}";
        }

        private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            if (root == null)
            {
                return false;
            }

            var hasBounds = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        private bool TrySelectActiveSpawnFromMouse()
        {
            if (!_visible ||
                !_spawnIndexReady ||
                ActiveSpawn == null ||
                string.IsNullOrWhiteSpace(ActiveSpawn.Id) ||
                IsAnyTextInputFocused())
            {
                return false;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return false;
            }

            var activeCamera = GetActiveCamera();
            if (activeCamera == null)
            {
                return false;
            }

            var ray = activeCamera.ScreenPointToRay(Input.mousePosition);
            var activePosition = ActiveSpawn.Position;
            var sphereRadius = Mathf.Max(0.08f, Plugin.SphereScale.Value * 0.5f);
            var toCenter = activePosition - ray.origin;
            var alongRay = Vector3.Dot(toCenter, ray.direction);
            if (alongRay < 0f)
            {
                return false;
            }

            var closest = ray.origin + ray.direction * alongRay;
            if ((closest - activePosition).sqrMagnitude > sphereRadius * sphereRadius)
            {
                return false;
            }

            _placementSpawn = ActiveSpawn;
            EnsurePlacementSpawnVisible();
            return true;
        }

        private static bool IsCreateSpawnPointPressed()
        {
            return IsKeyboardShortcutDown(Plugin.CreateSpawnPointKey, KeyCode.Mouse3);
        }

        private static bool IsKeyboardShortcutDown(ConfigEntry<KeyboardShortcut> entry, KeyCode fallback)
        {
            if (entry == null)
            {
                return Input.GetKeyDown(fallback);
            }

            var shortcut = entry.Value;
            return shortcut.MainKey != KeyCode.None && shortcut.IsDown();
        }

        private static bool IsAnyTextInputFocused()
        {
            if (GUIUtility.keyboardControl != 0)
            {
                return true;
            }

            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected == null)
            {
                return false;
            }

            var tmpInput = selected.GetComponent<TMP_InputField>() ?? selected.GetComponentInParent<TMP_InputField>();
            if (tmpInput != null && tmpInput.isFocused)
            {
                return true;
            }

            var input = selected.GetComponent<InputField>() ?? selected.GetComponentInParent<InputField>();
            return input != null && input.isFocused;
        }

        private SpawnPointData FindBestAimedSpawnWithinRadius(Vector3 pos, float radius)
        {
            if (!_spawnIndexReady)
            {
                return null;
            }

            var activeCamera = GetActiveCamera();
            if (activeCamera == null)
            {
                return FindNearestWithinRadius(pos, radius);
            }

            var clampedRadius = Mathf.Max(radius, 0.5f);
            CollectNearbySpawns(pos, clampedRadius, _nearbySpawns);

            SpawnPointData best = null;
            var bestScore = float.MaxValue;
            var radiusSqr = clampedRadius * clampedRadius;

            foreach (var spawn in _nearbySpawns)
            {
                var viewport = activeCamera.WorldToViewportPoint(spawn.Position);
                if (viewport.z <= 0f)
                {
                    continue;
                }

                var toSpawn = spawn.Position - activeCamera.transform.position;
                if (toSpawn.sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                var centerDelta = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
                var centerScore = centerDelta.sqrMagnitude;
                var distanceScore = toSpawn.sqrMagnitude / Mathf.Max(radiusSqr, 0.0001f);
                var verticalScore = Mathf.Abs(spawn.Position.y - pos.y) * 0.02f;
                var totalScore = centerScore * 8f + distanceScore * 0.5f + verticalScore;

                if (totalScore < bestScore)
                {
                    bestScore = totalScore;
                    best = spawn;
                }
            }

            return best ?? FindNearestWithinRadius(pos, clampedRadius);
        }

        private SpawnPointData FindNearestWithinRadius(Vector3 pos, float radius)
        {
            if (!_spawnIndexReady)
            {
                return null;
            }

            CollectNearbySpawns(pos, Mathf.Max(radius, 0.5f), _nearbySpawns);

            SpawnPointData best = null;
            var bestDistance = radius * radius;

            foreach (var spawn in _nearbySpawns)
            {
                var distance = (spawn.Position - pos).sqrMagnitude;
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = spawn;
                }
            }

            return best;
        }

        private static float GetScreenSortX(Camera camera, SpawnPointData spawn)
        {
            if (camera == null || spawn == null)
            {
                return 0f;
            }

            var viewport = camera.WorldToViewportPoint(spawn.Position);
            return viewport.z > 0f ? viewport.x : 0.5f;
        }

        private static float GetScreenSortY(Camera camera, SpawnPointData spawn)
        {
            if (camera == null || spawn == null)
            {
                return 0f;
            }

            var viewport = camera.WorldToViewportPoint(spawn.Position);
            return viewport.z > 0f ? viewport.y : 0.5f;
        }

        private void OnGUI()
        {
            if (_visible && !_spawnIndexReady)
            {
                var message = string.IsNullOrWhiteSpace(_spawnIndexLoadError)
                    ? "Ultimate Loot Editor: loading loose loot data..."
                    : $"Ultimate Loot Editor: failed to load loose loot data - {_spawnIndexLoadError}";
                GUI.Box(new Rect(20f, 20f, 420f, 28f), message);
            }
        }

        private void UpdateWorldLabels(Camera activeCamera)
        {
            if (!_visible ||
                !_spawnIndexReady ||
                !Plugin.DrawWorldLabels.Value ||
                IsRaidPauseMenuOpen() ||
                activeCamera == null ||
                _visibleSpawns.Count == 0)
            {
                HideWorldLabels();
                return;
            }

            EnsureWorldLabelPool();

            var labelsDrawn = 0;
            foreach (var spawn in _visibleSpawns)
            {
                if (labelsDrawn >= MaxWorldLabels)
                {
                    break;
                }

                var position = GetDisplayPosition(spawn) + Vector3.up * WorldLabelHeightOffset;
                var screen = activeCamera.WorldToScreenPoint(position);
                if (screen.z <= 0f)
                {
                    continue;
                }

                var entry = _worldLabels[labelsDrawn];
                entry.Text.text = GetSpawnLabel(spawn);
                entry.Rect.anchoredPosition = new Vector2(screen.x, screen.y + WorldLabelScreenYOffset);
                entry.Root.SetActive(true);
                labelsDrawn++;
            }

            for (var i = labelsDrawn; i < _worldLabels.Count; i++)
            {
                _worldLabels[i].Root.SetActive(false);
            }
        }

        private void EnsureWorldLabelPool()
        {
            if (_worldLabelFont == null)
            {
                _worldLabelFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            if (_worldLabelCanvasRoot == null)
            {
                _worldLabelCanvasRoot = new GameObject("ULE_WorldSpawnLabelCanvas");
                _worldLabelCanvasRoot.transform.SetParent(transform, false);
                var uiLayer = LayerMask.NameToLayer("UI");
                if (uiLayer >= 0)
                {
                    _worldLabelCanvasRoot.layer = uiLayer;
                }

                var canvas = _worldLabelCanvasRoot.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = WorldLabelCanvasSortingOrder;

                _worldLabelCanvasRect = _worldLabelCanvasRoot.transform as RectTransform;
            }

            while (_worldLabels.Count < MaxWorldLabels)
            {
                _worldLabels.Add(CreateWorldLabel());
            }
        }

        private WorldLabelEntry CreateWorldLabel()
        {
            var root = new GameObject("ULE_WorldSpawnLabel");
            root.transform.SetParent(_worldLabelCanvasRoot.transform, false);
            root.layer = _worldLabelCanvasRoot.layer;

            var rect = root.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(WorldLabelScreenWidth, WorldLabelScreenHeight);

            var text = root.AddComponent<Text>();
            text.font = _worldLabelFont;
            text.fontSize = WorldLabelFontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = string.Empty;

            root.SetActive(false);
            return new WorldLabelEntry(root, rect, text);
        }

        private void HideWorldLabels()
        {
            foreach (var label in _worldLabels)
            {
                if (label.Root != null)
                {
                    label.Root.SetActive(false);
                }
            }
        }

        private void DestroyWorldLabels()
        {
            foreach (var label in _worldLabels)
            {
                if (label.Root != null)
                {
                    Destroy(label.Root);
                }
            }

            _worldLabels.Clear();

            if (_worldLabelCanvasRoot != null)
            {
                Destroy(_worldLabelCanvasRoot);
                _worldLabelCanvasRoot = null;
                _worldLabelCanvasRect = null;
            }
        }

        private static bool IsRaidPauseMenuOpen()
        {
            try
            {
                var current = EFT.UI.Screens.EftScreenManager.Instance?.CurrentBaseScreenController;
                return current != null && current.ScreenType == EEftScreenType.MainMenu;
            }
            catch
            {
                return false;
            }
        }

        private string GetSpawnLabel(SpawnPointData spawn)
        {
            var spawnId = spawn?.Id ?? string.Empty;
            var label = !string.IsNullOrWhiteSpace(spawn?.Name) ? spawn.Name : spawnId;
            var bracketIndex = label.IndexOf('[');
            if (bracketIndex > 0)
            {
                label = label.Substring(0, bracketIndex).Trim();
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                label = "Spawn";
            }

            if (_edits?.BySpawnId != null && _edits.BySpawnId.ContainsKey(spawnId))
            {
                label = "* " + label;
            }

            return label;
        }

        private static Dictionary<Vector2Int, List<SpawnPointData>> BuildSpatialBuckets(List<SpawnPointData> spawns)
        {
            var buckets = new Dictionary<Vector2Int, List<SpawnPointData>>();
            if (spawns == null)
            {
                return buckets;
            }

            foreach (var spawn in spawns)
            {
                var key = GetBucketKey(spawn.Position);
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new List<SpawnPointData>();
                    buckets[key] = bucket;
                }

                bucket.Add(spawn);
            }

            return buckets;
        }

        private void RegisterSpawn(SpawnPointData spawn, bool includeVanilla)
        {
            if (spawn == null || string.IsNullOrWhiteSpace(spawn.Id))
            {
                return;
            }

            if (!_spawnById.ContainsKey(spawn.Id))
            {
                _spawns.Add(spawn);
            }

            _spawnById[spawn.Id] = spawn;
            if (includeVanilla)
            {
                _vanillaSpawnById[spawn.Id] = spawn.Clone();
            }

            var bucketKey = GetBucketKey(spawn.Position);
            if (!_spatialBuckets.TryGetValue(bucketKey, out var bucket))
            {
                bucket = new List<SpawnPointData>();
                _spatialBuckets[bucketKey] = bucket;
            }

            if (!bucket.Any(existing => string.Equals(existing?.Id, spawn.Id, StringComparison.Ordinal)))
            {
                bucket.Add(spawn);
            }
        }

        private void MoveIndexedSpawn(SpawnPointData spawn, Vector3 position)
        {
            if (spawn == null)
            {
                return;
            }

            var previousPosition = spawn.Position;
            spawn.Position = position;
            UpdateSpawnSpatialBucket(spawn, previousPosition);
        }

        private void UpdateSpawnSpatialBucket(SpawnPointData spawn, Vector3 previousPosition)
        {
            if (spawn == null || string.IsNullOrWhiteSpace(spawn.Id))
            {
                return;
            }

            var previousKey = GetBucketKey(previousPosition);
            var nextKey = GetBucketKey(spawn.Position);
            if (previousKey == nextKey)
            {
                if (_spatialBuckets.TryGetValue(nextKey, out var existingBucket) &&
                    !existingBucket.Any(candidate => string.Equals(candidate?.Id, spawn.Id, StringComparison.Ordinal)))
                {
                    existingBucket.Add(spawn);
                }

                return;
            }

            if (_spatialBuckets.TryGetValue(previousKey, out var previousBucket))
            {
                previousBucket.RemoveAll(candidate => candidate == null || string.Equals(candidate.Id, spawn.Id, StringComparison.Ordinal));
            }

            if (!_spatialBuckets.TryGetValue(nextKey, out var nextBucket))
            {
                nextBucket = new List<SpawnPointData>();
                _spatialBuckets[nextKey] = nextBucket;
            }

            if (!nextBucket.Any(candidate => string.Equals(candidate?.Id, spawn.Id, StringComparison.Ordinal)))
            {
                nextBucket.Add(spawn);
            }
        }

        private void RebuildSpatialBuckets()
        {
            _spatialBuckets.Clear();
            foreach (var kv in BuildSpatialBuckets(_spawns))
            {
                _spatialBuckets[kv.Key] = kv.Value;
            }
        }

        private void RemoveSpawnById(string spawnId)
        {
            if (string.IsNullOrWhiteSpace(spawnId))
            {
                return;
            }

            if (!_spawnById.TryGetValue(spawnId, out var spawn))
            {
                return;
            }

            _spawnById.Remove(spawnId);
            _vanillaSpawnById.Remove(spawnId);
            _spawns.Remove(spawn);

            foreach (var bucket in _spatialBuckets.Values)
            {
                bucket.RemoveAll(candidate => candidate == null || string.Equals(candidate.Id, spawnId, StringComparison.Ordinal));
            }

            _editorSessionDraftsById.Remove(spawnId);
            _editorSessionDirtySpawnIds.Remove(spawnId);
        }

        private void RemoveUnsavedSessionCreatedSpawns()
        {
            if (_editorSessionCreatedSpawnIds.Count == 0)
            {
                return;
            }

            var createdIds = _editorSessionCreatedSpawnIds.ToList();
            foreach (var spawnId in createdIds)
            {
                if (_edits?.BySpawnId != null &&
                    _edits.BySpawnId.TryGetValue(spawnId, out var savedEdit) &&
                    savedEdit?.IsCreated == true)
                {
                    continue;
                }

                RemoveSpawnById(spawnId);
            }

            _editorSessionCreatedSpawnIds.Clear();
        }

        private static Vector2Int GetBucketKey(Vector3 position)
        {
            return new Vector2Int(
                Mathf.FloorToInt(position.x / SpatialCellSize),
                Mathf.FloorToInt(position.z / SpatialCellSize));
        }

        private void CollectNearbySpawns(Vector3 origin, float radius, List<SpawnPointData> buffer)
        {
            buffer.Clear();

            if (_spatialBuckets.Count == 0)
            {
                return;
            }

            var radiusSqr = radius * radius;
            var min = origin - new Vector3(radius, 0f, radius);
            var max = origin + new Vector3(radius, 0f, radius);
            var minKey = GetBucketKey(min);
            var maxKey = GetBucketKey(max);

            for (int x = minKey.x; x <= maxKey.x; x++)
            {
                for (int y = minKey.y; y <= maxKey.y; y++)
                {
                    if (!_spatialBuckets.TryGetValue(new Vector2Int(x, y), out var bucket))
                    {
                        continue;
                    }

                    foreach (var spawn in bucket)
                    {
                        if ((spawn.Position - origin).sqrMagnitude <= radiusSqr)
                        {
                            buffer.Add(spawn);
                        }
                    }
                }
            }
        }

        private float GetNearestSpawnDistance(Vector3 origin)
        {
            var nearestDistanceSqr = float.MaxValue;

            foreach (var spawn in _spawns)
            {
                var distanceSqr = (spawn.Position - origin).sqrMagnitude;
                if (distanceSqr < nearestDistanceSqr)
                {
                    nearestDistanceSqr = distanceSqr;
                }
            }

            return nearestDistanceSqr < float.MaxValue ? Mathf.Sqrt(nearestDistanceSqr) : -1f;
        }

        private Vector3 GetSpawnCreationPosition()
        {
            var activeCamera = GetActiveCamera();
            if (activeCamera == null)
            {
                return GetReferencePosition();
            }

            var ray = new Ray(activeCamera.transform.position, activeCamera.transform.forward);
            if (Physics.Raycast(ray, out var hit, 25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return hit.point + Vector3.up * 0.03f;
            }

            return activeCamera.transform.position + activeCamera.transform.forward * 2f;
        }

        private Vector3 GetSpawnCreationRotation()
        {
            var activeCamera = GetActiveCamera();
            if (activeCamera == null)
            {
                return Vector3.zero;
            }

            return NormalizeEuler(new Vector3(0f, activeCamera.transform.rotation.eulerAngles.y, 0f));
        }

        private static bool AreVectorsClose(Vector3 left, Vector3 right)
        {
            return (left - right).sqrMagnitude <= 0.000001f;
        }

        private static Vector3 NormalizeEuler(Vector3 value)
        {
            return new Vector3(NormalizeAngle(value.x), NormalizeAngle(value.y), NormalizeAngle(value.z));
        }

        private static float NormalizeAngle(float value)
        {
            value %= 360f;
            return value < 0f ? value + 360f : value;
        }

        private sealed class SpawnIndexLoadResult
        {
            public List<SpawnPointData> Spawns { get; set; } = new List<SpawnPointData>();
            public Dictionary<Vector2Int, List<SpawnPointData>> SpatialBuckets { get; set; } = new Dictionary<Vector2Int, List<SpawnPointData>>();
            public string Error { get; set; } = string.Empty;
            public string SourceLabel { get; set; } = string.Empty;
            public string Warning { get; set; } = string.Empty;
        }

        private sealed class SpawnDetailLoadResult
        {
            public SpawnPointData Spawn { get; set; }
            public string Error { get; set; } = string.Empty;
        }

        private sealed class WorldLabelEntry
        {
            public WorldLabelEntry(GameObject root, RectTransform rect, Text text)
            {
                Root = root;
                Rect = rect;
                Text = text;
            }

            public GameObject Root { get; }
            public RectTransform Rect { get; }
            public Text Text { get; }
        }
    }
}
#endregion
