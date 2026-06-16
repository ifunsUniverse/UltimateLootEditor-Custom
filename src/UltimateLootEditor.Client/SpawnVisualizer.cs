#region SpawnVisualizer.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using UnityEngine;

namespace ULE.SpawnEditor
{
    internal class SpawnVisualizer : MonoBehaviour
    {
        private const float SpatialCellSize = 20f;
        private const int MaxWorldLabels = 10;
        private const float VisibilityRefreshInterval = 0.75f;

        private List<SpawnPointData> _spawns = new List<SpawnPointData>();
        private readonly Dictionary<string, SpawnPointData> _spawnById = new Dictionary<string, SpawnPointData>(StringComparer.Ordinal);
        private readonly Dictionary<string, SpawnPointData> _vanillaSpawnById = new Dictionary<string, SpawnPointData>(StringComparer.Ordinal);
        private readonly Dictionary<Vector2Int, List<SpawnPointData>> _spatialBuckets = new Dictionary<Vector2Int, List<SpawnPointData>>();
        private readonly List<SpawnPointData> _nearbySpawns = new List<SpawnPointData>(128);
        private readonly List<SpawnPointData> _visibleSpawns = new List<SpawnPointData>(64);
        private readonly List<string> _editorCandidateSpawnIds = new List<string>();
        private readonly Dictionary<string, SpawnPointData> _editorSessionDraftsById = new Dictionary<string, SpawnPointData>(StringComparer.Ordinal);
        private readonly HashSet<string> _editorSessionDirtySpawnIds = new HashSet<string>(StringComparer.Ordinal);
        private BepInEx.Logging.ManualLogSource _log;
        private string _mapId;
        private string _looseLootPath;
        private MapEdits _edits;

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
        private int _editorCandidateIndex = -1;

        public SpawnPointData ActiveSpawn { get; private set; }

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

            ActiveSpawn = null;
            _activeSourceSpawn = null;
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
            PollPendingSpawnIndexLoad();
            PollPendingDetailLoad();

            if (Input.GetKeyDown(Plugin.ToggleVizKey.Value))
            {
                _visible = !_visible;
                _nextVisibilityRefreshAt = 0f;
                _nextVisibilityHintAt = 0f;

                if (_visible)
                {
                    BeginLoadSpawnIndex();
                    LogDebug(_spawnIndexReady
                        ? $"[ULE] Visualizer enabled. Range={GetEffectiveRenderDistance():0.#}m, max visible={GetVisibleSphereCap()}."
                        : "[ULE] Visualizer enabled. Loose loot data is still loading in the background.");
                    RefreshVisibleSpawns(forceRefresh: true);
                }
                else
                {
                    _visibleSpawns.Clear();
                    _hasLastVisibilityOrigin = false;
                    LogDebug("[ULE] Visualizer disabled.");
                }
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

            if (Input.GetKeyDown(Plugin.OpenEditorKey.Value))
            {
                var near = FindBestAimedSpawnWithinRadius(GetReferencePosition(), Plugin.SelectRadius.Value);
                if (near != null)
                {
                    OpenEditorForSpawn(near);
                }
            }
        }

        private void LateUpdate()
        {
            if (!_spawnIndexReady)
            {
                return;
            }

            if (!_visible || _visibleSpawns.Count == 0)
            {
                return;
            }

            var mesh = Util.GetOrCreateSphereMesh();
            if (mesh == null)
            {
                return;
            }

            var scale = Vector3.one * Mathf.Max(0.02f, Plugin.SphereScale.Value);
            if (_visible)
            {
                foreach (var spawn in _visibleSpawns)
                {
                    Graphics.DrawMesh(
                        mesh,
                        Matrix4x4.TRS(spawn.Position, Quaternion.identity, scale),
                        GetRenderMaterial(spawn),
                        0);
                }
            }
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

            target.CopyFrom(edited);
            target.ItemCountSummary = target.Items?.Count ?? 0;
            target.DetailsLoaded = true;
            target.DataVersion++;

            var vanilla = GetVanillaSpawn(target.Id);
            if (AreSpawnsEquivalent(target, vanilla))
            {
                _edits.BySpawnId.Remove(target.Id);
            }
            else
            {
                var se = new SpawnEdit
                {
                    SpawnChance = target.SpawnChance,
                    IsAlwaysSpawn = target.HasAlwaysSpawnFlag ? target.IsAlwaysSpawn : (bool?)null,
                    Items = CloneLootItemList(target.Items)
                };

                _edits.BySpawnId[target.Id] = se;
            }

            SaveManager.SaveMapEdits(_edits);
            _activeSourceSpawn = target;
            ActiveSpawn = target.Clone();
            _activeSpawnDirty = false;
        }

