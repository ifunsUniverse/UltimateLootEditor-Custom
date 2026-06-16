#region LootEditorGUI.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT.UI;
using UnityEngine;

namespace ULE.SpawnEditor
{
    internal class LootEditorGUI : MonoBehaviour
    {
        private enum EditorViewMode
        {
            Edited,
            Vanilla,
            Compare
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

        public static bool Open = false;

        private const int MainWindowId = 0x5A17BEEF;
        private const int ItemsPerPage = 200;
        private const float MinItemsScrollHeight = 120f;
        private const float MaxItemsScrollHeight = 260f;
        private const float ReservedFooterHeight = 290f;
        private const float BaseWindowHeight = 680f;
        private const float WindowScreenMargin = 24f;
        private const float ItemNameColumnWidth = 290f;
        private const float ItemTplColumnWidth = 260f;
        private const float ItemWeightLabelWidth = 16f;
        private const float ItemWeightFieldWidth = 56f;
        private const float ItemOpenButtonWidth = 52f;
        private const float ItemRemoveButtonWidth = 24f;
        private const float WorkbenchWindowWidth = 1120f;
        private const float WorkbenchWindowHeight = 760f;
        private const float LivePreviewWindowWidth = 620f;
        private const float LivePreviewWindowHeight = 700f;
        private const float WorkbenchPreviewWidth = 420f;
        private const float WorkbenchPreviewHeight = 320f;
        private const float WorkbenchAttachmentIndent = 18f;
        private const int MaxUndoHistory = 64;
        private const float CoalescedUndoCaptureSeconds = 0.75f;
        private const float StatusMessageHeight = 20f;

        // Wider window so columns don’t feel cramped
        private Rect _win = new Rect(120, 120, 820, BaseWindowHeight);
        private Rect _windowBeforeWorkbench;

        private SpawnVisualizer _viz;
        private BepInEx.Logging.ManualLogSource _log;

        private Vector2 _scroll;
        private int _itemPage;
        private string _itemFilter = string.Empty;
        private string _activeSpawnId = string.Empty;
        private int _activeSpawnDataVersion = -1;
        private string _spawnChanceText = string.Empty;
        private bool _itemRowsDirty = true;
        private bool _itemFilterDirty = true;
        private string _itemRowsSourceKey = string.Empty;
        private List<CachedItemRow> _allItemRows = new List<CachedItemRow>();
        private List<CachedItemRow> _visibleItemRows = new List<CachedItemRow>();
        private readonly Dictionary<LootItem, string> _weightDrafts = new Dictionary<LootItem, string>();
        private readonly Stack<SpawnPointData> _undoHistory = new Stack<SpawnPointData>(MaxUndoHistory);
        private readonly Stack<SpawnPointData> _redoHistory = new Stack<SpawnPointData>(MaxUndoHistory);
        private string _addQuery = string.Empty;
        private string _lastQuery = string.Empty;
        private List<SearchCandidate> _suggestions = new List<SearchCandidate>();
        private LootItem _focusedWeightItem;
        private string _focusedWeightControlName = string.Empty;
        private string _historySpawnId = string.Empty;
        private string _lastUndoActionKey = string.Empty;
        private float _lastUndoCaptureAt;
        private SpawnPointData _pendingPreviewUndoSnapshot;
        private string _pendingPreviewUndoSpawnId = string.Empty;
        private bool _cursorForcedByEditor;
        private bool _previousCursorVisible;
        private CursorLockMode _previousCursorLockState;
        private bool _inputIgnoreForcedByEditor;
        private bool _presetWorkbenchOpen;
        private bool _presetScreenOpen;
        private string _statusMessage = string.Empty;
        private float _statusMessageUntil;
        private readonly PresetWorkbenchSession _presetWorkbench = new PresetWorkbenchSession();
        private Vector2 _workbenchPartsScroll;
        private readonly List<CanvasStateSnapshot> _hiddenWorkbenchBattleUiCanvases = new List<CanvasStateSnapshot>(8);
        private bool _battleUiHiddenForWorkbench;
        private bool _capturedRaidInfoVisibility;

        private bool _showSuggestions = false;
        private int _visibleSuggestionCount;
        private int _suggestionPage;
        private const int _maxSuggestions = 8;
        private const float SuggestionRowHeight = 64f;
        private const float SuggestionPadding = 6f;
        private const float SuggestionPanelSpacing = 8f;
        private const float SuggestionHeaderHeight = 32f;

        private GUIStyle _suggestionRowStyle;
        private GUIStyle _suggestionPrimaryStyle;
        private GUIStyle _suggestionSecondaryStyle;
        private EditorViewMode _viewMode = EditorViewMode.Edited;
        private static MethodInfo _setIgnoreInputWithKeepResetLookMethod;
        private static bool _setIgnoreInputLookupAttempted;

        public void Init(SpawnVisualizer viz, BepInEx.Logging.ManualLogSource log)
        {
            _viz = viz; _log = log;
        }

        private void OnDestroy()
        {
            var hadPreviewScreenOpen = _presetScreenOpen || PresetPreviewBridge.IsPreviewTransitionActiveOrOpen;
            _presetWorkbench.Close();
            RestoreBattleUiForWorkbench();
            ReleaseEditorInputCaptureIfNeeded();
            if (hadPreviewScreenOpen)
            {
                PresetPreviewBridge.ForceRestoreBattleUiNow();
            }
        }

        private void Update()
        {
            if (Plugin.UseTarkovUIForEditor.Value)
            {
                return;
            }

            if (!Open) return;

            if (_presetScreenOpen)
            {
                if (!PresetPreviewBridge.IsPreviewOpen)
                {
                    _presetScreenOpen = false;
                    PresetPreviewBridge.ForceRestoreBattleUiNow();
                    if (PresetPreviewBridge.ConsumeLastAppliedChanges(out var previewEditMessage))
                    {
                        AcceptPendingPreviewUndoSnapshot();
                        if (_viz.ActiveSpawn != null)
                        {
                            _viz.ActiveSpawn.DataVersion++;
                        }

                        _viz.MarkActiveUnsaved();
                        InvalidateItemRows();
                    }
                    else
                    {
                        ClearPendingPreviewUndoSnapshot();
                    }

                    if (!string.IsNullOrWhiteSpace(previewEditMessage))
                    {
                        ShowStatusMessage(previewEditMessage, 6f);
                    }
                }

                return;
            }

            MaintainEditorInputCapture();

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_presetWorkbenchOpen)
                {
                    ClosePresetWorkbench();
                    return;
                }

                CloseEditorWindow();
            }
        }

        private void LateUpdate()
        {
            if (Plugin.UseTarkovUIForEditor.Value)
            {
                return;
            }

            if (!Open)
            {
                return;
            }

            if (_presetScreenOpen)
            {
                return;
            }

            MaintainEditorInputCapture();
        }

        private void OnGUI()
        {
            if (Plugin.UseTarkovUIForEditor.Value)
            {
                return;
            }

            if (!Open || _viz.ActiveSpawn == null || _presetScreenOpen) return;

            UpdateWindowFrame();
            _win = GUI.Window(MainWindowId, _win, DrawWindow, "Ultimate Loot Editor - Spawn Editor");
        }