        public void CloseEditorWithoutSaving()
        {
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

                if (edit.Items != null)
                {
                    spawn.Items = CloneLootItemList(edit.Items);

                    spawn.ItemCountSummary = spawn.Items.Count;
                    spawn.DetailsLoaded = true;
                    spawn.DataVersion++;
                }
            }
        }

        private void OpenEditorForSpawn(SpawnPointData spawn)
        {
            ResetEditorDraftSession();
            BuildEditorCandidateGroup(spawn);
            ActivateEditorSpawn(spawn);
            LootEditorGUI.Open = true;
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

            target.SpawnChance = loaded.SpawnChance;
            target.Items = CloneLootItemList(loaded.Items);
            target.ItemCountSummary = loaded.ItemCountSummary;
            target.DetailsLoaded = true;
            target.DataVersion++;

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

            _activeSourceSpawn = sourceSpawn;
            ActiveSpawn = GetEditorDraftOrClone(sourceSpawn);
            _activeSpawnDirty = ActiveSpawn != null &&
                !string.IsNullOrWhiteSpace(ActiveSpawn.Id) &&
                _editorSessionDirtySpawnIds.Contains(ActiveSpawn.Id);
            _activeSpawnLoadErrorSpawnId = string.Empty;
            _activeSpawnLoadError = string.Empty;

            var vanilla = GetVanillaSpawn(sourceSpawn.Id);
            var needsEditedDetails = !sourceSpawn.DetailsLoaded;
            var needsVanillaDetails = vanilla == null || !vanilla.DetailsLoaded;

            if (needsEditedDetails)
            {
                BeginLoadSpawnDetails(sourceSpawn, forceReload: false);
            }
            else if (needsVanillaDetails)
            {
                BeginLoadSpawnDetails(sourceSpawn, forceReload: true);
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

            if ((left.HasAlwaysSpawnFlag || right.HasAlwaysSpawnFlag) &&
                left.IsAlwaysSpawn != right.IsAlwaysSpawn)
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
            return items?.Select(item => item?.Clone())
                .Where(item => item != null)
                .ToList() ?? new List<LootItem>();
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
                LogDebug($"[ULE] Visualizer is enabled, but no spheres are within the current range of {maxDistance:0.#}m.{nearestSuffix}");
            }
            else if (forceRefresh)
            {
                LogDebug($"[ULE] Visualizer refresh: visible={_visibleSpawns.Count}, range={maxDistance:0.#}m.");
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
            var activeCamera = GetActiveCamera();
            return activeCamera != null ? activeCamera.transform.position : Vector3.zero;
        }

        private static Camera GetActiveCamera()
        {
            try
            {
                if (CameraClass.Exist)
                {
                    var battleCamera = CameraClass.Instance.Camera;
                    if (battleCamera != null && battleCamera.isActiveAndEnabled)
                    {
                        return battleCamera;
                    }

                    var opticCamera = CameraClass.Instance.OpticCameraManager?.Camera;
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

            if (!_spawnIndexReady || !Plugin.DrawWorldLabels.Value)
            {
                return;
            }

            var activeCamera = GetActiveCamera();
            if (activeCamera == null)
            {
                return;
            }

            var labelsDrawn = 0;
            foreach (var spawn in _visibleSpawns)
            {
                if (labelsDrawn >= MaxWorldLabels)
                {
                    break;
                }

                var wp = spawn.Position + Vector3.up * 0.6f;
                var sp = activeCamera.WorldToScreenPoint(wp);
                if (sp.z <= 0f)
                {
                    continue;
                }

                var rect = new Rect(sp.x - 120f, Screen.height - sp.y - 10f, 240f, 20f);
                GUI.Label(rect, GetSpawnLabel(spawn.Id));
                labelsDrawn++;
            }
        }

        private string GetSpawnLabel(string spawnId)
        {
            var label = spawnId ?? "Spawn";
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
    }
}
#endregion