        private void DrawWindow(int id)
        {
            var s = _viz.ActiveSpawn;
            if (s == null) { GUI.DragWindow(new Rect(0, 0, 10000, 24)); return; }
            var vanilla = _viz.ActiveVanillaSpawn;

            if (!string.Equals(_activeSpawnId, s.Id, StringComparison.Ordinal))
            {
                var isFirstOpen = string.IsNullOrEmpty(_activeSpawnId);
                _activeSpawnId = s.Id ?? string.Empty;
                _activeSpawnDataVersion = s.DataVersion;
                _itemPage = 0;
                _itemFilter = string.Empty;
                if (isFirstOpen)
                {
                    _viewMode = EditorViewMode.Edited;
                }
                _spawnChanceText = s.SpawnChance.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                _scroll = Vector2.zero;
                _itemRowsSourceKey = string.Empty;
                _itemRowsDirty = true;
                _itemFilterDirty = true;
                ResetTransientEditorState();
                ResetUndoRedoHistory();
            }
            else if (_activeSpawnDataVersion != s.DataVersion)
            {
                _activeSpawnDataVersion = s.DataVersion;
                _spawnChanceText = s.SpawnChance.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                _scroll = Vector2.zero;
                _itemRowsSourceKey = string.Empty;
                InvalidateItemRows();
                ResetWeightDrafts();
            }

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            if (_presetWorkbenchOpen)
            {
                GUILayout.Label($"Workbench: {_presetWorkbench.HeaderTitle}");
                GUILayout.Label($"Spawn ID: {s.Id}");
                GUILayout.Label($"Position: {s.Position}");
            }
            else
            {
                GUILayout.Label($"Spawn ID: {s.Id}");
                GUILayout.Label($"Position: {s.Position}");
                if (_viewMode == EditorViewMode.Compare && vanilla != null)
                {
                    GUILayout.Label($"Possible Items: Vanilla {vanilla.ItemCountSummary} | Edited {s.ItemCountSummary}");
                }
                else if (_viewMode == EditorViewMode.Vanilla && vanilla != null)
                {
                    GUILayout.Label($"Possible Items: {vanilla.ItemCountSummary}");
                }
                else
                {
                    GUILayout.Label($"Possible Items: {s.ItemCountSummary}");
                }
            }
            GUILayout.EndVertical();

            GUILayout.Space(12f);
            GUILayout.BeginVertical(GUILayout.Width(_presetWorkbenchOpen ? 420f : 350f));
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (_presetWorkbenchOpen)
            {
                GUI.enabled = !_presetWorkbench.IsReadOnly;
                if (GUILayout.Button("Apply", GUILayout.Width(72)))
                {
                    var undoSnapshot = CaptureActiveSpawnSnapshot();
                    if (_presetWorkbench.TryApply())
                    {
                        PushUndoSnapshot(undoSnapshot, "preset-workbench-apply", coalesce: false);
                        _viz.MarkActiveUnsaved();
                        InvalidateItemRows();
                        ClosePresetWorkbench();
                        ClearStatusMessage();
                    }
                    else
                    {
                        ShowStatusMessage("Nothing was applied from the preset workbench.");
                    }
                }
                GUI.enabled = !_presetWorkbench.IsReadOnly;
                if (GUILayout.Button("Reset", GUILayout.Width(72)))
                {
                    if (_presetWorkbench.TryReset(out var workbenchResetError))
                    {
                        ClearStatusMessage();
                    }
                    else if (!string.IsNullOrWhiteSpace(workbenchResetError))
                    {
                        ShowStatusMessage(workbenchResetError);
                    }
                }
                GUI.enabled = true;
                if (GUILayout.Button("Back", GUILayout.Width(72)))
                {
                    ClosePresetWorkbench();
                }
            }
            else
            {
                GUI.enabled = CanUndoActiveSpawn;
                if (GUILayout.Button("Undo", GUILayout.Width(72)))
                {
                    UndoActiveSpawnChange();
                }

                GUI.enabled = CanRedoActiveSpawn;
                if (GUILayout.Button("Redo", GUILayout.Width(72)))
                {
                    RedoActiveSpawnChange();
                }

                GUI.enabled = true;
                if (GUILayout.Button("Save", GUILayout.Width(72)))
                {
                    CommitFocusedWeightDraft();
                    _viz.CommitEdits(s);
                    ReleaseEditorInputCaptureIfNeeded();
                    _viz.CloseEditorWithoutSaving();
                    ResetEditorWindowState();
                    Open = false;
                    _showSuggestions = false;
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                    return;
                }
            }

            if (GUILayout.Button("X", GUILayout.Width(28)))
            {
                CloseEditorWindow();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                return;
            }
            GUILayout.EndHorizontal();

            if (_presetWorkbenchOpen)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(_presetWorkbench.IsReadOnly
                    ? "Read-only workbench"
                    : "Apply changes here, then use Save to write the spawn edit.");
                GUILayout.EndHorizontal();
            }
            else
            {
                DrawViewModeToolbar(vanilla);
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            DrawStatusMessage();

            if (_viz.IsActiveSpawnLoading)
            {
                GUILayout.Space(8);
                GUILayout.Label("Loading full spawn details...");
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Close"))
                {
                    CloseEditorWindow();
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUI.DragWindow(new Rect(0, 0, 10000, 24));
                return;
            }

            if (!s.DetailsLoaded)
            {
                GUILayout.Space(8);
                var error = _viz.ActiveSpawnLoadError;
                if (!string.IsNullOrWhiteSpace(error))
                {
                    GUILayout.Label(error);
                }
                else
                {
                    GUILayout.Label("Spawn details are not ready yet.");
                }

                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                if (!string.IsNullOrWhiteSpace(error) && GUILayout.Button("Retry"))
                {
                    _viz.RetryActiveSpawnDetails();
                }
                if (GUILayout.Button("Close"))
                {
                    CloseEditorWindow();
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUI.DragWindow(new Rect(0, 0, 10000, 24));
                return;
            }

            if (_presetWorkbenchOpen)
            {
                DrawPresetWorkbench();
                GUILayout.EndVertical();
                GUI.DragWindow(new Rect(0, 0, 10000, 24));
                return;
            }

            if (_viewMode == EditorViewMode.Compare)
            {
                DrawCompareSpawnChance(vanilla, s);
                DrawCompareItemsSection(vanilla, s);
            }
            else
            {
                var displaySpawn = _viewMode == EditorViewMode.Vanilla && vanilla != null ? vanilla : s;
                var editable = _viewMode == EditorViewMode.Edited;

                if (editable)
                {
                    DrawEditableSpawnChance(s, vanilla);
                }
                else
                {
                    DrawReadonlySpawnChance(displaySpawn);
                }

                DrawStandardItemsSection(displaySpawn, editable);

                if (editable)
                {
                    DrawAddItemSection(s);
                    DrawInlineSuggestions(s);
                }
                else if (_viz.IsActiveVanillaLoading && (vanilla == null || !vanilla.DetailsLoaded))
                {
                    GUILayout.Space(6);
                    GUILayout.Label("Vanilla loot table loading...");
                }
            }

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        private void DrawViewModeToolbar(SpawnPointData vanilla)
        {
            var previousMode = _viewMode;
            GUILayout.BeginHorizontal();

            var canPrev = _viz.CanNavigateEditorPrevious;
            var canNext = _viz.CanNavigateEditorNext;
            var candidateCount = Mathf.Max(1, _viz.EditorCandidateCount);
            var activeIndex = Mathf.Clamp(_viz.ActiveEditorCandidateIndex + 1, 1, candidateCount);

            GUI.enabled = canPrev;
            if (GUILayout.Button("<", GUILayout.Width(28)))
            {
                NavigateToEditorCandidate(-1);
            }
            GUI.enabled = canNext;
            if (GUILayout.Button(">", GUILayout.Width(28)))
            {
                NavigateToEditorCandidate(1);
            }
            GUI.enabled = true;

            GUILayout.Label($"{activeIndex}/{candidateCount}", GUILayout.Width(44));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            if (GUILayout.Toggle(_viewMode == EditorViewMode.Edited, "Edited", GUI.skin.button, GUILayout.Width(66)))
            {
                _viewMode = EditorViewMode.Edited;
            }

            if (GUILayout.Toggle(_viewMode == EditorViewMode.Vanilla, "Vanilla", GUI.skin.button, GUILayout.Width(66)))
            {
                _viewMode = EditorViewMode.Vanilla;
            }

            if (GUILayout.Toggle(_viewMode == EditorViewMode.Compare, "Compare", GUI.skin.button, GUILayout.Width(72)))
            {
                _viewMode = EditorViewMode.Compare;
            }

            GUILayout.FlexibleSpace();
            var canRevert = vanilla != null && vanilla.DetailsLoaded;
            GUI.enabled = canRevert;
            if (GUILayout.Button("Revert Vanilla", GUILayout.Width(112)))
            {
                CommitFocusedWeightDraft();
                PushUndoSnapshot("revert-vanilla", coalesce: false);
                if (_viz.RevertActiveToVanilla())
                {
                    _viewMode = EditorViewMode.Edited;
                    _spawnChanceText = _viz.ActiveSpawn != null
                        ? _viz.ActiveSpawn.SpawnChance.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                        : string.Empty;
                    _showSuggestions = false;
                    _visibleSuggestionCount = 0;
                    ResetWeightDrafts();
                    ResetListViewport();
                }
            }
            GUI.enabled = true;

            if (_viz.IsActiveVanillaLoading && (vanilla == null || !vanilla.DetailsLoaded))
            {
                GUILayout.Space(8);
                GUILayout.Label("Vanilla loading...");
            }
            GUILayout.EndHorizontal();

            if (previousMode != _viewMode)
            {
                CommitFocusedWeightDraft();
                _showSuggestions = false;
                _visibleSuggestionCount = 0;
                if (_viewMode == EditorViewMode.Edited && _viz.ActiveSpawn != null)
                {
                    _spawnChanceText = _viz.ActiveSpawn.SpawnChance.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                }
                ResetListViewport();
            }
        }

        private void NavigateToEditorCandidate(int direction)
        {
            CommitFocusedWeightDraft();
            _showSuggestions = false;
            _visibleSuggestionCount = 0;
            if (_viz.NavigateEditorSelection(direction))
            {
                _itemRowsSourceKey = string.Empty;
                _spawnChanceText = _viz.ActiveSpawn != null
                    ? _viz.ActiveSpawn.SpawnChance.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                    : string.Empty;
                ResetWeightDrafts();
                ResetListViewport();
            }
        }

        private void DrawEditableSpawnChance(SpawnPointData spawn, SpawnPointData vanilla)
        {
            GUILayout.Space(6);
            GUILayout.Label("Spawn Chance [0..1]");

            var sliderChance = GUILayout.HorizontalSlider(Mathf.Clamp01(spawn.SpawnChance), 0f, 1f);
            if (Math.Abs(sliderChance - spawn.SpawnChance) > 0.0001f)
            {
                PushUndoSnapshot("spawn-chance", coalesce: true);
                ApplyEditedSpawnChance(spawn, sliderChance);
            }

            var newChanceText = GUILayout.TextField(_spawnChanceText, GUILayout.Width(90));
            if (!string.Equals(newChanceText, _spawnChanceText, StringComparison.Ordinal))
            {
                _spawnChanceText = newChanceText;
                if (float.TryParse(_spawnChanceText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float typed))
                {
                    var typedChance = Mathf.Clamp01(typed);
                    if (Math.Abs(typedChance - spawn.SpawnChance) > 0.0001f)
                    {
                        PushUndoSnapshot("spawn-chance", coalesce: true);
                        ApplyEditedSpawnChance(spawn, typedChance);
                    }
                }
            }

            EnsureAlwaysSpawnSupportFromVanilla(spawn, vanilla);
            if (SupportsAlwaysSpawnUi(spawn, vanilla))
            {
                GUILayout.BeginHorizontal();
                var current = spawn.IsAlwaysSpawn;
                var next = GUILayout.Toggle(current, "Always Spawn", GUILayout.Width(130));
                GUILayout.Label(current ? "Forced" : "Probability roll", GUILayout.Width(120));
                GUILayout.EndHorizontal();

                if (next != current)
                {
                    PushUndoSnapshot("always-spawn", coalesce: false);
                    ApplyAlwaysSpawnToggle(spawn, vanilla, next);
                }
            }
        }

        private void DrawReadonlySpawnChance(SpawnPointData spawn)
        {
            GUILayout.Space(6);
            GUILayout.Label("Spawn Chance [0..1]");
            GUILayout.Label(spawn != null
                ? spawn.SpawnChance.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                : "Unavailable");

            if (SupportsAlwaysSpawnUi(spawn, null))
            {
                GUILayout.Label($"Always Spawn: {(spawn.IsAlwaysSpawn ? "Yes" : "No")}");
            }
        }

        private void DrawCompareSpawnChance(SpawnPointData vanilla, SpawnPointData edited)
        {
            GUILayout.Space(6);
            GUILayout.Label("Spawn Chance Comparison");

            var vanillaChance = vanilla?.SpawnChance ?? 0f;
            var editedChance = edited?.SpawnChance ?? 0f;
            var delta = editedChance - vanillaChance;
            GUILayout.Label(
                $"Vanilla: {vanillaChance:0.000} | Edited: {editedChance:0.000} | Delta: {delta:+0.000;-0.000;0.000}");

            if (SupportsAlwaysSpawnUi(edited, vanilla))
            {
                var vanillaAlways = vanilla != null && vanilla.IsAlwaysSpawn;
                var editedAlways = edited != null && edited.IsAlwaysSpawn;
                GUILayout.Label($"Always Spawn: Vanilla {(vanillaAlways ? "Yes" : "No")} | Edited {(editedAlways ? "Yes" : "No")}");
            }
        }

        private void EnsureAlwaysSpawnSupportFromVanilla(SpawnPointData spawn, SpawnPointData vanilla)
        {
            if (spawn == null || vanilla == null || !vanilla.HasAlwaysSpawnFlag || spawn.HasAlwaysSpawnFlag)
            {
                return;
            }

            spawn.HasAlwaysSpawnFlag = true;
            spawn.IsAlwaysSpawn = spawn.SpawnChance >= 0.999f;
        }

        private static bool SupportsAlwaysSpawnUi(SpawnPointData spawn, SpawnPointData vanilla)
        {
            return (spawn != null && (spawn.HasAlwaysSpawnFlag || spawn.IsAlwaysSpawn)) ||
                   (vanilla != null && (vanilla.HasAlwaysSpawnFlag || vanilla.IsAlwaysSpawn));
        }

        private void ApplyEditedSpawnChance(SpawnPointData spawn, float chance)
        {
            if (spawn == null)
            {
                return;
            }

            spawn.SpawnChance = Mathf.Clamp01(chance);
            if (spawn.HasAlwaysSpawnFlag)
            {
                spawn.IsAlwaysSpawn = spawn.SpawnChance >= 0.999f;
            }

            _spawnChanceText = spawn.SpawnChance.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
            _viz.MarkActiveUnsaved();
        }

        private void ApplyAlwaysSpawnToggle(SpawnPointData spawn, SpawnPointData vanilla, bool enabled)
        {
            if (spawn == null)
            {
                return;
            }

            spawn.HasAlwaysSpawnFlag = true;
            spawn.IsAlwaysSpawn = enabled;
            spawn.SpawnChance = enabled
                ? 1f
                : GetProbabilityWhenAlwaysSpawnDisabled(spawn, vanilla);
            _spawnChanceText = spawn.SpawnChance.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
            _viz.MarkActiveUnsaved();
        }

        private static float GetProbabilityWhenAlwaysSpawnDisabled(SpawnPointData spawn, SpawnPointData vanilla)
        {
            if (spawn != null && spawn.SpawnChance < 0.999f)
            {
                return Mathf.Clamp01(spawn.SpawnChance);
            }

            if (vanilla != null && vanilla.SpawnChance < 0.999f)
            {
                return Mathf.Clamp01(vanilla.SpawnChance);
            }

            return 0.5f;
        }

        private void DrawStandardItemsSection(SpawnPointData spawn, bool editable)
        {
            GUILayout.Space(6);
            GUILayout.Label(editable ? "Items at this spawn" : "Items in this view");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Filter", GUILayout.Width(36));
            var newItemFilter = GUILayout.TextField(_itemFilter, GUILayout.Width(260));
            if (!string.Equals(newItemFilter, _itemFilter, StringComparison.Ordinal))
            {
                _itemFilter = newItemFilter;
                _itemPage = 0;
                _scroll = Vector2.zero;
                _itemFilterDirty = true;
            }
            GUILayout.EndHorizontal();

            if (spawn == null)
            {
                GUILayout.Label("No spawn data is available for this view.");
                return;
            }

            if (!spawn.DetailsLoaded)
            {
                GUILayout.Label("Full loot table loading...");
                return;
            }

            EnsureItemRows(
                spawn,
                $"{_viewMode}|{spawn.Id}|{spawn.DataVersion}|{spawn.ItemCountSummary}|{spawn.DetailsLoaded}");

            var pageCount = Mathf.Max(1, Mathf.CeilToInt(_visibleItemRows.Count / (float)ItemsPerPage));
            _itemPage = Mathf.Clamp(_itemPage, 0, pageCount - 1);
            var pageStart = _itemPage * ItemsPerPage;
            var pagedItems = _visibleItemRows.Skip(pageStart).Take(ItemsPerPage).ToList();
            var shownFrom = _visibleItemRows.Count == 0 ? 0 : pageStart + 1;
            var shownTo = _visibleItemRows.Count == 0 ? 0 : pageStart + pagedItems.Count;

            GUILayout.Label(
                string.IsNullOrWhiteSpace(_itemFilter)
                    ? $"Showing {shownFrom}-{shownTo} of {_visibleItemRows.Count} items"
                    : $"Showing {shownFrom}-{shownTo} of {_visibleItemRows.Count} matching items");

            var itemScrollHeight = Mathf.Clamp(_win.height - ReservedFooterHeight, MinItemsScrollHeight, MaxItemsScrollHeight);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(itemScrollHeight));

            LootItem toRemove = null;
            LootItem focusedWeightItemThisFrame = null;
            string focusedWeightControlThisFrame = string.Empty;

            for (int rowIndex = 0; rowIndex < pagedItems.Count; rowIndex++)
            {
                var row = pagedItems[rowIndex];
                var item = row.Item;
                var canOpenPresetPreview = PresetPreviewBridge.CanOpen(item);

                GUILayout.BeginHorizontal();
                GUILayout.Label(row.DisplayName, GUILayout.Width(ItemNameColumnWidth));
                GUILayout.Label($"TPL: {item.Tpl}", GUILayout.Width(ItemTplColumnWidth));

                if (editable)
                {
                    if (canOpenPresetPreview)
                    {
                        if (GUILayout.Button("Edit", GUILayout.Width(ItemOpenButtonWidth)))
                        {
                            TryOpenPresetScreen(item, readOnly: false);
                        }
                    }
                    else
                    {
                        GUILayout.Space(ItemOpenButtonWidth);
                    }

                    GUILayout.Label("W:", GUILayout.Width(ItemWeightLabelWidth));
                    var weightControlName = $"ule_weight_{pageStart + rowIndex}_{GetStableItemKey(item)}";
                    GUI.SetNextControlName(weightControlName);
                    var currentWeightText = GetWeightFieldText(item);
                    var wstr = GUILayout.TextField(currentWeightText, GUILayout.Width(ItemWeightFieldWidth));
                    if (!string.Equals(wstr, currentWeightText, StringComparison.Ordinal))
                    {
                        _weightDrafts[item] = wstr;
                    }

                    if (string.Equals(GUI.GetNameOfFocusedControl(), weightControlName, StringComparison.Ordinal))
                    {
                        focusedWeightItemThisFrame = item;
                        focusedWeightControlThisFrame = weightControlName;
                    }

                    if (GUILayout.Button("X", GUILayout.Width(ItemRemoveButtonWidth)))
                    {
                        toRemove = item;
                    }
                }
                else
                {
                    if (PresetPreviewBridge.CanOpen(item))
                    {
                        if (GUILayout.Button("Edit", GUILayout.Width(ItemOpenButtonWidth)))
                        {
                            TryOpenPresetScreen(item, readOnly: true);
                        }
                    }
                    else
                    {
                        GUILayout.Space(ItemOpenButtonWidth);
                    }

                    GUILayout.Label("W:", GUILayout.Width(ItemWeightLabelWidth));
                    GUILayout.Label(item.Weight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), GUILayout.Width(ItemWeightFieldWidth + 24f));
                }

                GUILayout.EndHorizontal();
            }

            if (editable)
            {
                HandleWeightDraftFocusTransition(focusedWeightItemThisFrame, focusedWeightControlThisFrame);

                if (toRemove != null)
                {
                    PushUndoSnapshot($"remove-item:{GetStableItemKey(toRemove)}", coalesce: false);
                    spawn.Items.Remove(toRemove);
                    _weightDrafts.Remove(toRemove);
                    if (ReferenceEquals(_focusedWeightItem, toRemove))
                    {
                        _focusedWeightItem = null;
                        _focusedWeightControlName = string.Empty;
                    }
                    spawn.ItemCountSummary = spawn.Items.Count;
                    _viz.MarkActiveUnsaved();
                    InvalidateItemRows();
                }
            }

            GUILayout.EndScrollView();
            DrawPager(pageCount);
        }

        private void DrawCompareItemsSection(SpawnPointData vanilla, SpawnPointData edited)
        {
            GUILayout.Space(6);
            GUILayout.Label("Compare Loot Tables");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Filter", GUILayout.Width(36));
            var newItemFilter = GUILayout.TextField(_itemFilter, GUILayout.Width(260));
            if (!string.Equals(newItemFilter, _itemFilter, StringComparison.Ordinal))
            {
                _itemFilter = newItemFilter;
                _itemPage = 0;
                _scroll = Vector2.zero;
            }
            GUILayout.EndHorizontal();

            if (vanilla == null || !vanilla.DetailsLoaded)
            {
                GUILayout.Label(_viz.IsActiveVanillaLoading
                    ? "Vanilla loot table loading..."
                    : "Vanilla loot table is not ready yet.");
                return;
            }

            var allRows = BuildCompareRows(vanilla, edited);
            var filteredRows = string.IsNullOrWhiteSpace(_itemFilter)
                ? allRows
                : allRows.Where(row => MatchesCompareFilter(row, _itemFilter)).ToList();

            var addedCount = allRows.Count(row => string.Equals(row.Status, "Added", StringComparison.Ordinal));
            var removedCount = allRows.Count(row => string.Equals(row.Status, "Removed", StringComparison.Ordinal));
            var changedCount = allRows.Count(row => string.Equals(row.Status, "Changed", StringComparison.Ordinal));
            GUILayout.Label($"Changed: {changedCount} | Added: {addedCount} | Removed: {removedCount}");

            var pageCount = Mathf.Max(1, Mathf.CeilToInt(filteredRows.Count / (float)ItemsPerPage));
            _itemPage = Mathf.Clamp(_itemPage, 0, pageCount - 1);
            var pageStart = _itemPage * ItemsPerPage;
            var pagedRows = filteredRows.Skip(pageStart).Take(ItemsPerPage).ToList();
            var shownFrom = filteredRows.Count == 0 ? 0 : pageStart + 1;
            var shownTo = filteredRows.Count == 0 ? 0 : pageStart + pagedRows.Count;

            GUILayout.Label(
                string.IsNullOrWhiteSpace(_itemFilter)
                    ? $"Showing {shownFrom}-{shownTo} of {filteredRows.Count} items"
                    : $"Showing {shownFrom}-{shownTo} of {filteredRows.Count} matching items");

            var itemScrollHeight = Mathf.Clamp(_win.height - ReservedFooterHeight, MinItemsScrollHeight, MaxItemsScrollHeight);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(itemScrollHeight));

            foreach (var row in pagedRows)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(row.DisplayName, GUILayout.Width(240));
                GUILayout.Label($"TPL: {row.Tpl}", GUILayout.Width(260));
                GUILayout.Label($"V: {row.VanillaWeightText}", GUILayout.Width(82));
                GUILayout.Label($"E: {row.EditedWeightText}", GUILayout.Width(82));
                GUILayout.Label(row.Status, GUILayout.Width(90));
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            DrawPager(pageCount);
        }

        private void DrawAddItemSection(SpawnPointData spawn)
        {
            GUILayout.Space(6);
            GUILayout.Label("Add item by TPL or Name:");
            GUILayout.BeginHorizontal();

            _addQuery = GUILayout.TextField(_addQuery, GUILayout.Width(460));

            if (_addQuery != _lastQuery)
            {
                _lastQuery = _addQuery;
                UpdateSuggestions();
            }

            if (GUILayout.Button("Search", GUILayout.Width(80))) UpdateSuggestions();
            if (GUILayout.Button("Add First", GUILayout.Width(90))) AddFirstSuggestion(spawn);
            GUILayout.EndHorizontal();
        }

        private void DrawPager(int pageCount)
        {
            GUILayout.BeginHorizontal();
            var prevEnabled = _itemPage > 0;
            var nextEnabled = _itemPage < pageCount - 1;

            GUI.enabled = prevEnabled;
            if (GUILayout.Button("Previous 200", GUILayout.Width(120)))
            {
                _itemPage--;
                _scroll = Vector2.zero;
            }

            GUI.enabled = nextEnabled;
            if (GUILayout.Button("Next 200", GUILayout.Width(120)))
            {
                _itemPage++;
                _scroll = Vector2.zero;
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            GUILayout.Label($"Page {_itemPage + 1}/{pageCount}");
            GUILayout.EndHorizontal();
        }

        private void ResetListViewport()
        {
            _itemPage = 0;
            _scroll = Vector2.zero;
            _itemRowsSourceKey = string.Empty;
            InvalidateItemRows();
        }

        private void DrawInlineSuggestions(SpawnPointData spawn)
        {
            if (!_showSuggestions || _suggestions.Count == 0)
            {
                return;
            }

            EnsureSuggestionStyles();
            ClampSuggestionPage();
            var pageCount = GetSuggestionPageCount();
            var startIndex = GetSuggestionPageStartIndex();
            var count = Mathf.Min(Mathf.Max(1, _visibleSuggestionCount), Mathf.Max(0, _suggestions.Count - startIndex));
            var rowH = SuggestionRowHeight;
            var pad = SuggestionPadding;

            GUILayout.Space(SuggestionPanelSpacing);
            GUILayout.BeginHorizontal();
            GUILayout.Label(count < _suggestions.Count
                ? $"Search Results (showing {startIndex + 1}-{startIndex + count} of {_suggestions.Count})"
                : "Search Results");
            GUILayout.FlexibleSpace();
            GUI.enabled = _suggestionPage > 0;
            if (GUILayout.Button("<", GUILayout.Width(28)))
            {
                _suggestionPage = Mathf.Max(0, _suggestionPage - 1);
            }

            GUI.enabled = _suggestionPage < pageCount - 1;
            if (GUILayout.Button(">", GUILayout.Width(28)))
            {
                _suggestionPage = Mathf.Min(pageCount - 1, _suggestionPage + 1);
            }
            GUI.enabled = true;
            GUILayout.Label($"Page {Mathf.Max(1, _suggestionPage + 1)}/{Mathf.Max(1, pageCount)}", GUILayout.Width(72));
            GUILayout.EndHorizontal();

            var panelHeight = SuggestionHeaderHeight + pad * 2f + count * rowH;
            var panelRect = GUILayoutUtility.GetRect(10f, panelHeight, GUILayout.ExpandWidth(true));
            GUI.Box(panelRect, GUIContent.none);

            var currentEvent = Event.current;
            if (currentEvent != null &&
                currentEvent.type == EventType.ScrollWheel &&
                panelRect.Contains(currentEvent.mousePosition) &&
                pageCount > 1)
            {
                if (currentEvent.delta.y > 0f && _suggestionPage < pageCount - 1)
                {
                    _suggestionPage++;
                    currentEvent.Use();
                }
                else if (currentEvent.delta.y < 0f && _suggestionPage > 0)
                {
                    _suggestionPage--;
                    currentEvent.Use();
                }
            }

            for (int i = 0; i < count; i++)
            {
                var y = panelRect.y + SuggestionHeaderHeight + pad + i * rowH;
                var rowRect = new Rect(panelRect.x + pad, y, panelRect.width - pad * 2f, rowH - 4f);

                var item = _suggestions[startIndex + i];

                if (GUI.Button(rowRect, GUIContent.none, _suggestionRowStyle))
                {
                    AddSuggestionToSpawn(spawn, item);
                    _showSuggestions = false;
                }

                const float primaryHeight = 20f;
                const float secondaryHeight = 18f;
                const float textGap = 6f;
                var contentHeight = primaryHeight + textGap + secondaryHeight;
                var topOffset = Mathf.Max(8f, Mathf.Round((rowRect.height - contentHeight) * 0.5f));

                var primaryRect = new Rect(rowRect.x + 12f, rowRect.y + topOffset, rowRect.width - 24f, primaryHeight);
                var secondaryRect = new Rect(rowRect.x + 12f, primaryRect.yMax + textGap, rowRect.width - 24f, secondaryHeight);
                GUI.Label(primaryRect, item.DisplayName, _suggestionPrimaryStyle);
                var secondaryText = item.IsPreset && !string.IsNullOrWhiteSpace(item.TemplateItem?.PresetName)
                    ? $"TPL: {item.Tpl} | Preset: {item.TemplateItem.PresetName}"
                    : $"TPL: {item.Tpl}";
                GUI.Label(secondaryRect, secondaryText, _suggestionSecondaryStyle);
            }
        }

        private void UpdateSuggestions()
        {
            var query = (_addQuery ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                _suggestions.Clear();
                _visibleSuggestionCount = 0;
                _suggestionPage = 0;
                _showSuggestions = false;
                return;
            }

            _suggestions = TplCache.Search(query, Plugin.IncludeUnsafeSearchItems)
                                   .Distinct(new SearchCandidateKeyComparer())
                                   .OrderBy(candidate => GetSuggestionRank(candidate, query))
                                   .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                                   .ThenBy(candidate => candidate.Tpl, StringComparer.OrdinalIgnoreCase)
                                   .Take(GetConfiguredSuggestionLimit())
                                   .ToList();
            _suggestionPage = 0;
            _visibleSuggestionCount = Mathf.Min(_maxSuggestions, _suggestions.Count);
            _showSuggestions = _suggestions.Count > 0;
        }

        private void MaintainEditorInputCapture()
        {
            if (!_cursorForcedByEditor)
            {
                _previousCursorVisible = Cursor.visible;
                _previousCursorLockState = Cursor.lockState;
                _cursorForcedByEditor = true;
            }

            if (!_inputIgnoreForcedByEditor)
            {
                SetRaidInputIgnored(ignore: true);
                _inputIgnoreForcedByEditor = true;
            }

            if (!Cursor.visible)
            {
                Cursor.visible = true;
            }

            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void ReleaseEditorInputCaptureIfNeeded()
        {
            if (_inputIgnoreForcedByEditor)
            {
                SetRaidInputIgnored(ignore: false);
                _inputIgnoreForcedByEditor = false;
            }

            if (_cursorForcedByEditor)
            {
                Cursor.visible = _previousCursorVisible;
                Cursor.lockState = _previousCursorLockState;
                _cursorForcedByEditor = false;
            }
        }

        private void SetRaidInputIgnored(bool ignore)
        {
            try
            {
                if (!_setIgnoreInputLookupAttempted)
                {
                    _setIgnoreInputLookupAttempted = true;
                    var gamePlayerOwnerType = Type.GetType("EFT.GamePlayerOwner, Assembly-CSharp", throwOnError: false);
                    _setIgnoreInputWithKeepResetLookMethod = gamePlayerOwnerType?.GetMethod(
                        "SetIgnoreInputWithKeepResetLook",
                        BindingFlags.Public | BindingFlags.Static,
                        binder: null,
                        types: new[] { typeof(bool) },
                        modifiers: null);
                }

                _setIgnoreInputWithKeepResetLookMethod?.Invoke(null, new object[] { ignore });
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to toggle raid input ignore state: {ex.Message}");
            }
        }

        private void AddFirstSuggestion(SpawnPointData s)
        {
            var firstIndex = GetSuggestionPageStartIndex();
            var first = firstIndex >= 0 && firstIndex < _suggestions.Count
                ? _suggestions[firstIndex]
                : _suggestions.FirstOrDefault();
            if (first != null)
            {
                AddSuggestionToSpawn(s, first);
                _showSuggestions = false;
            }
        }

        private void EnsureItemRows(SpawnPointData spawn, string sourceKey)
        {
            if (!string.Equals(_itemRowsSourceKey, sourceKey, StringComparison.Ordinal))
            {
                _itemRowsSourceKey = sourceKey ?? string.Empty;
                _itemRowsDirty = true;
                _itemFilterDirty = true;
            }

            if (_itemRowsDirty)
            {
                _allItemRows = spawn.Items
                    .Select(item =>
                    {
                        var name = FormatItemDisplayName(item);
                        return new CachedItemRow
                        {
                            Item = item,
                            DisplayName = name,
                            SearchText = BuildItemSearchText(item, name),
                        };
                    })
                    .OrderByDescending(row => row.Item.Weight)
                    .ThenBy(row => row.Item.Tpl, StringComparer.Ordinal)
                    .ToList();

                _itemRowsDirty = false;
                _itemFilterDirty = true;
            }

            if (_itemFilterDirty)
            {
                _visibleItemRows = string.IsNullOrWhiteSpace(_itemFilter)
                    ? _allItemRows
                    : _allItemRows.Where(row => MatchesItemFilter(row, _itemFilter)).ToList();
                _itemFilterDirty = false;
            }
        }

        private void InvalidateItemRows()
        {
            _itemRowsSourceKey = string.Empty;
            _itemRowsDirty = true;
            _itemFilterDirty = true;
        }

        private void CloseEditorWindow()
        {
            var hadPreviewScreenOpen = _presetScreenOpen || PresetPreviewBridge.IsPreviewTransitionActiveOrOpen;
            ResetEditorWindowState();
            ReleaseEditorInputCaptureIfNeeded();
            if (hadPreviewScreenOpen)
            {
                PresetPreviewBridge.ForceRestoreBattleUiNow();
            }
            _viz.CloseEditorWithoutSaving();
            Open = false;
            _showSuggestions = false;
        }

        private void TryOpenPresetScreen(LootItem item, bool readOnly)
        {
            if (item == null)
            {
                return;
            }

            CommitFocusedWeightDraft();
            if (!readOnly)
            {
                CapturePendingPreviewUndoSnapshot();
            }

            if (PresetPreviewBridge.TryOpen(item, _log, applyOnClose: !readOnly, out var error))
            {
                ReleaseEditorInputCaptureIfNeeded();
                _presetScreenOpen = true;
                ClearStatusMessage();
                return;
            }

            ClearPendingPreviewUndoSnapshot();
            ShowStatusMessage(string.IsNullOrWhiteSpace(error)
                ? "Failed to open the preset screen."
                : error);
        }

        private void HandleWeightDraftFocusTransition(LootItem focusedItemThisFrame, string focusedControlThisFrame)
        {
            if (_focusedWeightItem != null &&
                !ReferenceEquals(_focusedWeightItem, focusedItemThisFrame))
            {
                CommitWeightDraft(_focusedWeightItem);
            }

            _focusedWeightItem = focusedItemThisFrame;
            _focusedWeightControlName = focusedControlThisFrame ?? string.Empty;
        }

        private void CommitFocusedWeightDraft()
        {
            if (_focusedWeightItem != null)
            {
                CommitWeightDraft(_focusedWeightItem);
            }
        }

        private void CommitWeightDraft(LootItem item)
        {
            if (item == null || !_weightDrafts.TryGetValue(item, out var draft))
            {
                return;
            }

            if (float.TryParse(draft, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var newWeight))
            {
                newWeight = Mathf.Max(0f, newWeight);
                if (Math.Abs(newWeight - item.Weight) > 0.0001f)
                {
                    PushUndoSnapshot($"weight:{GetStableItemKey(item)}", coalesce: false);
                    item.Weight = newWeight;
                    _viz.MarkActiveUnsaved();
                    InvalidateItemRows();
                }
            }

            _weightDrafts.Remove(item);
            if (ReferenceEquals(_focusedWeightItem, item))
            {
                _focusedWeightItem = null;
                _focusedWeightControlName = string.Empty;
            }
        }

        private string GetWeightFieldText(LootItem item)
        {
            return _weightDrafts.TryGetValue(item, out var draft)
                ? draft
                : item.Weight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private void ResetTransientEditorState()
        {
            ResetWeightDrafts();
            _addQuery = string.Empty;
            _lastQuery = string.Empty;
            _suggestions.Clear();
            _showSuggestions = false;
            _visibleSuggestionCount = 0;
            _suggestionPage = 0;
            ClearPendingPreviewUndoSnapshot();
        }

        private void ResetEditorWindowState()
        {
            _presetWorkbench.Close();
            RestoreBattleUiForWorkbench();
            _presetWorkbenchOpen = false;
            _presetScreenOpen = false;
            _workbenchPartsScroll = Vector2.zero;
            ResetTransientEditorState();
            _activeSpawnId = string.Empty;
            _activeSpawnDataVersion = -1;
            _viewMode = EditorViewMode.Edited;
            _spawnChanceText = string.Empty;
            _itemFilter = string.Empty;
            _itemRowsSourceKey = string.Empty;
            _itemPage = 0;
            _scroll = Vector2.zero;
            ResetUndoRedoHistory();
            ClearStatusMessage();
        }

        private int GetSuggestionPageCount()
        {
            return Mathf.Max(1, Mathf.CeilToInt((_suggestions?.Count ?? 0) / (float)_maxSuggestions));
        }

        private int GetSuggestionPageStartIndex()
        {
            ClampSuggestionPage();
            return Mathf.Max(0, _suggestionPage * _maxSuggestions);
        }

        private void ClampSuggestionPage()
        {
            var pageCount = GetSuggestionPageCount();
            _suggestionPage = Mathf.Clamp(_suggestionPage, 0, Mathf.Max(0, pageCount - 1));
        }

        private static int GetSuggestionRank(SearchCandidate candidate, string query)
        {
            if (candidate == null)
            {
                return int.MaxValue;
            }

            var normalizedQuery = (query ?? string.Empty).Trim();
            var displayName = candidate.DisplayName ?? string.Empty;
            var presetName = candidate.TemplateItem?.PresetName ?? string.Empty;
            var tpl = candidate.Tpl ?? string.Empty;

            if (displayName.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                presetName.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.IsPreset ? 0 : 1;
            }

            if (candidate.IsPreset &&
                (displayName.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                 presetName.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase)))
            {
                return 2;
            }

            if (displayName.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                presetName.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (candidate.IsPreset &&
                (displayName.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 presetName.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return 4;
            }

            if (displayName.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                presetName.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 5;
            }

            if (tpl.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.IsPreset ? 6 : 7;
            }

            return candidate.IsPreset ? 8 : 9;
        }

        private int GetConfiguredSuggestionLimit()
        {
            return Mathf.Clamp(Plugin.SearchMaxRankedMatches, _maxSuggestions, 1000);
        }

        private void ResetWeightDrafts()
        {
            _weightDrafts.Clear();
            _focusedWeightItem = null;
            _focusedWeightControlName = string.Empty;
        }

        private static string GetStableItemKey(LootItem item)
        {
            if (item == null)
            {
                return "null";
            }

            if (!string.IsNullOrWhiteSpace(item.ComposedKey))
            {
                return item.ComposedKey;
            }

            return item.Tpl ?? "unknown";
        }

        private void AddSuggestionToSpawn(SpawnPointData spawn, SearchCandidate item)
        {
            if (spawn == null || item == null || string.IsNullOrWhiteSpace(item.Tpl))
            {
                return;
            }

            var newItem = item.TemplateItem?.Clone() ?? new LootItem { Tpl = item.Tpl };
            newItem.ComposedKey = Util.GenerateComposedKey();
            newItem.Weight = 1f;
            LootAmmoAutoFill.FillMagazineAmmo(newItem);

            PushUndoSnapshot($"add-item:{item.StableKey}", coalesce: false);
            spawn.Items.Add(newItem);
            spawn.ItemCountSummary = spawn.Items.Count;
            _viz.MarkActiveUnsaved();
            InvalidateItemRows();
        }

        private void TryOpenPresetWorkbench(LootItem item, bool readOnly, bool useLiveCameraOverlay = false)
        {
            if (item == null)
            {
                return;
            }

            CommitFocusedWeightDraft();

            if (_presetWorkbench.TryOpen(item, readOnly, _log, useLiveCameraOverlay, out var error))
            {
                _presetWorkbenchOpen = true;
                _workbenchPartsScroll = Vector2.zero;
                _windowBeforeWorkbench = _win;
                HideBattleUiForWorkbench();
                if (useLiveCameraOverlay)
                {
                    _win.width = LivePreviewWindowWidth;
                    _win.height = LivePreviewWindowHeight;
                }
                else
                {
                    _win.width = Mathf.Max(_win.width, WorkbenchWindowWidth);
                    _win.height = Mathf.Max(_win.height, WorkbenchWindowHeight);
                }
                ClearStatusMessage();
                return;
            }

            ShowStatusMessage(string.IsNullOrWhiteSpace(error)
                ? "Failed to open the preset workbench."
                : error);
        }

        private void ClosePresetWorkbench()
        {
            _presetWorkbench.Close();
            _presetWorkbenchOpen = false;
            _workbenchPartsScroll = Vector2.zero;
            RestoreBattleUiForWorkbench();
            if (_windowBeforeWorkbench.width > 0f && _windowBeforeWorkbench.height > 0f)
            {
                _win.width = _windowBeforeWorkbench.width;
                _win.height = _windowBeforeWorkbench.height;
            }
        }

        private void DrawPresetWorkbench()
        {
            if (_presetWorkbench.UsesLiveCameraOverlay)
            {
                DrawLiveOverlayWorkbench();
                return;
            }

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(WorkbenchPreviewWidth));
            GUILayout.Label("Preview");
            var previewRect = GUILayoutUtility.GetRect(WorkbenchPreviewWidth, WorkbenchPreviewHeight, GUILayout.Width(WorkbenchPreviewWidth), GUILayout.Height(WorkbenchPreviewHeight));
            GUI.Box(previewRect, GUIContent.none);
            _presetWorkbench.EnsurePreviewTexture(Mathf.RoundToInt(previewRect.width), Mathf.RoundToInt(previewRect.height));
            var previewTexture = _presetWorkbench.PreviewTexture;
            if (previewTexture != null)
            {
                GUI.DrawTexture(previewRect, previewTexture, ScaleMode.ScaleToFit, false);
            }
            else
            {
                GUI.Label(previewRect, "Loading preview...");
            }

            HandleWorkbenchPreviewInput(previewRect);
            GUILayout.Space(6f);
            GUILayout.Label("Drag inside the preview to rotate. Mouse wheel zooms.");
            GUILayout.Label($"Attachments: {_presetWorkbench.AttachmentCount}");
            GUILayout.EndVertical();

            GUILayout.Space(14f);
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Label("Attachment Tree");
            _workbenchPartsScroll = GUILayout.BeginScrollView(_workbenchPartsScroll, GUILayout.Height(WorkbenchPreviewHeight + 60f));
            foreach (var row in _presetWorkbench.GetRows())
            {
                DrawWorkbenchAttachmentRow(row);
            }
            GUILayout.EndScrollView();
            GUILayout.Label(_presetWorkbench.IsReadOnly
                ? "Opened from a read-only view. Switch to Edited mode to apply attachment changes."
                : "Safe preset preview: inspect and remove attachments without opening Tarkov's native modding screen. Compatible-part swapping comes next.");
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void HideBattleUiForWorkbench()
        {
            if (_battleUiHiddenForWorkbench)
            {
                return;
            }

            _hiddenWorkbenchBattleUiCanvases.Clear();

            try
            {
                var battleScreen = MonoBehaviourSingleton<CommonUI>.Instance?.EftBattleUIScreen;
                if (battleScreen != null)
                {
                    foreach (var canvas in battleScreen.GetComponentsInChildren<Canvas>(true))
                    {
                        if (canvas == null)
                        {
                            continue;
                        }

                        _hiddenWorkbenchBattleUiCanvases.Add(new CanvasStateSnapshot(canvas, canvas.enabled));
                        canvas.enabled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to hide Battle UI canvases for preset workbench: {ex.Message}");
            }

            try
            {
                var preloader = MonoBehaviourSingleton<PreloaderUI>.Instance;
                if (preloader != null)
                {
                    _capturedRaidInfoVisibility = true;
                    preloader.RaidInfoVisibility = false;
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to hide raid info for preset workbench: {ex.Message}");
            }

            _battleUiHiddenForWorkbench = true;
        }

        private void RestoreBattleUiForWorkbench()
        {
            if (!_battleUiHiddenForWorkbench &&
                _hiddenWorkbenchBattleUiCanvases.Count == 0 &&
                !_capturedRaidInfoVisibility)
            {
                return;
            }

            foreach (var snapshot in _hiddenWorkbenchBattleUiCanvases)
            {
                if (snapshot.Canvas != null)
                {
                    snapshot.Canvas.enabled = snapshot.Enabled;
                }
            }

            _hiddenWorkbenchBattleUiCanvases.Clear();

            if (_capturedRaidInfoVisibility)
            {
                try
                {
                    var preloader = MonoBehaviourSingleton<PreloaderUI>.Instance;
                    if (preloader != null)
                    {
                        preloader.RaidInfoVisibility = true;
                    }
                }
                catch (Exception ex)
                {
                    _log?.LogWarning($"[ULE] Failed to restore raid info after preset workbench: {ex.Message}");
                }
            }

            _capturedRaidInfoVisibility = false;
            _battleUiHiddenForWorkbench = false;
        }

        private void DrawLiveOverlayWorkbench()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Live raid-background preview");
            GUILayout.Label("This path keeps Tarkov's raid camera, snow, lighting, HUD, and screen state untouched.");

            var controlRect = GUILayoutUtility.GetRect(520f, 120f, GUILayout.ExpandWidth(true), GUILayout.Height(120f));
            GUI.Box(controlRect, "Drag here to rotate the weapon. Mouse wheel zooms.");
            HandleWorkbenchPreviewInput(controlRect);

            GUILayout.Space(8f);
            GUILayout.Label($"Attachments: {_presetWorkbench.AttachmentCount}");
            GUILayout.Label("Attachment Tree");

            var treeHeight = Mathf.Max(220f, _win.height - 300f);
            _workbenchPartsScroll = GUILayout.BeginScrollView(_workbenchPartsScroll, GUILayout.Height(treeHeight));
            foreach (var row in _presetWorkbench.GetRows())
            {
                DrawWorkbenchAttachmentRow(row);
            }
            GUILayout.EndScrollView();

            GUILayout.Label(_presetWorkbench.IsReadOnly
                ? "Opened from a read-only view. Switch to Edited mode to apply attachment changes."
                : "Safe live-preview path: inspect and remove attachments here without opening Tarkov's native modding screen.");
        }

        private void HandleWorkbenchPreviewInput(Rect previewRect)
        {
            var currentEvent = Event.current;
            if (currentEvent == null || !previewRect.Contains(currentEvent.mousePosition))
            {
                return;
            }

            if (currentEvent.type == EventType.MouseDrag && currentEvent.button == 0)
            {
                _presetWorkbench.RotatePreview(currentEvent.delta.x, -currentEvent.delta.y);
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.ScrollWheel)
            {
                _presetWorkbench.ZoomPreview(currentEvent.delta.y * 0.02f);
                currentEvent.Use();
            }
        }

        private void DrawWorkbenchAttachmentRow(PresetWorkbenchSession.AttachmentRow row)
        {
            if (row == null)
            {
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Space(row.Depth * WorkbenchAttachmentIndent);
            GUILayout.Label(row.SlotLabel, GUILayout.Width(90));
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Label(row.DisplayName);
            GUILayout.Label($"TPL: {row.Tpl}");
            GUILayout.EndVertical();

            if (!_presetWorkbench.IsReadOnly && row.CanRemove)
            {
                if (GUILayout.Button("Remove", GUILayout.Width(72)))
                {
                    if (_presetWorkbench.TryRemoveNode(row, out var error))
                    {
                        ClearStatusMessage();
                    }
                    else if (!string.IsNullOrWhiteSpace(error))
                    {
                        ShowStatusMessage(error);
                    }
                }
            }
            else
            {
                GUILayout.Space(72f);
            }

            GUILayout.EndHorizontal();
        }

        private void UpdateWindowFrame()
        {
            if (_presetWorkbenchOpen)
            {
                var minWidth = _presetWorkbench.UsesLiveCameraOverlay ? LivePreviewWindowWidth : WorkbenchWindowWidth;
                var minHeight = _presetWorkbench.UsesLiveCameraOverlay ? LivePreviewWindowHeight : WorkbenchWindowHeight;
                var workbenchMaxWidth = Mathf.Max(minWidth, Screen.width - WindowScreenMargin * 2f);
                var workbenchMaxHeight = Mathf.Max(minHeight, Screen.height - WindowScreenMargin * 2f);
                _win.width = Mathf.Clamp(_win.width, minWidth, workbenchMaxWidth);
                _win.height = Mathf.Clamp(_win.height, minHeight, workbenchMaxHeight);
                _win.x = Mathf.Clamp(_win.x, WindowScreenMargin, Mathf.Max(WindowScreenMargin, Screen.width - _win.width - WindowScreenMargin));
                _win.y = Mathf.Clamp(_win.y, WindowScreenMargin, Mathf.Max(WindowScreenMargin, Screen.height - _win.height - WindowScreenMargin));
                return;
            }

            _visibleSuggestionCount = _showSuggestions
                ? Mathf.Min(_maxSuggestions, Mathf.Max(0, _suggestions.Count - GetSuggestionPageStartIndex()))
                : 0;

            var suggestionExtraHeight = _visibleSuggestionCount > 0
                ? SuggestionPanelSpacing + SuggestionHeaderHeight + SuggestionPadding * 2f + _visibleSuggestionCount * SuggestionRowHeight + 20f
                : 0f;

            var maxHeight = Mathf.Max(BaseWindowHeight, Screen.height - WindowScreenMargin * 2f);
            _win.height = Mathf.Clamp(BaseWindowHeight + suggestionExtraHeight, BaseWindowHeight, maxHeight);
            _win.x = Mathf.Clamp(_win.x, WindowScreenMargin, Mathf.Max(WindowScreenMargin, Screen.width - _win.width - WindowScreenMargin));
            _win.y = Mathf.Clamp(_win.y, WindowScreenMargin, Mathf.Max(WindowScreenMargin, Screen.height - _win.height - WindowScreenMargin));
        }

        private bool CanUndoActiveSpawn =>
            _undoHistory.Count > 0 &&
            _viz?.ActiveSpawn != null &&
            string.Equals(_historySpawnId, _viz.ActiveSpawn.Id, StringComparison.Ordinal);

        private bool CanRedoActiveSpawn =>
            _redoHistory.Count > 0 &&
            _viz?.ActiveSpawn != null &&
            string.Equals(_historySpawnId, _viz.ActiveSpawn.Id, StringComparison.Ordinal);

        private SpawnPointData CaptureActiveSpawnSnapshot()
        {
            return _viz?.ActiveSpawn?.Clone();
        }

        private void CapturePendingPreviewUndoSnapshot()
        {
            _pendingPreviewUndoSnapshot = CaptureActiveSpawnSnapshot();
            _pendingPreviewUndoSpawnId = _pendingPreviewUndoSnapshot?.Id ?? string.Empty;
        }

        private void AcceptPendingPreviewUndoSnapshot()
        {
            if (_pendingPreviewUndoSnapshot != null &&
                _viz?.ActiveSpawn != null &&
                string.Equals(_pendingPreviewUndoSpawnId, _viz.ActiveSpawn.Id, StringComparison.Ordinal))
            {
                PushUndoSnapshot(_pendingPreviewUndoSnapshot, "preset-screen-apply", coalesce: false);
            }

            ClearPendingPreviewUndoSnapshot();
        }

        private void ClearPendingPreviewUndoSnapshot()
        {
            _pendingPreviewUndoSnapshot = null;
            _pendingPreviewUndoSpawnId = string.Empty;
        }

        private void PushUndoSnapshot(string actionKey, bool coalesce)
        {
            PushUndoSnapshot(CaptureActiveSpawnSnapshot(), actionKey, coalesce);
        }

        private void PushUndoSnapshot(SpawnPointData snapshot, string actionKey, bool coalesce)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.Id))
            {
                return;
            }

            EnsureHistoryScope(snapshot.Id);

            var now = Time.unscaledTime;
            if (coalesce &&
                string.Equals(_lastUndoActionKey, actionKey ?? string.Empty, StringComparison.Ordinal) &&
                now - _lastUndoCaptureAt <= CoalescedUndoCaptureSeconds)
            {
                return;
            }

            if (_undoHistory.Count > 0 && AreSpawnSnapshotsEquivalent(_undoHistory.Peek(), snapshot))
            {
                return;
            }

            PushHistorySnapshot(_undoHistory, snapshot);
            _redoHistory.Clear();
            _lastUndoActionKey = actionKey ?? string.Empty;
            _lastUndoCaptureAt = now;
        }

        private void UndoActiveSpawnChange()
        {
            if (!CanUndoActiveSpawn)
            {
                return;
            }

            CommitFocusedWeightDraft();
            if (!CanUndoActiveSpawn)
            {
                return;
            }

            var current = CaptureActiveSpawnSnapshot();
            var previous = _undoHistory.Pop();
            PushHistorySnapshot(_redoHistory, current);
            RestoreActiveSpawnSnapshot(previous);
            ResetUndoCoalescing();
            ShowStatusMessage("Undo applied.");
        }

        private void RedoActiveSpawnChange()
        {
            if (!CanRedoActiveSpawn)
            {
                return;
            }

            CommitFocusedWeightDraft();
            if (!CanRedoActiveSpawn)
            {
                return;
            }

            var current = CaptureActiveSpawnSnapshot();
            var next = _redoHistory.Pop();
            PushHistorySnapshot(_undoHistory, current);
            RestoreActiveSpawnSnapshot(next);
            ResetUndoCoalescing();
            ShowStatusMessage("Redo applied.");
        }

        private void RestoreActiveSpawnSnapshot(SpawnPointData snapshot)
        {
            var active = _viz?.ActiveSpawn;
            if (active == null ||
                snapshot == null ||
                !string.Equals(active.Id, snapshot.Id, StringComparison.Ordinal))
            {
                return;
            }

            active.CopyFrom(snapshot);
            active.ItemCountSummary = active.Items?.Count ?? 0;
            active.DetailsLoaded = true;
            active.DataVersion++;
            _activeSpawnDataVersion = active.DataVersion;
            _spawnChanceText = active.SpawnChance.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
            _viewMode = EditorViewMode.Edited;
            _showSuggestions = false;
            _visibleSuggestionCount = 0;
            _suggestionPage = 0;
            ClearPendingPreviewUndoSnapshot();
            ResetWeightDrafts();
            ResetListViewport();
            _viz.MarkActiveUnsaved();
        }

        private void EnsureHistoryScope(string spawnId)
        {
            if (string.Equals(_historySpawnId, spawnId ?? string.Empty, StringComparison.Ordinal))
            {
                return;
            }

            ResetUndoRedoHistory();
            _historySpawnId = spawnId ?? string.Empty;
        }

        private void ResetUndoRedoHistory()
        {
            _undoHistory.Clear();
            _redoHistory.Clear();
            _historySpawnId = _viz?.ActiveSpawn?.Id ?? string.Empty;
            ResetUndoCoalescing();
            ClearPendingPreviewUndoSnapshot();
        }

        private void ResetUndoCoalescing()
        {
            _lastUndoActionKey = string.Empty;
            _lastUndoCaptureAt = 0f;
        }

        private static void PushHistorySnapshot(Stack<SpawnPointData> history, SpawnPointData snapshot)
        {
            if (history == null || snapshot == null)
            {
                return;
            }

            if (history.Count >= MaxUndoHistory)
            {
                var retained = history.Reverse().Skip(1).ToList();
                history.Clear();
                foreach (var existing in retained)
                {
                    history.Push(existing);
                }
            }

            history.Push(snapshot.Clone());
        }

        private static bool AreSpawnSnapshotsEquivalent(SpawnPointData left, SpawnPointData right)
        {
            if (left == null || right == null)
            {
                return ReferenceEquals(left, right);
            }

            if (!string.Equals(left.Id, right.Id, StringComparison.Ordinal) ||
                Math.Abs(left.SpawnChance - right.SpawnChance) > 0.0001f)
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

                if (Math.Abs(leftItem.Weight - rightItem.Weight) > 0.0001f ||
                    !AreLootItemsEquivalent(leftItem, rightItem))
                {
                    return false;
                }
            }

            return true;
        }

        private List<CompareItemRow> BuildCompareRows(SpawnPointData vanilla, SpawnPointData edited)
        {
            var vanillaByKey = (vanilla?.Items ?? new List<LootItem>())
                .Where(item => item != null)
                .ToDictionary(GetStableItemKey, item => item, StringComparer.Ordinal);

            var editedByKey = (edited?.Items ?? new List<LootItem>())
                .Where(item => item != null)
                .ToDictionary(GetStableItemKey, item => item, StringComparer.Ordinal);

            var allKeys = vanillaByKey.Keys
                .Concat(editedByKey.Keys)
                .Distinct(StringComparer.Ordinal);

            return allKeys
                .Select(key =>
                {
                    vanillaByKey.TryGetValue(key, out var vanillaItem);
                    editedByKey.TryGetValue(key, out var editedItem);

                    var item = editedItem ?? vanillaItem;
                    var tpl = item?.Tpl ?? string.Empty;
                    var status = "Unchanged";
                    if (vanillaItem == null)
                    {
                        status = "Added";
                    }
                    else if (editedItem == null)
                    {
                        status = "Removed";
                    }
                    else if (Math.Abs(vanillaItem.Weight - editedItem.Weight) > 0.0001f ||
                             !AreLootItemsEquivalent(vanillaItem, editedItem))
                    {
                        status = "Changed";
                    }

                    return new CompareItemRow
                    {
                        DisplayName = FormatItemDisplayName(item),
                        Tpl = tpl,
                        VanillaWeightText = vanillaItem != null
                            ? vanillaItem.Weight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                            : "-",
                        EditedWeightText = editedItem != null
                            ? editedItem.Weight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                            : "-",
                        Status = status,
                        SearchText = BuildItemSearchText(item, FormatItemDisplayName(item)) + "\n" + status
                    };
                })
                .OrderBy(row => CompareStatusRank(row.Status))
                .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Tpl, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void DrawStatusMessage()
        {
            GUILayout.BeginHorizontal(GUILayout.Height(StatusMessageHeight));
            if (!string.IsNullOrWhiteSpace(_statusMessage) && Time.unscaledTime <= _statusMessageUntil)
            {
                GUILayout.Label(_statusMessage);
            }
            else
            {
                GUILayout.Label(string.Empty);
            }
            GUILayout.EndHorizontal();
        }

        private void ShowStatusMessage(string message, float durationSeconds = 4f)
        {
            _statusMessage = message ?? string.Empty;
            _statusMessageUntil = Time.unscaledTime + Mathf.Max(1f, durationSeconds);
        }

        private void ClearStatusMessage()
        {
            _statusMessage = string.Empty;
            _statusMessageUntil = 0f;
        }

        private static int CompareStatusRank(string status)
        {
            switch (status)
            {
                case "Changed":
                    return 0;
                case "Added":
                    return 1;
                case "Removed":
                    return 2;
                default:
                    return 3;
            }
        }

        private void EnsureSuggestionStyles()
        {
            if (_suggestionRowStyle != null)
            {
                return;
            }

            _suggestionRowStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 0, 0),
            };

            _suggestionPrimaryStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Clip,
                wordWrap = false,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Max(12, GUI.skin.label.fontSize)
            };

            _suggestionSecondaryStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Clip,
                wordWrap = false,
                fontSize = Mathf.Max(12, GUI.skin.label.fontSize),
                normal = { textColor = new Color(0.78f, 0.82f, 0.86f, 0.95f) }
            };
        }

        private static bool MatchesItemFilter(CachedItemRow row, string query)
        {
            if (row == null || string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(row.SearchText) &&
                   row.SearchText.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool MatchesCompareFilter(CompareItemRow row, string query)
        {
            if (row == null || string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(row.SearchText) &&
                   row.SearchText.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatItemDisplayName(LootItem item)
        {
            if (item == null)
            {
                return "Unknown";
            }

            var baseName = TplCache.DisplayNameFromTpl(item.Tpl, includeSafetyBadges: true);
            var label = string.IsNullOrWhiteSpace(item.PresetName)
                ? baseName
                : item.PresetName.Trim();

            if (!string.IsNullOrWhiteSpace(item.PresetName) &&
                !string.Equals(item.PresetName.Trim(), baseName, StringComparison.OrdinalIgnoreCase))
            {
                label = $"{item.PresetName.Trim()} ({baseName})";
            }

            if (PresetPreviewBridge.CanOpen(item))
            {
                var childCount = CountChildNodes(item.Children);
                if (childCount > 0)
                {
                    label = $"{label} [+{childCount}]";
                }
            }

            return label;
        }

        private static string BuildItemSearchText(LootItem item, string displayName)
        {
            var parts = new List<string>
            {
                displayName ?? string.Empty,
                item?.Tpl ?? string.Empty,
                item?.PresetName ?? string.Empty,
                item?.PresetId ?? string.Empty,
            };

            AppendChildSearchText(parts, item?.Children);
            return string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private static void AppendChildSearchText(List<string> parts, IEnumerable<LootItemNode> nodes)
        {
            if (parts == null || nodes == null)
            {
                return;
            }

            foreach (var node in nodes)
            {
                if (node == null)
                {
                    continue;
                }

                parts.Add(node.Tpl ?? string.Empty);
                parts.Add(TplCache.NameFromTpl(node.Tpl));
                AppendChildSearchText(parts, node.Children);
            }
        }

        private static int CountChildNodes(IEnumerable<LootItemNode> nodes)
        {
            if (nodes == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var node in nodes)
            {
                if (node == null)
                {
                    continue;
                }

                count++;
                count += CountChildNodes(node.Children);
            }

            return count;
        }

        private static bool AreLootItemsEquivalent(LootItem left, LootItem right)
        {
            if (left == null || right == null)
            {
                return ReferenceEquals(left, right);
            }

            if (!string.Equals(left.Tpl, right.Tpl, StringComparison.Ordinal) ||
                !string.Equals(left.ComposedKey, right.ComposedKey, StringComparison.Ordinal) ||
                !string.Equals(left.PresetId, right.PresetId, StringComparison.Ordinal) ||
                !string.Equals(left.PresetName, right.PresetName, StringComparison.Ordinal) ||
                !string.Equals(left.SlotId, right.SlotId, StringComparison.Ordinal) ||
                !string.Equals(left.LocationJson, right.LocationJson, StringComparison.Ordinal) ||
                !string.Equals(left.UpdJson, right.UpdJson, StringComparison.Ordinal) ||
                left.StackMin != right.StackMin ||
                left.StackMax != right.StackMax)
            {
                return false;
            }

            return AreLootItemNodesEquivalent(left.Children, right.Children);
        }

        private static bool AreLootItemNodesEquivalent(IList<LootItemNode> left, IList<LootItemNode> right)
        {
            var leftNodes = left ?? Array.Empty<LootItemNode>();
            var rightNodes = right ?? Array.Empty<LootItemNode>();
            if (leftNodes.Count != rightNodes.Count)
            {
                return false;
            }

            for (var i = 0; i < leftNodes.Count; i++)
            {
                var leftNode = leftNodes[i];
                var rightNode = rightNodes[i];
                if (leftNode == null || rightNode == null)
                {
                    if (!ReferenceEquals(leftNode, rightNode))
                    {
                        return false;
                    }

                    continue;
                }

                if (!string.Equals(leftNode.Tpl, rightNode.Tpl, StringComparison.Ordinal) ||
                    !string.Equals(leftNode.SlotId, rightNode.SlotId, StringComparison.Ordinal) ||
                    !string.Equals(leftNode.LocationJson, rightNode.LocationJson, StringComparison.Ordinal) ||
                    !string.Equals(leftNode.UpdJson, rightNode.UpdJson, StringComparison.Ordinal) ||
                    leftNode.StackMin != rightNode.StackMin ||
                    leftNode.StackMax != rightNode.StackMax ||
                    !AreLootItemNodesEquivalent(leftNode.Children, rightNode.Children))
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class SearchCandidateKeyComparer : IEqualityComparer<SearchCandidate>
        {
            public bool Equals(SearchCandidate x, SearchCandidate y)
                => string.Equals(x?.StableKey, y?.StableKey, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode(SearchCandidate obj)
                => obj?.StableKey?.GetHashCode() ?? 0;
        }

        private sealed class CachedItemRow
        {
            public LootItem Item;
            public string DisplayName;
            public string SearchText;
        }

        private sealed class CompareItemRow
        {
            public string DisplayName;
            public string Tpl;
            public string VanillaWeightText;
            public string EditedWeightText;
            public string Status;
            public string SearchText;
        }
    }
}
#endregion

