#region TarkovLootEditorUI.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.HandBook;
using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ULE.SpawnEditor
{
    internal sealed class UleSpawnEditorWindow : Window<GClass3829>
    {
        private static readonly BindingFlags WindowFieldFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo WindowTransformField = typeof(Window<GClass3829>).GetField("_windowTransform", WindowFieldFlags);
        private static readonly FieldInfo CaptionPanelField = typeof(Window<GClass3829>).GetField("_captionPanel", WindowFieldFlags);
        private static readonly FieldInfo CaptionField = typeof(Window<GClass3829>).GetField("_caption", WindowFieldFlags);
        private static readonly FieldInfo CloseButtonField = typeof(Window<GClass3829>).GetField("_closeButton", WindowFieldFlags);

        private Action _requestClose;
        private bool _closing;

        public override void Awake()
        {
            // Runtime-added Window<T> fields are copied from the inspect template after AddComponent.
        }

        public void InitFromInspectTemplate(InfoWindow source, RectTransform windowTransform, Action requestClose)
        {
            _requestClose = requestClose;
            _closing = false;

            var root = source != null ? source.transform : transform;
            var captionPanel = root.Find("Inner/Caption Panel")?.gameObject;
            var caption = source?.Caption ?? captionPanel?.GetComponentInChildren<TextMeshProUGUI>(true);
            var closeButton = source?.CloseButton ?? root.Find("Inner/Caption Panel/Close Button")?.GetComponent<Button>();

            WindowTransformField?.SetValue(this, windowTransform ?? source?.WindowTransform ?? (transform as RectTransform));
            CaptionPanelField?.SetValue(this, captionPanel);
            CaptionField?.SetValue(this, caption);
            CloseButtonField?.SetValue(this, closeButton);
        }

        public override void Close()
        {
            if (_closing)
            {
                return;
            }

            _closing = true;
            _requestClose?.Invoke();
        }

        public void CloseSilently()
        {
            if (_closing)
            {
                return;
            }

            _closing = true;
            try
            {
                base.Close();
            }
            catch
            {
                // The shell is being torn down; stale EFT window state is removed separately.
            }
        }

        public void ResetInterceptedClose()
        {
            _closing = false;
        }
    }

    internal sealed class TarkovLootEditorUI : MonoBehaviour
    {
        private enum EditorViewMode
        {
            Edited,
            Vanilla,
            Compare
        }

        private const int MaxUndoHistory = 64;
        private const int MaxSearchResults = 8;
        private const float CoalescedUndoCaptureSeconds = 0.75f;
        private const float NativeCloseAfterPresetReturnGraceSeconds = 0.75f;
        private const float MinWindowWidth = 760f;
        private const float MinWindowHeight = 560f;
        private const float DefaultWindowWidth = 870f;
        private const float DefaultWindowHeight = 650f;
        private const float WindowMargin = 0f;
        private const float NativeButtonWidth = 175f;
        private const float NativeButtonHeight = 18f;
        private const float NativeButtonSpacing = 8f;
        private const float NativeHeaderInputHeight = 30f;
        private const float NativeSpawnChanceInputWidth = 105f;
        private const float NativeSpawnChanceInputTop = 4f;
        private const float NativeFooterInputHeight = 30f;
        private const float NativeExistingSearchWidth = 470f;
        private const float NativeAddSearchWidth = NativeExistingSearchWidth;
        private const float NativeHeaderHeight = 148f;
        private const float NativeFooterHeight = 76f;
        private const float NativeResultsWidth = 858f;
        private const float NativeResultsTop = NativeHeaderHeight + NativeResultRowSpacing;
        private const float NativeResultsHeight = 446f;
        private const float NativeResultsSideMargin = NativeResultRowSpacing;
        private const float NativeResultsLeftMargin = 4f;
        private const float NativeResultsRightMargin = 0f;
        private const float NativeResultsFooterGap = 23f;
        private const float NativeVisualPixel = 0.75f;
        private const float NativeMiddleBottomBorderDrop = 6f * NativeVisualPixel;
        private const float NativeResultsScrollbarWidth = 6f;
        private const float NativeResultsScrollbarLeftGap = 2.25f;
        private const float NativeResultsScrollbarRightGap = 2.25f;
        private const float NativeResultsScrollbarBottomExtension = 2f * NativeVisualPixel;
        private const float NativeResultsViewportRightGutter =
            NativeResultsScrollbarLeftGap + NativeResultsScrollbarWidth + NativeResultsScrollbarRightGap;
        private const float NativeResultRowHeight = 68f;
        private const float NativeResultRowSpacing = 4f;
        private const float NativeRowControlWidth = 78f;
        private const float NativeRowWeightWidth = 46f;
        private const float NativeRowRemoveButtonSize = 20f;
        private const float NativeRowWeightHeight = 23f;
        private const float NativeRowWeightLeftOffset = -1f;
        private const float NativeRowControlInset = 10f;
        private const float NativeRowControlYOffset = 3f;
        private const float NativeRowControlReservedWidth = 102f;
        private const float NativeRowRemoveButtonInset = 4f;
        private const float NativeCompareStatusWidth = 180f;
        private const float NativeCompareStatusSpacing = 8f;
        private const float NativeRowTextLeftInset = 98f;
        private const float NativeRowTextRightPadding = 10f;
        private const int NativeVirtualizedRowThreshold = 24;
        private const int NativeVirtualizedRowBuffer = 4;
        private const int NativeVirtualizedMinRows = 12;
        private const int NativeVirtualizedMaxResizePoolRows = 48;
        private const int EditorCanvasSortingOrder = 3000;
        private const float EditorCursorReapplyIntervalSeconds = 0.12f;

        private static readonly Color PanelColor = new Color(0.015f, 0.017f, 0.018f, 0.72f);
        private static readonly Color HeaderColor = new Color(0.055f, 0.065f, 0.068f, 0.88f);
        private static readonly Color SectionColor = new Color(0.03f, 0.035f, 0.038f, 0.64f);
        private static readonly Color RowColor = new Color(0.02f, 0.023f, 0.026f, 0.66f);
        private static readonly Color RowAltColor = new Color(0.035f, 0.04f, 0.043f, 0.66f);
        private static readonly Color ButtonColor = new Color(0.13f, 0.15f, 0.155f, 0.96f);
        private static readonly Color ButtonDisabledColor = new Color(0.08f, 0.085f, 0.09f, 0.55f);
        private static readonly Color AccentColor = new Color(0.08f, 0.30f, 0.39f, 0.95f);
        private static readonly Color TextColor = new Color(0.88f, 0.91f, 0.92f, 1f);
        private static readonly Color SubtleTextColor = new Color(0.66f, 0.71f, 0.74f, 1f);

        private SpawnVisualizer _viz;
        private ManualLogSource _log;
        private Font _font;
        private GameObject _root;
        private RectTransform _window;
        private RectTransform _content;
        private Text _statusText;
        private bool _usingInspectWindowShell;
        private bool _usingNativeInspectWindowContainer;
        private InfoWindow _inspectWindowShell;
        private UleSpawnEditorWindow _inspectInputWindow;
        private ItemSpecificationPanel _inspectPanelShell;
        private InteractionButtonsContainer _inspectActionContainer;
        private SimpleContextMenuButton _inspectActionTemplate;
        private RectTransform _inspectActionHost;
        private RectTransform _inspectFooterPanel;
        private RectTransform _inspectPanelBackgroundTemplate;
        private Graphic _inspectPanelBackgroundGraphicTemplate;
        private Slider _chanceSlider;
        private InputField _chanceInput;
        private TMP_InputField _nativeChanceInput;
        private RectTransform _nativeChanceCaret;
        private bool _updatingNativeChanceInput;
        private Toggle _alwaysSpawnToggle;
        private string _lastBuildKey = string.Empty;
        private string _activeSpawnId = string.Empty;
        private int _activeSpawnDataVersion = -1;
        private EditorViewMode _viewMode = EditorViewMode.Edited;
        private string _itemFilter = string.Empty;
        private string _addQuery = string.Empty;
        private List<SearchCandidate> _suggestions = new List<SearchCandidate>();
        private readonly Stack<SpawnPointData> _undoHistory = new Stack<SpawnPointData>(MaxUndoHistory);
        private readonly Stack<SpawnPointData> _redoHistory = new Stack<SpawnPointData>(MaxUndoHistory);
        private string _historySpawnId = string.Empty;
        private string _lastUndoActionKey = string.Empty;
        private float _lastUndoCaptureAt;
        private SpawnPointData _pendingPreviewUndoSnapshot;
        private string _pendingPreviewUndoSpawnId = string.Empty;
        private bool _presetScreenOpen;
        private bool _openingPresetScreen;
        private float _ignoreNativeCloseUntil;
        private string _statusMessage = string.Empty;
        private float _statusMessageUntil;
        private bool _inputCaptured;
        private bool _raidInputIgnoredByEditor;
        private bool _previousCursorVisible;
        private CursorLockMode _previousCursorLockState;
        private ECursorType? _editorHoverCursor;
        private ECursorType? _editorForcedCursor;
        private ECursorType? _lastAppliedEditorCursor;
        private float _lastEditorCursorApplyAt;
        private EventSystem _previousEventSystem;
        private GameObject _editorEventSystemRoot;
        private static MethodInfo _setIgnoreInputMethod;
        private static MethodInfo _setIgnoreInputWithKeepResetLookMethod;
        private static bool _setIgnoreInputLookupAttempted;
        private static FieldInfo _browseSearchInputField;
        private static TMP_InputField _nativeTextInputTemplate;
        private static EntityListElement _handbookEntityRowTemplate;
        private static GameObject _handbookEntityScrollTemplate;
        private static FieldInfo _handbookEntityNameField;
        private static FieldInfo _handbookEntityCategoryField;
        private static FieldInfo _handbookEntityBackgroundField;
        private static FieldInfo _handbookEntityIconField;
        private static FieldInfo _handbookEntityWishlistPanelField;
        private static FieldInfo _handbookEntityNewNodeObjectField;
        private static FieldInfo _entitiesPanelContentField;
        private static FieldInfo _entitiesPanelElementField;

        public void Init(SpawnVisualizer viz, ManualLogSource log)
        {
            _viz = viz;
            _log = log;
        }

        private void OnDestroy()
        {
            ReleaseInputCapture();
            DestroyRoot();
        }

        private void Update()
        {
            if (!Plugin.UseTarkovUIForEditor.Value)
            {
                DestroyRoot();
                ReleaseInputCapture();
                return;
            }

            if (!LootEditorGUI.Open || _viz == null || _viz.ActiveSpawn == null)
            {
                DestroyRoot();
                ReleaseInputCapture();
                ResetWindowSessionState();
                return;
            }

            if (_presetScreenOpen)
            {
                HideRoot();
                if (!PresetPreviewBridge.IsPreviewOpen)
                {
                    _presetScreenOpen = false;
                    _ignoreNativeCloseUntil = Time.unscaledTime + NativeCloseAfterPresetReturnGraceSeconds;
                    PresetPreviewBridge.ForceRestoreBattleUiNow();
                    if (PresetPreviewBridge.ConsumeLastAppliedChanges(out var previewEditMessage))
                    {
                        AcceptPendingPreviewUndoSnapshot();
                        if (_viz.ActiveSpawn != null)
                        {
                            _viz.ActiveSpawn.DataVersion++;
                        }

                        _viz.MarkActiveUnsaved();
                    }
                    else
                    {
                        ClearPendingPreviewUndoSnapshot();
                    }

                    if (!string.IsNullOrWhiteSpace(previewEditMessage))
                    {
                        ShowStatus(previewEditMessage, 6f);
                    }

                    _lastBuildKey = string.Empty;
                }

                return;
            }

            EnsureRoot();
            MaintainInputCapture();
            ClampActiveWindowToScreen();

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CloseEditor(save: false);
                return;
            }

            SyncActiveSpawnSession();
            UpdateStatusLabel();
            var buildKey = BuildUiKey();
            if (!string.Equals(buildKey, _lastBuildKey, StringComparison.Ordinal))
            {
                _lastBuildKey = buildKey;
                RebuildContent();
            }
        }

        private void LateUpdate()
        {
            ClampActiveWindowToScreen();
            UpdateNativeChanceCaret();
        }

        private void EnsureRoot()
        {
            if (_root != null)
            {
                EnsureEditorEventSystem();
                _root.SetActive(true);
                if (!_usingNativeInspectWindowContainer)
                {
                    EnsureEditorCanvasLayer();
                }

                if (_usingInspectWindowShell)
                {
                    EnsureInspectWindowInputLayer();
                }
                return;
            }

            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            EnsureEditorEventSystem();

            if (TryCreateInspectWindowShell())
            {
                _lastBuildKey = string.Empty;
                return;
            }

            _root = new GameObject("ULE_TarkovStyleEditor");
            var uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                _root.layer = uiLayer;
            }
            _root.transform.SetParent(transform, false);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = EditorCanvasSortingOrder;

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _root.AddComponent<GraphicRaycaster>();

            _window = CreateRect("Window", _root.transform);
            _window.anchorMin = new Vector2(0f, 1f);
            _window.anchorMax = new Vector2(0f, 1f);
            _window.pivot = new Vector2(0f, 1f);
            _window.anchoredPosition = new Vector2(110f, -105f);
            _window.sizeDelta = new Vector2(DefaultWindowWidth, DefaultWindowHeight);
            AddImage(_window.gameObject, PanelColor);

            var outline = _window.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.62f, 0.66f, 0.60f);
            outline.effectDistance = new Vector2(1f, -1f);

            var vertical = _window.gameObject.AddComponent<VerticalLayoutGroup>();
            vertical.spacing = 6f;
            vertical.padding = new RectOffset(10, 10, 8, 10);
            vertical.childControlWidth = true;
            vertical.childControlHeight = false;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = false;

            var dragTarget = _window.gameObject.AddComponent<DraggableWindow>();
            dragTarget.Init(_window);

            var resizeHandle = CreatePanel(_window, "ResizeHandle", new Color(0.45f, 0.50f, 0.52f, 0.45f));
            resizeHandle.anchorMin = new Vector2(1f, 0f);
            resizeHandle.anchorMax = new Vector2(1f, 0f);
            resizeHandle.pivot = new Vector2(1f, 0f);
            resizeHandle.anchoredPosition = new Vector2(-3f, 3f);
            resizeHandle.sizeDelta = new Vector2(18f, 18f);
            resizeHandle.gameObject.AddComponent<ResizableWindow>().Init(_window, MinWindowWidth, MinWindowHeight);

            _content = CreateRect("Content", _window);
            var contentLayout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 6f;
            contentLayout.padding = new RectOffset(0, 0, 0, 0);
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = false;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            var contentElement = _content.gameObject.AddComponent<LayoutElement>();
            contentElement.flexibleHeight = 1f;
            contentElement.minHeight = 1f;

            _lastBuildKey = string.Empty;
        }

        private void EnsureEditorCanvasLayer()
        {
            if (_root == null)
            {
                return;
            }

            var canvas = _root.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.overrideSorting = true;
                if (canvas.sortingOrder != EditorCanvasSortingOrder)
                {
                    canvas.sortingOrder = EditorCanvasSortingOrder;
                }
            }
        }

        private bool TryCreateInspectWindowShell()
        {
            try
            {
                var itemUiContext = ItemUiContext.Instance;
                if (itemUiContext == null)
                {
                    return false;
                }

                var template = GetPrivateField<InfoWindow>(itemUiContext, "_infoWindowTemplate");
                if (template == null)
                {
                    return false;
                }

                var uiLayer = LayerMask.NameToLayer("UI");
                var nativeContainer = GetPrivateField<RectTransform>(itemUiContext, "_infoWindowsContainer");
                if (nativeContainer != null)
                {
                    _inspectWindowShell = Instantiate(template, nativeContainer, false);
                    _root = _inspectWindowShell.gameObject;
                    _usingNativeInspectWindowContainer = true;
                }
                else
                {
                    _root = new GameObject("ULE_TarkovStyleInspectEditor");
                    if (uiLayer >= 0)
                    {
                        _root.layer = uiLayer;
                    }

                    _root.transform.SetParent(transform, false);
                    var canvas = _root.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = EditorCanvasSortingOrder;

                    var scaler = _root.AddComponent<CanvasScaler>();
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920f, 1080f);
                    scaler.matchWidthOrHeight = 0.5f;

                    _root.AddComponent<GraphicRaycaster>();

                    _inspectWindowShell = Instantiate(template, _root.transform, false);
                }

                _inspectWindowShell.name = "ULE_SpawnEditor_InspectWindow";
                _inspectWindowShell.gameObject.SetActive(true);
                SetLayerRecursively(_inspectWindowShell.gameObject, uiLayer);
                _usingInspectWindowShell = true;

                _inspectPanelShell = _inspectWindowShell.GetComponentInChildren<ItemSpecificationPanel>(true);
                _window = ResolveInspectWindowRect(_inspectWindowShell, _inspectPanelShell);
                if (_inspectWindowShell != null)
                {
                    _inspectWindowShell.enabled = false;
                }

                _inspectInputWindow = _inspectWindowShell.gameObject.GetComponent<UleSpawnEditorWindow>()
                    ?? _inspectWindowShell.gameObject.AddComponent<UleSpawnEditorWindow>();
                _inspectInputWindow.InitFromInspectTemplate(_inspectWindowShell, _window, HandleNativeWindowCloseRequest);

                EnsureInspectWindowInputLayer();
                NormalizeInspectWindowInteractionState();
                ConfigureInspectWindowRect(_window);
                ConfigureInspectWindowCaption(_inspectWindowShell, _inspectPanelShell, null);
                ConfigureInspectCloseButton(_inspectWindowShell, _inspectPanelShell);
                ReinitializeInspectWindowInteractions(_inspectWindowShell, _inspectPanelShell);

                var host = ConfigureInspectWindowContents(_inspectWindowShell, _inspectPanelShell);
                if (host == null)
                {
                    DestroyRoot();
                    return false;
                }

                _content = CreateRect("ULE Spawn Editor Content", host);
                Stretch(_content, 0f, 0f, 0f, 0f);

                var contentLayout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
                contentLayout.spacing = 6f;
                contentLayout.padding = new RectOffset(0, 0, 0, 0);
                contentLayout.childControlWidth = true;
                contentLayout.childControlHeight = false;
                contentLayout.childForceExpandWidth = true;
                contentLayout.childForceExpandHeight = false;

                var contentElement = _content.gameObject.AddComponent<LayoutElement>();
                contentElement.flexibleHeight = 1f;
                contentElement.minHeight = 1f;

                if (_inspectPanelShell != null)
                {
                    _inspectPanelShell.enabled = false;
                }

                RegisterInspectInputWindow(itemUiContext);
                return true;
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to create inspect-window editor shell, using fallback UI: {ex.Message}");
                DestroyRoot();
                return false;
            }
        }

        private static T GetPrivateField<T>(object instance, string fieldName) where T : class
        {
            return instance?
                .GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(instance) as T;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null || layer < 0)
            {
                return;
            }

            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private void RegisterInspectInputWindow(ItemUiContext itemUiContext)
        {
            if (itemUiContext == null || _inspectInputWindow == null)
            {
                return;
            }

            try
            {
                var context = _inspectInputWindow.Show();
                itemUiContext.method_10<GClass3829>(_inspectInputWindow, context);
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to register native inspect-window editor input: {ex.Message}");
            }
        }

        private void UnregisterInspectInputWindow()
        {
            if (_inspectInputWindow == null && _inspectWindowShell == null)
            {
                return;
            }

            try
            {
                _inspectInputWindow?.CloseSilently();

                var itemUiContext = ItemUiContext.Instance;
                if (itemUiContext == null)
                {
                    return;
                }

                RemoveMatchingPrivateListEntries(itemUiContext, "_children", entry =>
                    ReferenceEquals(entry, _inspectInputWindow) || ReferenceEquals(entry, _inspectWindowShell));

                RemoveMatchingPrivateListEntries(itemUiContext, "list_1", entry =>
                {
                    if (entry == null)
                    {
                        return false;
                    }

                    var window = entry
                        .GetType()
                        .GetField("Window", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.GetValue(entry);

                    return ReferenceEquals(window, _inspectInputWindow) || ReferenceEquals(window, _inspectWindowShell);
                });
            }
            catch (Exception ex)
            {
                _log?.LogDebug($"[ULE] Failed to unregister native inspect-window editor input: {ex.Message}");
            }
        }

        private static void RemoveMatchingPrivateListEntries(object owner, string fieldName, Func<object, bool> shouldRemove)
        {
            var list = owner?
                .GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(owner) as System.Collections.IList;
            if (list == null || shouldRemove == null)
            {
                return;
            }

            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (shouldRemove(list[i]))
                {
                    list.RemoveAt(i);
                }
            }
        }

        private static RectTransform ResolveInspectWindowRect(InfoWindow window, ItemSpecificationPanel panel)
        {
            if (window?.WindowTransform != null)
            {
                return window.WindowTransform;
            }

            if (panel != null)
            {
                return panel.transform as RectTransform;
            }

            return window != null ? window.transform as RectTransform : null;
        }

        private static void ConfigureInspectWindowRect(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(96f, -70f);

            var maxWidth = Mathf.Max(MinWindowWidth, Screen.width - 160f);
            var maxHeight = Mathf.Max(MinWindowHeight, Screen.height - 120f);
            var targetWidth = Mathf.Min(DefaultWindowWidth, maxWidth);
            var targetHeight = Mathf.Min(DefaultWindowHeight, maxHeight);

            var layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = targetWidth;
            layout.preferredHeight = targetHeight;
            layout.minWidth = MinWindowWidth;
            layout.minHeight = MinWindowHeight;
            rect.sizeDelta = new Vector2(targetWidth, targetHeight);

            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        }

        private static void ConfigureInspectWindowFrameLayout(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var windowRect = root as RectTransform;
            DisableDrivenLayout(windowRect);

            var inner = root.Find("Inner") as RectTransform;
            var border = root.Find("Inner/Border") as RectTransform;
            var captionPanel = root.Find("Inner/Caption Panel") as RectTransform;
            var contents = root.Find("Inner/Contents") as RectTransform;
            var stretchButtons = root.Find("StretchButtons") as RectTransform;

            DisableDrivenLayout(inner);
            DisableDrivenLayout(border);
            DisableDrivenLayout(captionPanel);
            DisableDrivenLayout(contents);
            DisableDrivenLayout(stretchButtons);

            if (inner != null)
            {
                inner.gameObject.SetActive(true);
                Stretch(inner);
            }

            if (border != null)
            {
                border.gameObject.SetActive(true);
                Stretch(border, -3f, -3f, -3f, -3f);
                foreach (var graphic in border.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.raycastTarget = false;
                }
            }

            var captionHeight = GetNativeRectHeight(captionPanel, 30f);
            if (captionPanel != null)
            {
                captionPanel.gameObject.SetActive(true);
                captionPanel.anchorMin = new Vector2(0f, 1f);
                captionPanel.anchorMax = new Vector2(1f, 1f);
                captionPanel.pivot = new Vector2(0.5f, 1f);
                captionPanel.offsetMin = new Vector2(0f, -captionHeight);
                captionPanel.offsetMax = Vector2.zero;
            }

            if (contents != null)
            {
                contents.gameObject.SetActive(true);
                contents.anchorMin = Vector2.zero;
                contents.anchorMax = Vector2.one;
                contents.offsetMin = Vector2.zero;
                contents.offsetMax = new Vector2(0f, -captionHeight);
            }

            if (stretchButtons != null)
            {
                stretchButtons.gameObject.SetActive(true);
                Stretch(stretchButtons);
            }

            if (windowRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(windowRect);
            }
        }

        private static void DisableDrivenLayout(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            foreach (var layoutGroup in rect.GetComponents<LayoutGroup>())
            {
                layoutGroup.enabled = false;
            }

            foreach (var contentSizeFitter in rect.GetComponents<ContentSizeFitter>())
            {
                contentSizeFitter.enabled = false;
            }
        }

        private static float GetNativeRectHeight(RectTransform rect, float fallback)
        {
            if (rect == null)
            {
                return fallback;
            }

            var layout = rect.GetComponent<LayoutElement>();
            if (layout != null)
            {
                if (layout.preferredHeight > 1f)
                {
                    return layout.preferredHeight;
                }

                if (layout.minHeight > 1f)
                {
                    return layout.minHeight;
                }
            }

            if (rect.sizeDelta.y > 1f)
            {
                return rect.sizeDelta.y;
            }

            if (rect.rect.height > 1f)
            {
                return rect.rect.height;
            }

            return fallback;
        }

        private void ConfigureInspectWindowCaption(InfoWindow window, ItemSpecificationPanel panel, SpawnPointData spawn)
        {
            var caption = window?.Caption;
            if (caption == null && panel != null)
            {
                caption = panel.transform.Find("Inner/Caption Panel")?.GetComponentInChildren<TextMeshProUGUI>(true);
            }

            if (caption != null)
            {
                caption.text = "Ultimate Loot Editor - Spawn Editor";
                caption.SetAllDirty();
            }

            var root = ResolveInspectWindowRoot(window, panel);
            if (root == null)
            {
                return;
            }

            var itemType = root.Find("Inner/Caption Panel/Item Type");
            SetNativeLabelText(itemType, string.Empty);
            if (itemType != null)
            {
                itemType.gameObject.SetActive(false);
            }

            SetNativeLabelText(root.Find("Inner/Caption Panel/MassText"), string.Empty);

            var massText = root.Find("Inner/Caption Panel/MassText");
            if (massText != null)
            {
                massText.gameObject.SetActive(false);
            }

            var tagPanel = root.Find("Inner/Caption Panel/TagPanel");
            if (tagPanel != null)
            {
                tagPanel.gameObject.SetActive(false);
            }
        }

        private static void SetNativeLabelText(Transform transform, string text)
        {
            if (transform == null)
            {
                return;
            }

            foreach (var tmp in transform.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                tmp.text = text ?? string.Empty;
                tmp.SetAllDirty();
            }

            foreach (var label in transform.GetComponentsInChildren<Text>(true))
            {
                label.text = text ?? string.Empty;
                label.SetAllDirty();
            }
        }

        private static void ConfigureCompactButtonRow(RectTransform row, float spacing, float leftPadding = 0f)
        {
            if (row == null)
            {
                return;
            }

            var layout = row.GetComponent<HorizontalLayoutGroup>();
            if (layout == null)
            {
                return;
            }

            layout.spacing = spacing;
            layout.padding = new RectOffset(Mathf.RoundToInt(leftPadding), 8, 0, 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
        }

        private static void ConfigureNativeButtonSize(SimpleContextMenuButton button, float width, float height)
        {
            if (button == null)
            {
                return;
            }

            var root = button.Transform as RectTransform;
            if (root != null)
            {
                SetLayout(root, minWidth: width, preferredWidth: width, minHeight: height, preferredHeight: height, flexibleWidth: 0f, flexibleHeight: 0f);
                root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }

            foreach (var element in button.GetComponentsInChildren<LayoutElement>(true))
            {
                element.minHeight = height;
                element.preferredHeight = height;
                element.flexibleHeight = 0f;
                if (element.transform == root)
                {
                    element.minWidth = width;
                    element.preferredWidth = width;
                    element.flexibleWidth = 0f;
                }
            }

            foreach (var fitter in button.GetComponentsInChildren<ContentSizeFitter>(true))
            {
                fitter.enabled = false;
            }
        }

        private void ConfigureInspectCloseButton(InfoWindow window, ItemSpecificationPanel panel)
        {
            Button closeButton = null;
            var root = ResolveInspectWindowRoot(window, panel);
            if (root != null)
            {
                closeButton = root.Find("Inner/Caption Panel/Close Button")?.GetComponent<Button>();
            }

            if (closeButton == null)
            {
                closeButton = window?.CloseButton;
            }

            if (closeButton == null)
            {
                return;
            }

            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(() => CloseEditor(save: false));
        }

        private RectTransform ConfigureInspectWindowContents(InfoWindow window, ItemSpecificationPanel panel)
        {
            var root = ResolveInspectWindowRoot(window, panel);
            if (root == null)
            {
                return null;
            }

            var contents = root.Find("Inner/Contents") as RectTransform;
            if (contents == null)
            {
                return root.Find("Inner") as RectTransform ?? root as RectTransform;
            }

            contents.gameObject.SetActive(true);

            var preview = contents.Find("Preview Panel") as RectTransform;
            if (preview != null)
            {
                preview.gameObject.SetActive(true);
                DisableInspectPreviewRuntimeVisuals(preview);
                foreach (var graphic in preview.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.raycastTarget = false;
                }
            }

            var interactionPanel = contents.Find("InteractionButtonsPanel") as RectTransform;
            if (interactionPanel != null)
            {
                interactionPanel.gameObject.SetActive(true);

                var modding = interactionPanel.Find("Modding");
                if (modding != null)
                {
                    modding.gameObject.SetActive(false);
                }

                _inspectActionContainer = interactionPanel
                    .Find("InteractionButtonsContainer")
                    ?.GetComponent<InteractionButtonsContainer>();
                _inspectActionTemplate = GetPrivateField<SimpleContextMenuButton>(_inspectActionContainer, "_buttonTemplate");
                var nativeActionHost = GetPrivateField<RectTransform>(_inspectActionContainer, "_buttonsContainer");
                _inspectFooterPanel = interactionPanel;
                ConfigureInspectFooterPanel(interactionPanel);
                _inspectActionHost = nativeActionHost ?? _inspectActionContainer?.transform as RectTransform;
                var actionContainerRect = _inspectActionContainer != null ? _inspectActionContainer.transform as RectTransform : null;
                if (actionContainerRect != null)
                {
                    actionContainerRect.gameObject.SetActive(true);
                    Stretch(actionContainerRect);
                    SetLayoutIgnore(actionContainerRect, true);
                }

                if (_inspectActionHost != null)
                {
                    _inspectActionHost.gameObject.SetActive(true);
                    ConfigureCompactButtonRow(_inspectActionHost, NativeButtonSpacing);
                }

                ResetInspectActionButtons();
            }

            var descriptionHost = contents.Find("DescriptionPanel") as RectTransform;
            if (descriptionHost != null)
            {
                descriptionHost.gameObject.SetActive(true);
                for (var i = descriptionHost.childCount - 1; i >= 0; i--)
                {
                    var child = descriptionHost.GetChild(i);
                    if (child.name.StartsWith("ULE ", StringComparison.Ordinal))
                    {
                        Destroy(child.gameObject);
                    }
                    else
                    {
                        child.gameObject.SetActive(false);
                    }
                }
            }

            return preview ?? descriptionHost ?? contents;
        }

        private void ConfigureInspectFooterPanel(RectTransform interactionPanel)
        {
            if (interactionPanel == null)
            {
                return;
            }

            interactionPanel.anchorMin = new Vector2(0f, 0f);
            interactionPanel.anchorMax = new Vector2(1f, 0f);
            interactionPanel.pivot = new Vector2(0.5f, 0f);
            interactionPanel.offsetMin = Vector2.zero;
            interactionPanel.offsetMax = new Vector2(0f, NativeFooterHeight);
            interactionPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, NativeFooterHeight);
            SetLayout(interactionPanel, minHeight: NativeFooterHeight, preferredHeight: NativeFooterHeight, flexibleHeight: 0f);
            SetLayoutIgnore(interactionPanel, true);
            interactionPanel.SetAsLastSibling();
            _inspectPanelBackgroundTemplate = FindNativePanelBackgroundTemplate(interactionPanel);
            _inspectPanelBackgroundGraphicTemplate = ResolveNativePanelBackgroundGraphic(interactionPanel, _inspectPanelBackgroundTemplate);
            StretchNativePanelBackgrounds(interactionPanel);
            var footerBackground = EnsureNativePanelBackgroundClone(interactionPanel, "ULE_NativeFooterBackground");
            if (footerBackground != null)
            {
                Stretch(footerBackground, 0f, NativeVisualPixel, 0f, 0f);
            }
        }

        private static Graphic ResolveNativePanelBackgroundGraphic(RectTransform panel, RectTransform template)
        {
            var rootGraphic = panel != null ? panel.GetComponent<Graphic>() : null;
            if (rootGraphic != null)
            {
                return rootGraphic;
            }

            var candidate = FindNativePanelBackgroundGraphicCandidate(panel, requireNameHint: true)
                ?? FindNativePanelBackgroundGraphicCandidate(panel, requireNameHint: false);
            if (candidate != null)
            {
                return candidate;
            }

            return template != null ? template.GetComponent<Graphic>() : null;
        }

        private RectTransform FindNativePanelBackgroundTemplate(RectTransform panel)
        {
            if (panel == null)
            {
                return null;
            }

            var best = FindNativePanelBackgroundCandidate(panel, requireNameHint: true)
                ?? FindNativePanelBackgroundCandidate(panel, requireNameHint: false);
            return best;
        }

        private static RectTransform FindNativePanelBackgroundCandidate(RectTransform panel, bool requireNameHint)
        {
            foreach (var rect in panel.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect == null)
                {
                    continue;
                }

                if (!CanUseAsNativePanelBackground(rect, panel, requireNameHint))
                {
                    continue;
                }

                return rect;
            }

            return null;
        }

        private static Graphic FindNativePanelBackgroundGraphicCandidate(RectTransform panel, bool requireNameHint)
        {
            if (panel == null)
            {
                return null;
            }

            Graphic best = null;
            var bestScore = -1f;
            foreach (var graphic in panel.GetComponentsInChildren<Graphic>(true))
            {
                var rect = graphic != null ? graphic.transform as RectTransform : null;
                if (rect == null || !CanUseAsNativePanelBackground(rect, panel, requireNameHint))
                {
                    continue;
                }

                var area = Mathf.Max(rect.rect.width, rect.sizeDelta.x) * Mathf.Max(rect.rect.height, rect.sizeDelta.y);
                if (area > bestScore)
                {
                    best = graphic;
                    bestScore = area;
                }
            }

            return best;
        }

        private static bool CanUseAsNativePanelBackground(RectTransform rect, RectTransform root, bool requireNameHint)
        {
            if (rect == null || rect.GetComponent<Graphic>() == null)
            {
                return false;
            }

            var width = Mathf.Max(rect.rect.width, rect.sizeDelta.x);
            var height = Mathf.Max(rect.rect.height, rect.sizeDelta.y);
            if (!ReferenceEquals(rect, root) && (width < 300f || height < 24f))
            {
                return false;
            }

            var ancestor = rect.parent;
            while (ancestor != null && ancestor != root)
            {
                if (ancestor.GetComponent<Button>() != null ||
                    ancestor.GetComponent<TMP_InputField>() != null ||
                    ancestor.GetComponent<Scrollbar>() != null ||
                    ancestor.GetComponent<Slider>() != null)
                {
                    return false;
                }

                ancestor = ancestor.parent;
            }

            if (requireNameHint)
            {
                var name = rect.name.ToLowerInvariant();
                if (!name.Contains("background") &&
                    !name.Contains("back") &&
                    !name.Contains("bg") &&
                    !name.Contains("panel"))
                {
                    return false;
                }
            }

            return rect.GetComponent<Button>() == null &&
                   rect.GetComponent<TMP_InputField>() == null &&
                   rect.GetComponent<Scrollbar>() == null &&
                   rect.GetComponent<Slider>() == null &&
                   rect.GetComponentInChildren<Button>(true) == null &&
                   rect.GetComponentInChildren<TMP_InputField>(true) == null &&
                   rect.GetComponentInChildren<TMP_Text>(true) == null &&
                   rect.GetComponentInChildren<Text>(true) == null;
        }

        private RectTransform EnsureNativePanelBackgroundClone(RectTransform parent, string name)
        {
            if (parent == null)
            {
                return null;
            }

            var existing = parent.Find(name) as RectTransform;
            if (existing != null)
            {
                existing.gameObject.SetActive(false);
                Destroy(existing.gameObject);
            }

            GameObject clone;
            if (_inspectPanelBackgroundGraphicTemplate != null)
            {
                clone = new GameObject(name, typeof(RectTransform));
                clone.transform.SetParent(parent, false);
                CopyNativeGraphic(_inspectPanelBackgroundGraphicTemplate, clone);
            }
            else if (_inspectPanelBackgroundTemplate != null)
            {
                clone = Instantiate(_inspectPanelBackgroundTemplate.gameObject, parent, false);
                StripInteractiveChildren(clone);
            }
            else
            {
                return null;
            }

            clone.name = name;
            clone.SetActive(true);
            SetLayerRecursively(clone, parent.gameObject.layer);
            var rect = clone.transform as RectTransform;
            Stretch(rect);
            rect?.SetAsFirstSibling();
            foreach (var graphic in clone.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }

            return rect;
        }

        private static void CopyNativeGraphic(Graphic source, GameObject target)
        {
            if (source is Image sourceImage)
            {
                var image = target.AddComponent<Image>();
                image.sprite = sourceImage.sprite;
                image.overrideSprite = sourceImage.overrideSprite;
                image.type = sourceImage.type;
                image.preserveAspect = sourceImage.preserveAspect;
                image.fillCenter = true;
                image.fillMethod = sourceImage.fillMethod;
                image.fillOrigin = sourceImage.fillOrigin;
                image.fillAmount = sourceImage.fillAmount;
                image.material = sourceImage.material;
                image.color = sourceImage.color;
                image.raycastTarget = false;
                return;
            }

            if (source is RawImage sourceRaw)
            {
                var raw = target.AddComponent<RawImage>();
                raw.texture = sourceRaw.texture;
                raw.uvRect = sourceRaw.uvRect;
                raw.material = sourceRaw.material;
                raw.color = sourceRaw.color;
                raw.raycastTarget = false;
                return;
            }

            var fallback = target.AddComponent<Image>();
            fallback.material = source.material;
            fallback.color = source.color;
            fallback.raycastTarget = false;
        }

        private static void StripInteractiveChildren(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            for (var i = root.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(root.transform.GetChild(i).gameObject);
            }

            foreach (var component in root.GetComponents<Component>())
            {
                if (component == null ||
                    component is Transform ||
                    component is RectTransform ||
                    component is Graphic ||
                    component is CanvasRenderer)
                {
                    continue;
                }

                Destroy(component);
            }
        }

        private static void StretchNativePanelBackgrounds(RectTransform panel)
        {
            if (panel == null)
            {
                return;
            }

            var rootGraphic = panel.GetComponent<Graphic>();
            if (rootGraphic != null)
            {
                rootGraphic.raycastTarget = false;
            }

            for (var i = 0; i < panel.childCount; i++)
            {
                var child = panel.GetChild(i) as RectTransform;
                if (child == null)
                {
                    continue;
                }

                if (child.GetComponent<Button>() != null ||
                    child.GetComponent<TMP_InputField>() != null ||
                    child.GetComponent<Scrollbar>() != null ||
                    child.GetComponent<Slider>() != null ||
                    child.GetComponentInChildren<Button>(true) != null ||
                    child.GetComponentInChildren<TMP_InputField>(true) != null ||
                    child.GetComponentInChildren<TMP_Text>(true) != null ||
                    child.GetComponentInChildren<Text>(true) != null)
                {
                    continue;
                }

                var graphic = child.GetComponent<Graphic>();
                if (graphic == null)
                {
                    continue;
                }

                Stretch(child);
                child.SetAsFirstSibling();
                graphic.raycastTarget = false;
            }
        }

        private static void DisableInspectPreviewRuntimeVisuals(RectTransform preview)
        {
            if (preview == null)
            {
                return;
            }

            foreach (var graphic in preview.GetComponents<Graphic>())
            {
                graphic.enabled = false;
                graphic.raycastTarget = false;
            }

            for (var i = 0; i < preview.childCount; i++)
            {
                preview.GetChild(i).gameObject.SetActive(false);
            }

            foreach (var component in preview.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                var typeName = component.GetType().Name;
                if ((typeName.IndexOf("CameraViewporter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     typeName.IndexOf("DragTrigger", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    component is Behaviour behaviour)
                {
                    behaviour.enabled = false;
                }
            }
        }

        private static Transform ResolveInspectWindowRoot(InfoWindow window, ItemSpecificationPanel panel)
        {
            if (panel != null)
            {
                return panel.transform;
            }

            return window != null ? window.transform : null;
        }

        private void EnsureInspectWindowInputLayer()
        {
            if (_root == null)
            {
                return;
            }

            var group = _root.GetComponent<CanvasGroup>() ?? _root.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
            group.ignoreParentGroups = !_usingNativeInspectWindowContainer;

            if (_inspectInputWindow != null && _inspectInputWindow.WindowContext == null)
            {
                _inspectInputWindow.WindowContext = new GClass3829();
            }
        }

        private void ClampActiveWindowToScreen()
        {
            if (_window == null)
            {
                return;
            }

            _window.anchoredPosition = ClampWindowPosition(_window, _window.anchoredPosition);
        }

        private void ReinitializeInspectWindowInteractions(InfoWindow window, ItemSpecificationPanel panel)
        {
            var root = ResolveInspectWindowRoot(window, panel);
            if (root == null || _window == null)
            {
                return;
            }

            var captionPanel = root.Find("Inner/Caption Panel") as RectTransform;
            if (captionPanel != null)
            {
                foreach (var graphic in captionPanel.GetComponents<Graphic>())
                {
                    graphic.raycastTarget = true;
                }

                foreach (var drag in captionPanel.GetComponents<UIDragComponent>())
                {
                    drag.enabled = _usingNativeInspectWindowContainer;
                }

                if (_usingNativeInspectWindowContainer)
                {
                    if (captionPanel.GetComponent<UIDragComponent>() == null)
                    {
                        captionPanel.gameObject.AddComponent<UIDragComponent>().Init(_window, true);
                    }

                    var fallbackDrag = captionPanel.GetComponent<DraggableWindow>();
                    if (fallbackDrag != null)
                    {
                        Destroy(fallbackDrag);
                    }
                }
                else
                {
                    var fallbackDrag = captionPanel.GetComponent<DraggableWindow>() ?? captionPanel.gameObject.AddComponent<DraggableWindow>();
                    fallbackDrag.Init(_window);
                }
            }

            var closeButton = root.Find("Inner/Caption Panel/Close Button")?.GetComponent<Button>() ?? window?.CloseButton;
            if (closeButton != null)
            {
                closeButton.interactable = true;
                foreach (var graphic in closeButton.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.raycastTarget = true;
                }
            }

            var layout = _window.GetComponent<LayoutElement>() ?? _window.gameObject.AddComponent<LayoutElement>();
            var stretchRoot = root.Find("StretchButtons");
            if (stretchRoot == null)
            {
                EnsureFallbackResizeHandle();
                return;
            }

            var maxSize = new Vector2(Mathf.Max(MinWindowWidth, Screen.width - WindowMargin), Mathf.Max(MinWindowHeight, Screen.height - WindowMargin));
            foreach (var stretchArea in stretchRoot.GetComponentsInChildren<StretchArea>(true))
            {
                if (!TryGetStretchAreaType(stretchArea.name, out var type))
                {
                    continue;
                }

                foreach (var graphic in stretchArea.GetComponents<Graphic>())
                {
                    graphic.raycastTarget = true;
                }

                stretchArea.Init(type, _window, layout, maxSize);
                stretchArea.OnCursorChange.Subscribe(new Action<ECursorType?>(SetEditorHoverCursor));
                stretchArea.OnForcedCursorChange.Subscribe(new Action<ECursorType?>(SetEditorForcedCursor));

                if (stretchArea.name.IndexOf("bottom", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    stretchArea.name.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var fallbackResize = stretchArea.GetComponent<ResizableWindow>() ?? stretchArea.gameObject.AddComponent<ResizableWindow>();
                    fallbackResize.Init(_window, MinWindowWidth, MinWindowHeight);
                }
            }

            EnsureFallbackResizeHandle();
        }

        private void EnsureFallbackResizeHandle()
        {
            if (_window == null)
            {
                return;
            }

            var existing = _window.Find("ULE Resize Handle") as RectTransform;
            var handle = existing ?? CreateRect("ULE Resize Handle", _window);
            handle.anchorMin = new Vector2(1f, 0f);
            handle.anchorMax = new Vector2(1f, 0f);
            handle.pivot = new Vector2(1f, 0f);
            handle.anchoredPosition = new Vector2(-3f, 3f);
            handle.sizeDelta = new Vector2(28f, 28f);
            handle.SetAsLastSibling();

            var image = handle.GetComponent<Image>() ?? handle.gameObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.001f);
            image.raycastTarget = true;

            var fallbackResize = handle.GetComponent<ResizableWindow>() ?? handle.gameObject.AddComponent<ResizableWindow>();
            fallbackResize.Init(_window, MinWindowWidth, MinWindowHeight);

            var cursorZone = handle.GetComponent<ResizeCursorZone>() ?? handle.gameObject.AddComponent<ResizeCursorZone>();
            cursorZone.Init(SetEditorHoverCursor, SetEditorForcedCursor);
        }

        private static bool TryGetStretchAreaType(string objectName, out StretchArea.EStretchAreaType type)
        {
            var name = (objectName ?? string.Empty).ToLowerInvariant();
            var top = name.Contains("top");
            var bottom = name.Contains("bottom");
            var left = name.Contains("left");
            var right = name.Contains("right");

            if (top && left)
            {
                type = StretchArea.EStretchAreaType.TopLeft;
                return true;
            }

            if (top && right)
            {
                type = StretchArea.EStretchAreaType.TopRight;
                return true;
            }

            if (bottom && left)
            {
                type = StretchArea.EStretchAreaType.BottomLeft;
                return true;
            }

            if (bottom && right)
            {
                type = StretchArea.EStretchAreaType.BottomRight;
                return true;
            }

            if (top)
            {
                type = StretchArea.EStretchAreaType.Top;
                return true;
            }

            if (bottom)
            {
                type = StretchArea.EStretchAreaType.Bottom;
                return true;
            }

            if (left)
            {
                type = StretchArea.EStretchAreaType.Left;
                return true;
            }

            if (right)
            {
                type = StretchArea.EStretchAreaType.Right;
                return true;
            }

            type = default;
            return false;
        }

        private void RebuildContent()
        {
            if (_content == null)
            {
                return;
            }

            ClearChildren(_content);
            var spawn = _viz.ActiveSpawn;
            if (spawn == null)
            {
                return;
            }

            if (_usingInspectWindowShell)
            {
                RebuildInspectShellContent(spawn);
                return;
            }

            BuildHeader(spawn);
            BuildStatusRow();

            if (_viz.IsActiveSpawnLoading)
            {
                BuildCenteredMessage("Loading full spawn details...");
                return;
            }

            if (!spawn.DetailsLoaded)
            {
                var error = _viz.ActiveSpawnLoadError;
                BuildCenteredMessage(string.IsNullOrWhiteSpace(error)
                    ? "Spawn details are not ready yet."
                    : error);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    BuildButtonRow(
                        CreateButtonSpec("Retry", () => _viz.RetryActiveSpawnDetails()),
                        CreateButtonSpec("Close", () => CloseEditor(save: false)));
                }
                return;
            }

            BuildModeAndNavigation(spawn);

            var vanilla = _viz.ActiveVanillaSpawn;
            if (_viewMode == EditorViewMode.Compare)
            {
                BuildCompareView(vanilla, spawn);
            }
            else
            {
                var editable = _viewMode == EditorViewMode.Edited;
                var displaySpawn = _viewMode == EditorViewMode.Vanilla && vanilla != null ? vanilla : spawn;
                BuildSpawnChance(displaySpawn, editable, vanilla);
                BuildItemList(displaySpawn, editable);
                if (editable)
                {
                    BuildAddItemSection(spawn);
                }
                else if (_viz.IsActiveVanillaLoading && (vanilla == null || !vanilla.DetailsLoaded))
                {
                    BuildSmallText("Vanilla loot table loading...", SubtleTextColor);
                }
            }
        }

        private void BuildHeader(SpawnPointData spawn)
        {
            if (_usingInspectWindowShell)
            {
                ConfigureInspectWindowCaption(_inspectWindowShell, _inspectPanelShell, spawn);
                ConfigureInspectActionButtons(spawn);
                return;
            }

            var header = CreatePanel(_content, "Header", HeaderColor);
            SetLayout(header, minHeight: 84f);
            var layout = header.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 5f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            var top = CreateRect("Top", header);
            SetLayout(top, minHeight: 30f);
            var topLayout = top.gameObject.AddComponent<HorizontalLayoutGroup>();
            topLayout.spacing = 8f;
            topLayout.childControlHeight = true;
            topLayout.childForceExpandHeight = true;
            topLayout.childControlWidth = false;
            topLayout.childForceExpandWidth = false;

            var title = CreateText(top, "Title", "ULTIMATE LOOT EDITOR", 18, FontStyle.Bold, TextColor);
            SetLayout(title.rectTransform, flexibleWidth: 1f, minHeight: 28f);

            CreateButton(top, "Undo", CanUndoActiveSpawn, UndoActiveSpawnChange, width: 70f);
            CreateButton(top, "Redo", CanRedoActiveSpawn, RedoActiveSpawnChange, width: 70f);
            CreateButton(top, "Save", true, () => CloseEditor(save: true), width: 70f);
            CreateButton(top, "X", true, () => CloseEditor(save: false), width: 34f);

            var details = CreateText(
                header,
                "Details",
                $"Spawn ID: {spawn.Id}\nPosition: {spawn.Position.x:0.00}, {spawn.Position.y:0.00}, {spawn.Position.z:0.00}   Possible Items: {spawn.ItemCountSummary}",
                12,
                FontStyle.Normal,
                SubtleTextColor);
            details.alignment = TextAnchor.UpperLeft;
            SetLayout(details.rectTransform, minHeight: 36f);
        }

        private void RebuildInspectShellContent(SpawnPointData spawn)
        {
            ConfigureInspectWindowCaption(_inspectWindowShell, _inspectPanelShell, spawn);
            ConfigureInspectActionButtons(spawn);

            if (_viz.IsActiveSpawnLoading)
            {
                BuildNativeSkeletonHeader(spawn);
                CreateNativeLabel(_content, "Loading full spawn details...", 15f, bold: false);
                return;
            }

            if (!spawn.DetailsLoaded)
            {
                BuildNativeSkeletonHeader(spawn);
                var error = _viz.ActiveSpawnLoadError;
                CreateNativeLabel(_content, string.IsNullOrWhiteSpace(error)
                    ? "Spawn details are not ready yet."
                    : error, 15f, bold: false);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    CreateNativeButtonStrip(_content, "RetryRow", GetNativeButtonHeight() + 4f)
                        .AddButton("RETRY", true, () => _viz.RetryActiveSpawnDetails(), NativeButtonWidth)
                        .AddButton("CLOSE", true, () => CloseEditor(save: false), NativeButtonWidth);
                }

                return;
            }

            var vanilla = _viz.ActiveVanillaSpawn;
            var displaySpawn = _viewMode == EditorViewMode.Vanilla && vanilla != null ? vanilla : spawn;

            BuildNativeSkeletonHeader(spawn, displaySpawn, vanilla, _viewMode == EditorViewMode.Edited, includeControls: true);
            CreateNativeMiddleSectionSeams();

            if (_viewMode == EditorViewMode.Compare)
            {
                BuildNativeSkeletonCompare(vanilla, spawn);
            }
            else
            {
                BuildNativeSkeletonItems(displaySpawn);
                if (_viewMode == EditorViewMode.Vanilla && _viz.IsActiveVanillaLoading && (vanilla == null || !vanilla.DetailsLoaded))
                {
                    CreateNativeLabel(_content, "Vanilla loot table loading...", 13f, bold: false);
                }
            }
        }

        private void BuildNativeSkeletonHeader(
            SpawnPointData spawn,
            SpawnPointData displaySpawn = null,
            SpawnPointData vanilla = null,
            bool editable = false,
            bool includeControls = false)
        {
            var header = CreateRect("NativeHeaderBlock", _content);
            SetLayout(header, minHeight: NativeHeaderHeight, preferredHeight: NativeHeaderHeight, flexibleHeight: 0f);
            SetStretchTop(header, NativeResultsSideMargin, 0f, NativeResultsSideMargin, NativeHeaderHeight);
            SetLayout(header, preferredHeight: NativeHeaderHeight, flexibleHeight: 0f);
            SetLayoutIgnore(header, true);
            var headerBackground = EnsureNativePanelBackgroundClone(header, "ULE_NativeHeaderBackground");
            Stretch(headerBackground, -NativeResultsSideMargin, 0f, -NativeResultsSideMargin, 0f);

            var spawnIdLabel = CreateNativeLabelAt(header, $"Spawn ID: {spawn.Id}", 13f, bold: false, 18f, 10f, 780f, 20f);
            SetStretchTop(spawnIdLabel?.rectTransform, 18f, 10f, 18f, 20f);
            var positionLabel = CreateNativeLabelAt(
                header,
                $"Position: ({spawn.Position.x:0.##}, {spawn.Position.y:0.##}, {spawn.Position.z:0.##})     Possible items: {spawn.ItemCountSummary}",
                13f,
                bold: false,
                18f,
                36f,
                760f,
                20f);
            SetStretchTop(positionLabel?.rectTransform, 18f, 36f, 18f, 20f);

            if (!includeControls)
            {
                return;
            }

            BuildNativeSkeletonSpawnChanceInput(header, displaySpawn ?? spawn, vanilla, editable);
            BuildNativeSkeletonModeRow(header);
            BuildNativeSkeletonSearchRow(header);
        }

        private void BuildNativeSkeletonModeRow(Transform header)
        {
            var buttonHeight = GetNativeButtonHeight();
            var canRevert = _viz.ActiveVanillaSpawn != null && _viz.ActiveVanillaSpawn.DetailsLoaded;

            CreateNativeButtonStrip(header, "NativeModeRow", buttonHeight)
                .CenteredAt(88f, NativeButtonWidth * 4f + NativeButtonSpacing * 3f, buttonHeight)
                .AddButton("EDITED", true, () => SetViewMode(EditorViewMode.Edited), NativeButtonWidth, "ModeButtons.Edited", _viewMode == EditorViewMode.Edited)
                .AddButton("VANILLA", true, () => SetViewMode(EditorViewMode.Vanilla), NativeButtonWidth, "ModeButtons.Vanilla", _viewMode == EditorViewMode.Vanilla)
                .AddButton("COMPARE", true, () => SetViewMode(EditorViewMode.Compare), NativeButtonWidth, "ModeButtons.Compare", _viewMode == EditorViewMode.Compare)
                .AddButton("REVERT VANILLA", canRevert, () =>
                {
                    PushUndoSnapshot("revert-vanilla", coalesce: false);
                    if (_viz.RevertActiveToVanilla())
                    {
                        _viewMode = EditorViewMode.Edited;
                        ResetSearchAndFilters();
                        ShowStatus("Reverted to vanilla spawn data.");
                        MarkDirtyAndRebuild();
                    }
                }, NativeButtonWidth, "ModeButtons.RevertVanilla");

            var count = Mathf.Max(1, _viz.EditorCandidateCount);
            var active = Mathf.Clamp(_viz.ActiveEditorCandidateIndex + 1, 1, count);
            CreateNativeButtonStrip(header, "NativeNavigationRow", buttonHeight)
                .RightAt(18f, 62f, 46f * 2f + 70f + NativeButtonSpacing * 2f, buttonHeight)
                .AddButton("<", _viz.CanNavigateEditorPrevious, () => Navigate(-1), 46f, "Navigation.Previous")
                .AddLabel($"{active}/{count}", 70f, "Navigation.PageLabel", TextAlignmentOptions.Center)
                .AddButton(">", _viz.CanNavigateEditorNext, () => Navigate(1), 46f, "Navigation.Next");
        }

        private void SetViewMode(EditorViewMode mode)
        {
            _viewMode = mode;
            _lastBuildKey = string.Empty;
        }

        private void BuildNativeSkeletonSpawnChanceInput(Transform header, SpawnPointData spawn, SpawnPointData vanilla, bool editable)
        {
            EnsureAlwaysSpawnSupportFromVanilla(spawn, vanilla);

            var row = CreateNativeFreeRow(header, "NativeSpawnChanceRow", NativeHeaderInputHeight);
            SetTopLeft(row, 18f, 62f, 430f, NativeHeaderInputHeight);
            var chanceLabel = CreateNativeLabelAt(row, "Spawn Chance:", 13f, bold: false, 0f, 0f, 114f, NativeHeaderInputHeight);

            _chanceInput = null;
            _nativeChanceInput = CreateNativeInput(
                row,
                "NativeChanceInput",
                spawn != null ? FormatSpawnChance(spawn.SpawnChance) : FormatSpawnChance(0f),
                TMP_InputField.ContentType.DecimalNumber,
                TMP_InputField.CharacterValidation.Decimal,
                textFontSize: 12f);
            SetTopLeft(_nativeChanceInput.GetComponent<RectTransform>(), 102f, NativeSpawnChanceInputTop, NativeSpawnChanceInputWidth, 21f);
            NormalizeNativeInputViewport(_nativeChanceInput, 8f, 4f);
            _nativeChanceInput.interactable = editable;
            _nativeChanceInput.customCaretColor = true;
            _nativeChanceInput.caretColor = new Color(0f, 0f, 0f, 0f);
            _nativeChanceInput.caretWidth = 1;
            _nativeChanceCaret = CreateNativeInputCaret(_nativeChanceInput, 13f);

            if (editable)
            {
                _nativeChanceInput.onValueChanged.AddListener(ClampNativeChanceInputText);
                _nativeChanceInput.onEndEdit.AddListener(text =>
                {
                    if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        SetNativeChanceInputText(spawn != null ? spawn.SpawnChance : 0f);
                        MarkDirtyAndRebuild();
                        return;
                    }

                    var active = _viz.ActiveSpawn;
                    if (active == null)
                    {
                        return;
                    }

                    var clamped = Mathf.Clamp01(value);
                    SetNativeChanceInputText(clamped);
                    if (Math.Abs(active.SpawnChance - clamped) <= 0.0001f)
                    {
                        return;
                    }

                    PushUndoSnapshot("spawn-chance", coalesce: false);
                    ApplyEditedSpawnChance(active, clamped);
                    MarkDirtyAndRebuild();
                });
            }

            var alwaysSpawnText = SupportsAlwaysSpawnUi(spawn, vanilla)
                ? (spawn != null && spawn.IsAlwaysSpawn ? "Always Spawn: Yes" : "Always Spawn: No")
                : "Always Spawn: Not available";
            var alwaysLabel = CreateNativeLabelAt(row, alwaysSpawnText, 13f, bold: false, 222f, 0f, 206f, NativeHeaderInputHeight);
        }

        private void BuildNativeSkeletonSearchRow(Transform header)
        {
            var row = CreateNativeFreeRow(header, "NativeItemSearchRow", NativeHeaderInputHeight);
            SetTopCenter(row, 112f, NativeExistingSearchWidth, NativeHeaderInputHeight);

            var searchInput = CreateNativeInput(
                row,
                "NativeItemSearchInput",
                _itemFilter,
                TMP_InputField.ContentType.Standard,
                TMP_InputField.CharacterValidation.None,
                "Search existing entries",
                showSearchIcon: true,
                textFontSize: 14f);
            var searchRect = searchInput != null ? searchInput.GetComponent<RectTransform>() : null;
            SetTopLeft(searchRect, 0f, 0f, NativeExistingSearchWidth, NativeHeaderInputHeight);

            if (searchInput != null)
            {
                searchInput.onEndEdit.AddListener(value =>
                {
                    _itemFilter = value ?? string.Empty;
                    MarkDirtyAndRebuild();
                });
            }
        }

        private RectTransform CreateNativeInputCaret(TMP_InputField input, float height = -1f)
        {
            var viewport = input != null ? input.textViewport : null;
            if (viewport == null)
            {
                return null;
            }

            var caret = CreatePanel(viewport, "ULE_NativeInputCaret", TextColor);
            var image = caret.GetComponent<Image>();
            if (image != null)
            {
                image.raycastTarget = false;
            }

            caret.anchorMin = new Vector2(0f, 0.5f);
            caret.anchorMax = new Vector2(0f, 0.5f);
            caret.pivot = new Vector2(0.5f, 0.5f);
            var caretHeight = height > 0f ? height : Mathf.Max(10f, viewport.rect.height - 2f);
            caret.sizeDelta = new Vector2(1f, caretHeight);
            caret.anchoredPosition = new Vector2(1f, 0f);
            caret.SetAsLastSibling();
            caret.gameObject.SetActive(false);
            return caret;
        }

        private void UpdateNativeChanceCaret()
        {
            var input = _nativeChanceInput;
            var caret = _nativeChanceCaret;
            if (input == null || caret == null)
            {
                return;
            }

            var shouldShow = input.interactable && input.isFocused && Mathf.Repeat(Time.unscaledTime * 1.8f, 1f) < 0.5f;
            if (!shouldShow)
            {
                if (caret.gameObject.activeSelf)
                {
                    caret.gameObject.SetActive(false);
                }

                return;
            }

            var text = input.textComponent;
            var viewport = input.textViewport;
            if (text == null || viewport == null)
            {
                caret.gameObject.SetActive(false);
                return;
            }

            var value = input.text ?? string.Empty;
            var caretPosition = Mathf.Clamp(input.caretPosition, 0, value.Length);
            var prefix = caretPosition > 0 ? value.Substring(0, caretPosition) : string.Empty;
            var textWidth = string.IsNullOrEmpty(prefix) ? 0f : text.GetPreferredValues(prefix).x;
            var x = Mathf.Clamp(textWidth + 1f, 1f, Mathf.Max(1f, viewport.rect.width - 1f));

            var caretHeight = caret.sizeDelta.y > 1f
                ? caret.sizeDelta.y
                : Mathf.Max(10f, viewport.rect.height - 2f);
            caret.sizeDelta = new Vector2(1f, caretHeight);
            caret.anchoredPosition = new Vector2(x, 0f);
            caret.SetAsLastSibling();
            if (!caret.gameObject.activeSelf)
            {
                caret.gameObject.SetActive(true);
            }
        }

        private void ClampNativeChanceInputText(string text)
        {
            if (_updatingNativeChanceInput || _nativeChanceInput == null || string.IsNullOrWhiteSpace(text) || text == ".")
            {
                return;
            }

            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return;
            }

            if (value <= 1f && value >= 0f)
            {
                return;
            }

            SetNativeChanceInputText(Mathf.Clamp01(value), compact: true);
        }

        private void SetNativeChanceInputText(float value, bool compact = false)
        {
            if (_nativeChanceInput == null)
            {
                return;
            }

            _updatingNativeChanceInput = true;
            _nativeChanceInput.SetTextWithoutNotify(FormatSpawnChance(value));
            _nativeChanceInput.caretPosition = _nativeChanceInput.text.Length;
            _updatingNativeChanceInput = false;
        }

        private static string FormatSpawnChance(float value)
        {
            return Mathf.Clamp01(value).ToString("0.#########", CultureInfo.InvariantCulture);
        }

        private static string FormatSpawnChanceDelta(float value)
        {
            var clamped = Mathf.Clamp(value, -1f, 1f);
            var prefix = clamped > 0f ? "+" : clamped < 0f ? "-" : string.Empty;
            return prefix + Math.Abs(clamped).ToString("0.#########", CultureInfo.InvariantCulture);
        }

        private void BuildNativeSkeletonItems(SpawnPointData spawn)
        {
            var addQuery = (_addQuery ?? string.Empty).Trim();
            var showingAddResults = _viewMode == EditorViewMode.Edited && !string.IsNullOrWhiteSpace(addQuery);
            var rows = showingAddResults
                ? BuildSuggestionRows()
                : BuildItemRows(spawn)
                    .Where(row => string.IsNullOrWhiteSpace(_itemFilter) ||
                                  row.SearchText.IndexOf(_itemFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderByDescending(row => row.Item.Weight)
                    .ThenBy(row => row.Item.Tpl, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            var title = showingAddResults ? "ADD ITEM RESULTS" : "ITEMS AT THIS SPAWN";
            var emptyMessage = showingAddResults
                ? "No add results match this search."
                : string.IsNullOrWhiteSpace(_itemFilter)
                    ? "No items at this spawn."
                    : "No items match this search.";

            CreateNativeListSection(title, rows, emptyMessage)
                .UseNativePanel(TryBuildHandbookSpawnItemsPanel)
                .UseFallbackScroll("NativeSpawnItemsScroll", 210f, 260f, (content, rowData) =>
                {
                    if (!TryCreateHandbookStyleSpawnItemRow(content, rowData))
                    {
                        CreateFallbackHandbookStyleSpawnItemRow(content, rowData);
                    }
                })
                .Build();
        }

        private List<CachedItemRow> BuildSuggestionRows()
        {
            if (_suggestions.Count == 0)
            {
                RefreshSuggestions();
            }

            return _suggestions
                .Take(50)
                .Select(candidate =>
                {
                    var item = candidate.TemplateItem?.Clone() ?? new LootItem { Tpl = candidate.Tpl };
                    if (item == null || string.IsNullOrWhiteSpace(item.Tpl))
                    {
                        return null;
                    }

                    var displayName = !string.IsNullOrWhiteSpace(candidate.DisplayName)
                        ? candidate.DisplayName
                        : FormatItemDisplayName(item);
                    return new CachedItemRow
                    {
                        Item = item,
                        DisplayName = displayName,
                        SearchText = BuildItemSearchText(item, displayName),
                        AddCandidate = candidate
                    };
                })
                .Where(row => row != null)
                .ToList();
        }

        private bool TryBuildHandbookSpawnItemsPanel(IReadOnlyList<CachedItemRow> rows)
        {
            var templateScroll = ResolveHandbookEntityScrollTemplate();
            var templateRow = ResolveHandbookEntityRowTemplate();
            if (templateScroll == null || templateRow == null || _content == null)
            {
                return false;
            }

            GameObject scrollObject = null;
            try
            {
                scrollObject = Instantiate(templateScroll, _content, false);
                scrollObject.name = "ULE_HandbookSpawnItemsScroll";
                scrollObject.SetActive(true);
                SetLayerRecursively(scrollObject, _content.gameObject.layer);

                var scrollRect = scrollObject.GetComponent<ScrollRect>() ?? scrollObject.GetComponentInChildren<ScrollRect>(true);
                var scrollTransform = scrollObject.transform as RectTransform;
                if (scrollRect == null || scrollTransform == null)
                {
                    Destroy(scrollObject);
                    return false;
                }

                scrollTransform.anchorMin = Vector2.zero;
                scrollTransform.anchorMax = Vector2.one;
                scrollTransform.pivot = new Vector2(0.5f, 0.5f);
                scrollTransform.offsetMin = new Vector2(NativeResultsLeftMargin, NativeFooterHeight - NativeResultsFooterGap);
                scrollTransform.offsetMax = new Vector2(-NativeResultsRightMargin, -NativeResultsTop);
                scrollTransform.localScale = Vector3.one;
                SetLayout(scrollTransform, minWidth: 1f, minHeight: 1f, flexibleWidth: 1f, flexibleHeight: 1f);
                SetLayoutIgnore(scrollTransform, true);

                var content = ResolveScrollContent(scrollRect);
                if (content == null)
                {
                    Destroy(scrollObject);
                    return false;
                }

                ClearChildrenDetached(content);

                ConfigureHandbookScrollClone(scrollRect, scrollTransform, content, rows.Count);
                var scrollbar = scrollRect.verticalScrollbar != null
                    ? scrollRect.verticalScrollbar.transform as RectTransform
                    : scrollTransform.Find("Scrollbar") as RectTransform;
                ConfigureHandbookScrollbarClone(scrollbar);
                scrollRect.content = content;
                scrollRect.horizontal = false;
                scrollRect.vertical = true;
                scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
                scrollRect.scrollSensitivity = Mathf.Max(scrollRect.scrollSensitivity, 35f);
                scrollRect.onValueChanged.RemoveAllListeners();

                if (rows.Count > NativeVirtualizedRowThreshold)
                {
                    PopulateHandbookSpawnRowsVirtualized(scrollRect, scrollTransform, content, templateRow, rows);
                }
                else
                {
                    PopulateHandbookSpawnRows(content, templateRow, rows, 0, rows.Count);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (scrollObject != null)
                {
                    Destroy(scrollObject);
                }

                _log?.LogDebug($"[ULE] Handbook spawn list fallback: {ex.Message}");
                return false;
            }
        }

        private void PopulateHandbookSpawnRows(
            RectTransform content,
            EntityListElement templateRow,
            IReadOnlyList<CachedItemRow> rows,
            int startIndex,
            int count)
        {
            ClearChildrenDetached(content);

            if (content == null || templateRow == null || rows == null || count <= 0)
            {
                return;
            }

            var end = Mathf.Min(rows.Count, startIndex + count);
            for (var i = Mathf.Max(0, startIndex); i < end; i++)
            {
                var row = rows[i];
                if (!TryCreateNativeHandbookSpawnItemRow(content, templateRow, row, i))
                {
                    CreateFallbackHandbookStyleSpawnItemRow(content, row);
                    var fallback = content.childCount > 0 ? content.GetChild(content.childCount - 1) as RectTransform : null;
                    PositionHandbookContentRow(fallback, i);
                }
            }
        }

        private void PopulateHandbookSpawnRowsVirtualized(
            ScrollRect scrollRect,
            RectTransform scrollTransform,
            RectTransform content,
            EntityListElement templateRow,
            IReadOnlyList<CachedItemRow> rows)
        {
            if (scrollRect == null || scrollTransform == null || content == null || templateRow == null || rows == null)
            {
                return;
            }

            var rowStride = NativeResultRowHeight + NativeResultRowSpacing;
            var visibleRows = CalculateNativeVirtualizedRowPoolSize(scrollRect, scrollTransform, rowStride, rows.Count);
            var pooledRows = new List<GameObject>(visibleRows);
            for (var i = 0; i < visibleRows; i++)
            {
                var row = Instantiate(templateRow.gameObject, content, false);
                row.name = "ULE_HandbookSpawnItemRow";
                row.SetActive(false);
                SetLayerRecursively(row, content.gameObject.layer);
                pooledRows.Add(row);
            }

            var currentStart = -1;

            void Refresh()
            {
                var viewportHeight = GetNativeViewportHeight(scrollRect, scrollTransform);
                var contentHeight = Mathf.Max(NativeResultRowHeight, content.rect.height);
                var scrollableHeight = Mathf.Max(0f, contentHeight - viewportHeight);
                var topOffset = Mathf.Clamp(content.anchoredPosition.y, 0f, scrollableHeight);
                var maxStart = Mathf.Max(0, rows.Count - visibleRows);
                var startIndex = Mathf.Clamp(
                    Mathf.FloorToInt(topOffset / rowStride) - NativeVirtualizedRowBuffer / 2,
                    0,
                    maxStart);

                if (startIndex == currentStart)
                {
                    return;
                }

                currentStart = startIndex;
                for (var poolIndex = 0; poolIndex < pooledRows.Count; poolIndex++)
                {
                    var rowIndex = startIndex + poolIndex;
                    var pooledRow = pooledRows[poolIndex];
                    if (rowIndex >= rows.Count)
                    {
                        pooledRow.SetActive(false);
                        continue;
                    }

                    if (!TryBindNativeHandbookSpawnItemRow(pooledRow, rows[rowIndex], rowIndex))
                    {
                        pooledRow.SetActive(false);
                    }
                }
            }

            scrollRect.verticalNormalizedPosition = 1f;
            Refresh();
            scrollRect.onValueChanged.AddListener(_ => Refresh());
        }

        private int CalculateNativeVirtualizedRowPoolSize(
            ScrollRect scrollRect,
            RectTransform scrollTransform,
            float rowStride,
            int rowCount)
        {
            if (rowCount <= 0)
            {
                return 0;
            }

            var viewportHeight = GetNativeViewportHeight(scrollRect, scrollTransform);
            if (_window != null)
            {
                var areaSize = GetClampAreaSize(_window);
                if (areaSize.y > 1f)
                {
                    var largestLikelyViewport = Mathf.Max(
                        NativeResultsHeight,
                        areaSize.y - NativeHeaderHeight - NativeFooterHeight - (NativeResultRowSpacing * 4f));
                    viewportHeight = Mathf.Max(viewportHeight, largestLikelyViewport);
                }
            }

            var rowsNeeded = Mathf.CeilToInt(viewportHeight / Mathf.Max(1f, rowStride)) + NativeVirtualizedRowBuffer;
            return Mathf.Min(
                rowCount,
                NativeVirtualizedMaxResizePoolRows,
                Mathf.Max(NativeVirtualizedMinRows, rowsNeeded));
        }

        private static float GetNativeViewportHeight(ScrollRect scrollRect, RectTransform scrollTransform)
        {
            if (scrollRect != null && scrollRect.viewport != null && scrollRect.viewport.rect.height > 1f)
            {
                return scrollRect.viewport.rect.height;
            }

            return scrollTransform != null && scrollTransform.rect.height > 1f
                ? scrollTransform.rect.height
                : NativeResultsHeight;
        }

        private static void ClearChildrenDetached(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }

        private static RectTransform ResolveScrollContent(ScrollRect scrollRect)
        {
            if (scrollRect == null)
            {
                return null;
            }

            if (scrollRect.content != null)
            {
                return scrollRect.content;
            }

            var root = scrollRect.transform as RectTransform;
            return root?.Find("ContentArea/Content") as RectTransform;
        }

        private static void ConfigureHandbookScrollClone(ScrollRect scrollRect, RectTransform scrollTransform, RectTransform content, int rowCount)
        {
            if (scrollRect == null || scrollTransform == null || content == null)
            {
                return;
            }

            StretchScrollAncestors(scrollTransform, content);

            var viewport = scrollRect.viewport ?? content.parent as RectTransform;
            if (viewport != null)
            {
                viewport.anchorMin = Vector2.zero;
                viewport.anchorMax = Vector2.one;
                viewport.pivot = new Vector2(0.5f, 0.5f);
                viewport.anchoredPosition = Vector2.zero;
                viewport.offsetMin = Vector2.zero;
                viewport.offsetMax = new Vector2(-NativeResultsViewportRightGutter, 0f);
                viewport.localScale = Vector3.one;
                scrollRect.viewport = viewport;
            }

            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            var contentHeight = rowCount <= 0
                ? NativeResultRowHeight
                : rowCount * NativeResultRowHeight + Mathf.Max(0, rowCount - 1) * NativeResultRowSpacing;
            content.sizeDelta = new Vector2(0f, Mathf.Max(NativeResultRowHeight, contentHeight));
            content.localScale = Vector3.one;

            foreach (var layout in content.GetComponents<VerticalLayoutGroup>())
            {
                Destroy(layout);
            }

            foreach (var fitter in content.GetComponents<ContentSizeFitter>())
            {
                Destroy(fitter);
            }
        }

        private static void StretchScrollAncestors(RectTransform scrollTransform, RectTransform content)
        {
            if (scrollTransform == null || content == null)
            {
                return;
            }

            var current = content.parent as RectTransform;
            while (current != null && current != scrollTransform)
            {
                current.anchorMin = Vector2.zero;
                current.anchorMax = Vector2.one;
                current.pivot = new Vector2(0.5f, 0.5f);
                current.anchoredPosition = Vector2.zero;
                current.offsetMin = Vector2.zero;
                current.offsetMax = Vector2.zero;
                current.localScale = Vector3.one;
                SetLayoutIgnore(current, true);
                current = current.parent as RectTransform;
            }
        }

        private static void ConfigureHandbookScrollbarClone(RectTransform scrollbar)
        {
            if (scrollbar == null)
            {
                return;
            }

            scrollbar.anchorMin = new Vector2(1f, 0f);
            scrollbar.anchorMax = new Vector2(1f, 1f);
            scrollbar.pivot = new Vector2(1f, 0.5f);
            scrollbar.offsetMin = new Vector2(
                -NativeResultsScrollbarRightGap - NativeResultsScrollbarWidth,
                -NativeResultsScrollbarBottomExtension);
            scrollbar.offsetMax = new Vector2(-NativeResultsScrollbarRightGap, 1f + NativeVisualPixel);
            scrollbar.localScale = Vector3.one;
            StretchScrollbarChildrenHorizontally(scrollbar);
            SetLayout(
                scrollbar,
                minWidth: NativeResultsScrollbarWidth,
                minHeight: 1f,
                preferredWidth: NativeResultsScrollbarWidth,
                preferredHeight: 1f,
                flexibleHeight: 1f);
            SetLayoutIgnore(scrollbar, true);
        }

        private static void StretchScrollbarChildrenHorizontally(RectTransform scrollbar)
        {
            if (scrollbar == null)
            {
                return;
            }

            foreach (var child in scrollbar.GetComponentsInChildren<RectTransform>(true))
            {
                if (child == null || child == scrollbar)
                {
                    continue;
                }

                child.anchorMin = new Vector2(0f, child.anchorMin.y);
                child.anchorMax = new Vector2(1f, child.anchorMax.y);
                child.offsetMin = new Vector2(0f, child.offsetMin.y);
                child.offsetMax = new Vector2(0f, child.offsetMax.y);
            }
        }

        private void CreateNativeMiddleSectionSeams()
        {
            if (_content == null)
            {
                return;
            }

            var top = CreateNativeHorizontalBorder(_content, "ULE_MiddleTopBorder");
            SetStretchTop(top, 0f, NativeHeaderHeight - (2f * NativeVisualPixel), 0f, 2f);

            var bottom = CreateNativeHorizontalBorder(_content, "ULE_MiddleBottomBorder");
            bottom.anchorMin = new Vector2(0f, 0f);
            bottom.anchorMax = new Vector2(1f, 0f);
            bottom.pivot = new Vector2(0.5f, 0.5f);
            var bottomSeam = NativeFooterHeight - NativeResultsFooterGap - NativeMiddleBottomBorderDrop;
            bottom.offsetMin = new Vector2(0f, bottomSeam - 1f);
            bottom.offsetMax = new Vector2(0f, bottomSeam + 1f);
        }

        private RectTransform CreateNativeHorizontalBorder(Transform parent, string name)
        {
            var borderHost = CreateRect(name, parent);
            SetLayoutIgnore(borderHost, true);
            borderHost.SetAsLastSibling();

            CreateNativeBorderStrip(borderHost, "TintLine", new Color32(88, 93, 96, 255), 0f, NativeVisualPixel);
            CreateNativeBorderStrip(borderHost, "ShadowBelow", new Color(0f, 0f, 0f, 0.70f), NativeVisualPixel, NativeVisualPixel);
            CreateNativeBorderStrip(borderHost, "DeepShadowBelow", new Color(0f, 0f, 0f, 0.42f), NativeVisualPixel * 2f, NativeVisualPixel);

            return borderHost;
        }

        private RectTransform CreateNativeBorderStrip(Transform parent, string name, Color color, float top, float height)
        {
            var strip = CreatePanel(parent, name, color);
            SetStretchTop(strip, 0f, top, 0f, height);
            SetLayoutIgnore(strip, true);
            var image = strip.GetComponent<Image>();
            if (image != null)
            {
                image.raycastTarget = false;
            }

            return strip;
        }

        private bool TryCreateNativeHandbookSpawnItemRow(RectTransform parent, EntityListElement template, CachedItemRow rowData, int rowIndex)
        {
            if (parent == null || template == null || rowData?.Item == null)
            {
                return false;
            }

            GameObject row = null;
            try
            {
                row = Instantiate(template.gameObject, parent, false);
                SetLayerRecursively(row, parent.gameObject.layer);
                if (TryBindNativeHandbookSpawnItemRow(row, rowData, rowIndex))
                {
                    return true;
                }

                Destroy(row);
                return false;
            }
            catch (Exception ex)
            {
                if (row != null)
                {
                    Destroy(row);
                }

                _log?.LogDebug($"[ULE] Native Handbook spawn row fallback: {ex.Message}");
                return false;
            }
        }

        private bool TryBindNativeHandbookSpawnItemRow(GameObject row, CachedItemRow rowData, int rowIndex)
        {
            if (row == null || rowData?.Item == null)
            {
                return false;
            }

            try
            {
                row.name = "ULE_HandbookSpawnItemRow";
                row.SetActive(true);

                var rowRect = row.transform as RectTransform;
                PositionHandbookContentRow(rowRect, rowIndex);
                RemoveNativeSpawnRowDecorations(rowRect);

                var element = row.GetComponent<LayoutElement>() ?? row.AddComponent<LayoutElement>();
                element.ignoreLayout = false;
                element.minHeight = NativeResultRowHeight;
                element.preferredHeight = NativeResultRowHeight;
                element.flexibleWidth = 1f;
                element.flexibleHeight = 0f;

                var view = row.GetComponent<EntityListElement>();
                if (view != null && TryResolveHandbookNode(rowData.Item.Tpl, out var node))
                {
                    var wishlist = TryGetWishlistManager();
                    if (wishlist != null)
                    {
                        view.enabled = true;
                        view.Show(node, _ => { }, wishlist);
                        HideHandbookRowNoise(view);
                        ApplyHandbookRowDisplayName(row, view, rowData);
                        DisableHandbookRowDefaultInteraction(row, view);
                        HideHandbookRowChevron(row);
                        ReserveHandbookRowControlSpace(row, GetNativeRowReservedWidth(rowData));
                        ReplaceHandbookRowTextWithClampedOverlay(rowRect, rowData, GetNativeRowReservedWidth(rowData));
                        AddNativeActionButtonToSpawnItemRow(rowRect, rowData);
                        return true;
                    }
                }

                return TryPopulateHandbookRowFallback(row, rowData);
            }
            catch (Exception ex)
            {
                row.SetActive(false);
                _log?.LogDebug($"[ULE] Native Handbook spawn row fallback: {ex.Message}");
                return false;
            }
        }

        private static void RemoveNativeSpawnRowDecorations(RectTransform row)
        {
            if (row == null)
            {
                return;
            }

            string[] names =
            {
                "ULE_RowTextOverlay",
                "ULE_ItemRowControls",
                "ULE_CompareStatusLabel",
                "ULE_RemoveItemButton"
            };

            foreach (var name in names)
            {
                var child = row.Find(name);
                if (child != null)
                {
                    Destroy(child.gameObject);
                }
            }
        }

        private static void PositionHandbookContentRow(RectTransform rowRect, int rowIndex)
        {
            if (rowRect == null)
            {
                return;
            }

            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.anchoredPosition = new Vector2(0f, -rowIndex * (NativeResultRowHeight + NativeResultRowSpacing));
            rowRect.sizeDelta = new Vector2(0f, NativeResultRowHeight);
            rowRect.localScale = Vector3.one;
        }

        private static void ApplyHandbookRowChrome(RectTransform row)
        {
            if (row == null)
            {
                return;
            }

            var outline = row.GetComponent<Outline>() ?? row.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.39f, 0.45f, 0.47f, 0.42f);
            outline.effectDistance = new Vector2(1f, -1f);
        }

        private void HandleSpawnItemRowActivated(CachedItemRow rowData)
        {
            if (rowData?.Item == null)
            {
                return;
            }

            if (rowData.AddCandidate != null)
            {
                AddSuggestionToSpawn(_viz.ActiveSpawn, rowData.AddCandidate);
                return;
            }

            if (!PresetPreviewBridge.CanOpen(rowData.Item))
            {
                return;
            }

            OpenPresetScreen(rowData.Item, readOnly: _viewMode != EditorViewMode.Edited);
        }

        private static GClass2067 TryGetWishlistManager()
        {
            try
            {
                return ItemUiContext.Instance?.WishlistManager;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryResolveHandbookNode(string templateId, out EntityNodeClass node)
        {
            node = null;
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return false;
            }

            try
            {
                if (!Singleton<HandbookClass>.Instantiated)
                {
                    return false;
                }

                var handbook = Singleton<HandbookClass>.Instance;
                node = handbook?.EncyclopediaNodes?[templateId];
                if (node == null && handbook != null)
                {
                    node = handbook[templateId];
                }
            }
            catch
            {
                node = null;
            }

            return node != null;
        }

        private static void ApplyHandbookRowDisplayName(GameObject row, EntityListElement view, CachedItemRow rowData)
        {
            if (row == null || rowData == null)
            {
                return;
            }

            EnsureHandbookRowFields();
            var nameLabel = view != null && _handbookEntityNameField != null
                ? _handbookEntityNameField.GetValue(view) as TextMeshProUGUI
                : row.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault();
            if (nameLabel == null)
            {
                return;
            }

            StripNativeTextBindings(row);
            nameLabel.text = rowData.DisplayName ?? string.Empty;
            nameLabel.enableWordWrapping = false;
            nameLabel.overflowMode = TextOverflowModes.Ellipsis;
            ConfigureHandbookRowTextLayout(nameLabel);
            nameLabel.gameObject.SetActive(true);
        }

        private static void ConfigureHandbookRowTextLayout(TextMeshProUGUI label)
        {
            if (label == null)
            {
                return;
            }

            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            var layout = label.GetComponent<LayoutElement>() ?? label.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = 0f;
            layout.preferredWidth = 0f;
            layout.flexibleWidth = 1f;
        }

        private static void HideHandbookRowNoise(EntityListElement view)
        {
            if (view == null)
            {
                return;
            }

            EnsureHandbookRowFields();
            var newNodeObject = _handbookEntityNewNodeObjectField?.GetValue(view) as GameObject;
            if (newNodeObject != null)
            {
                newNodeObject.SetActive(false);
            }
        }

        private static void DisableHandbookRowDefaultInteraction(GameObject row, EntityListElement view)
        {
            if (view != null)
            {
                view.Boolean_0 = true;
                view.enabled = false;
            }

            if (row == null)
            {
                return;
            }

            foreach (var hover in row.GetComponentsInChildren<HoverTrigger>(true))
            {
                Destroy(hover);
            }

            foreach (var trigger in row.GetComponentsInChildren<EventTrigger>(true))
            {
                Destroy(trigger);
            }

            foreach (var button in row.GetComponentsInChildren<Button>(true))
            {
                button.onClick.RemoveAllListeners();
                button.interactable = false;
            }

            var rootGraphic = row.GetComponent<Graphic>();
            if (rootGraphic != null)
            {
                rootGraphic.raycastTarget = false;
            }
        }

        private static void HideHandbookRowChevron(GameObject row)
        {
            if (row == null)
            {
                return;
            }

            foreach (var text in row.GetComponentsInChildren<TMP_Text>(true))
            {
                var value = (text.text ?? string.Empty).Trim();
                if (value == ">" || value == "›" || value == "»")
                {
                    text.gameObject.SetActive(false);
                }
            }

            foreach (var graphic in row.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic is TMP_Text)
                {
                    continue;
                }

                var lower = graphic.name?.ToLowerInvariant() ?? string.Empty;
                if (lower.Contains("arrow") || lower.Contains("chevron") || lower.Contains("caret") || lower.Contains("next"))
                {
                    graphic.gameObject.SetActive(false);
                }
            }
        }

        private static void HideHandbookRowRightEdgeGlyphs(GameObject row)
        {
            if (row == null)
            {
                return;
            }

            var rowRect = row.transform as RectTransform;
            foreach (var text in row.GetComponentsInChildren<TMP_Text>(true))
            {
                var rect = text.rectTransform;
                var value = (text.text ?? string.Empty).Trim();
                if (IsChevronGlyphText(value) ||
                    (IsRightEdgeRowElement(rowRect, rect) && value.Length <= 2))
                {
                    text.gameObject.SetActive(false);
                }
            }

            foreach (var text in row.GetComponentsInChildren<Text>(true))
            {
                var rect = text.rectTransform;
                var value = (text.text ?? string.Empty).Trim();
                if (IsChevronGlyphText(value) ||
                    (IsRightEdgeRowElement(rowRect, rect) && value.Length <= 2))
                {
                    text.gameObject.SetActive(false);
                }
            }

            foreach (var graphic in row.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic is TMP_Text || graphic is Text)
                {
                    continue;
                }

                var rect = graphic.transform as RectTransform;
                var lower = graphic.name?.ToLowerInvariant() ?? string.Empty;
                if (IsRightEdgeRowElement(rowRect, rect) &&
                    (lower.Contains("arrow") ||
                     lower.Contains("chevron") ||
                     lower.Contains("caret") ||
                     lower.Contains("next") ||
                     Mathf.Abs(rect.sizeDelta.x) <= 64f))
                {
                    graphic.gameObject.SetActive(false);
                }
            }
        }

        private static bool IsChevronGlyphText(string value)
        {
            return value == ">" || value == "\u203a" || value == "\u00bb";
        }

        private static bool IsRightEdgeRowElement(RectTransform row, RectTransform rect)
        {
            if (row == null || rect == null || rect == row)
            {
                return false;
            }

            for (var current = rect; current != null && current != row; current = current.parent as RectTransform)
            {
                var name = current.name ?? string.Empty;
                if (name.StartsWith("ULE_", StringComparison.Ordinal))
                {
                    return false;
                }

                if (current.anchorMin.x >= 0.78f ||
                    (current.anchorMin.x >= 0.5f && current.anchorMax.x >= 0.95f) ||
                    (current.pivot.x >= 0.9f && current.anchoredPosition.x > 0f))
                {
                    return true;
                }
            }

            return false;
        }

        private static float GetNativeRowReservedWidth(CachedItemRow rowData)
        {
            return string.IsNullOrWhiteSpace(rowData?.CompareStatusText)
                ? NativeRowControlReservedWidth
                : NativeRowControlReservedWidth + NativeCompareStatusSpacing + NativeCompareStatusWidth;
        }

        private static void ReserveHandbookRowControlSpace(GameObject row, float reservedWidth)
        {
            if (row == null)
            {
                return;
            }

            var rowRect = row.transform as RectTransform;
            foreach (var text in row.GetComponentsInChildren<TMP_Text>(true))
            {
                var rect = text.rectTransform;
                if (rect == null || text.name.StartsWith("ULE_", StringComparison.Ordinal))
                {
                    continue;
                }

                if (text is TextMeshProUGUI tmp)
                {
                    ConfigureHandbookRowTextLayout(tmp);
                }

                ClampHandbookRowTextRect(rowRect, rect, reservedWidth);
                ClampHandbookRowTextAncestors(rowRect, rect, reservedWidth);
                if (rect.anchorMax.x > 0.5f)
                {
                    rect.offsetMax = new Vector2(Mathf.Min(rect.offsetMax.x, -reservedWidth), rect.offsetMax.y);
                }
            }
        }

        private static void ClampHandbookRowTextAncestors(RectTransform row, RectTransform textRect, float reservedWidth)
        {
            if (row == null || textRect == null)
            {
                return;
            }

            for (var current = textRect.parent as RectTransform; current != null && current != row; current = current.parent as RectTransform)
            {
                if (current.name.StartsWith("ULE_", StringComparison.Ordinal) || IsRightEdgeRowElement(row, current))
                {
                    break;
                }

                Bounds bounds;
                try
                {
                    bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(row, current);
                }
                catch
                {
                    continue;
                }

                if (bounds.min.x < 80f || bounds.size.x < 80f)
                {
                    continue;
                }

                ClampHandbookRowTextRect(row, current, reservedWidth);
            }
        }

        private static void ClampHandbookRowTextRect(RectTransform row, RectTransform textRect, float reservedWidth)
        {
            if (row == null || textRect == null)
            {
                return;
            }

            foreach (var fitter in textRect.GetComponents<ContentSizeFitter>())
            {
                Destroy(fitter);
            }

            var rowWidth = GetBestKnownRowWidth(row);
            var left = 0f;
            try
            {
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(row, textRect);
                left = Mathf.Max(0f, bounds.min.x);
            }
            catch
            {
                left = Mathf.Max(0f, textRect.anchoredPosition.x);
            }

            var maxWidth = Mathf.Max(48f, rowWidth - left - reservedWidth - 8f);
            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, maxWidth);

            var layout = textRect.GetComponent<LayoutElement>() ?? textRect.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = 0f;
            layout.preferredWidth = maxWidth;
            layout.flexibleWidth = 1f;
        }

        private static float GetBestKnownRowWidth(RectTransform row)
        {
            for (var current = row; current != null; current = current.parent as RectTransform)
            {
                if (current.rect.width > 1f)
                {
                    return current.rect.width;
                }
            }

            return NativeResultsWidth;
        }

        private bool TryPopulateHandbookRowFallback(GameObject row, CachedItemRow rowData)
        {
            if (row == null || rowData?.Item == null)
            {
                return false;
            }

            var view = row.GetComponent<EntityListElement>();
            EnsureHandbookRowFields();
            if (view != null)
            {
                view.enabled = false;
            }

            var nameLabel = view != null && _handbookEntityNameField != null
                ? _handbookEntityNameField.GetValue(view) as TextMeshProUGUI
                : row.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault();
            var categoryLabel = view != null && _handbookEntityCategoryField != null
                ? _handbookEntityCategoryField.GetValue(view) as TextMeshProUGUI
                : null;
            var background = view != null && _handbookEntityBackgroundField != null
                ? _handbookEntityBackgroundField.GetValue(view) as Image
                : row.GetComponent<Image>() ?? row.GetComponentsInChildren<Image>(true).FirstOrDefault();
            var iconObject = view != null && _handbookEntityIconField != null
                ? (_handbookEntityIconField.GetValue(view) as Component)?.gameObject
                : null;

            if (nameLabel == null)
            {
                return false;
            }

            StripNativeTextBindings(row);
            nameLabel.text = rowData.DisplayName ?? string.Empty;
            nameLabel.enableWordWrapping = false;
            nameLabel.overflowMode = TextOverflowModes.Ellipsis;
            ConfigureHandbookRowTextLayout(nameLabel);
            nameLabel.gameObject.SetActive(true);

            if (categoryLabel != null)
            {
                categoryLabel.text = BuildSpawnItemInfoText(rowData.Item);
                categoryLabel.enableWordWrapping = false;
                categoryLabel.overflowMode = TextOverflowModes.Ellipsis;
                ConfigureHandbookRowTextLayout(categoryLabel);
                categoryLabel.gameObject.SetActive(true);
            }

            if (background != null)
            {
                background.color = new Color(0.275f, 0.275f, 0.275f, 0.314f);
                background.raycastTarget = false;
            }

            if (_handbookEntityWishlistPanelField?.GetValue(view) is GameObject wishlistPanel)
            {
                wishlistPanel.SetActive(false);
            }

            if (_handbookEntityNewNodeObjectField?.GetValue(view) is GameObject newNodeObject)
            {
                newNodeObject.SetActive(false);
            }

            if (iconObject != null)
            {
                KeepHandbookIconFrameOnly(iconObject);
            }

            foreach (var button in row.GetComponentsInChildren<Button>(true))
            {
                button.onClick.RemoveAllListeners();
                button.interactable = false;
            }

            DisableHandbookRowDefaultInteraction(row, view);
            HideHandbookRowChevron(row);
            ReserveHandbookRowControlSpace(row, GetNativeRowReservedWidth(rowData));
            ReplaceHandbookRowTextWithClampedOverlay(row.transform as RectTransform, rowData, GetNativeRowReservedWidth(rowData));
            AddNativeActionButtonToSpawnItemRow(row.transform as RectTransform, rowData);
            return true;
        }

        private bool TryCreateHandbookStyleSpawnItemRow(RectTransform parent, CachedItemRow rowData)
        {
            var template = ResolveHandbookEntityRowTemplate();
            if (template == null || parent == null || rowData?.Item == null)
            {
                return false;
            }

            GameObject row = null;
            try
            {
                row = Instantiate(template.gameObject, parent, false);
                row.name = "ULE_HandbookSpawnItemRow";
                row.SetActive(true);

                foreach (var hover in row.GetComponentsInChildren<HoverTrigger>(true))
                {
                    Destroy(hover);
                }

                var view = row.GetComponent<EntityListElement>();
                EnsureHandbookRowFields();

                var nameLabel = view != null && _handbookEntityNameField != null
                    ? _handbookEntityNameField.GetValue(view) as TextMeshProUGUI
                    : row.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault();
                var categoryLabel = view != null && _handbookEntityCategoryField != null
                    ? _handbookEntityCategoryField.GetValue(view) as TextMeshProUGUI
                    : null;
                var background = view != null && _handbookEntityBackgroundField != null
                    ? _handbookEntityBackgroundField.GetValue(view) as Image
                    : row.GetComponent<Image>() ?? row.GetComponentsInChildren<Image>(true).FirstOrDefault();
                var iconObject = view != null && _handbookEntityIconField != null
                    ? (_handbookEntityIconField.GetValue(view) as Component)?.gameObject
                    : null;
                var wishlistPanel = view != null && _handbookEntityWishlistPanelField != null
                    ? _handbookEntityWishlistPanelField.GetValue(view) as GameObject
                    : null;
                var newNodeObject = view != null && _handbookEntityNewNodeObjectField != null
                    ? _handbookEntityNewNodeObjectField.GetValue(view) as GameObject
                    : null;

                if (view != null)
                {
                    view.enabled = false;
                }

                if (nameLabel == null)
                {
                    Destroy(row);
                    return false;
                }

                StripNativeTextBindings(row);
                nameLabel.text = rowData.DisplayName ?? string.Empty;
                nameLabel.enableWordWrapping = false;
                nameLabel.overflowMode = TextOverflowModes.Ellipsis;
                ConfigureHandbookRowTextLayout(nameLabel);
                nameLabel.gameObject.SetActive(true);

                if (categoryLabel != null)
                {
                    categoryLabel.text = BuildSpawnItemInfoText(rowData.Item);
                    categoryLabel.enableWordWrapping = false;
                    categoryLabel.overflowMode = TextOverflowModes.Ellipsis;
                    ConfigureHandbookRowTextLayout(categoryLabel);
                    categoryLabel.gameObject.SetActive(true);
                }

                if (background != null)
                {
                    background.color = new Color(0.275f, 0.275f, 0.275f, 0.314f);
                    background.raycastTarget = false;
                }

                if (wishlistPanel != null)
                {
                    wishlistPanel.SetActive(false);
                }

                if (newNodeObject != null)
                {
                    newNodeObject.SetActive(false);
                }

                if (iconObject != null)
                {
                    KeepHandbookIconFrameOnly(iconObject);
                }

                foreach (var button in row.GetComponentsInChildren<Button>(true))
                {
                    button.onClick.RemoveAllListeners();
                    button.interactable = false;
                }

                DisableHandbookRowDefaultInteraction(row, view);
                HideHandbookRowChevron(row);
                ReserveHandbookRowControlSpace(row, GetNativeRowReservedWidth(rowData));
                ReplaceHandbookRowTextWithClampedOverlay(row.transform as RectTransform, rowData, GetNativeRowReservedWidth(rowData));
                AddNativeActionButtonToSpawnItemRow(row.transform as RectTransform, rowData);

                var rowRect = row.transform as RectTransform;
                if (rowRect != null)
                {
                    rowRect.anchorMin = new Vector2(0f, 1f);
                    rowRect.anchorMax = new Vector2(1f, 1f);
                    rowRect.pivot = new Vector2(0.5f, 1f);
                    rowRect.localScale = Vector3.one;
                    ApplyHandbookRowChrome(rowRect);
                }

                var element = row.GetComponent<LayoutElement>() ?? row.AddComponent<LayoutElement>();
                element.ignoreLayout = false;
                element.minHeight = Mathf.Max(element.minHeight, 52f);
                element.preferredHeight = Mathf.Max(element.preferredHeight, 52f);
                element.flexibleWidth = 1f;
                return true;
            }
            catch (Exception ex)
            {
                if (row != null)
                {
                    Destroy(row);
                }

                _log?.LogDebug($"[ULE] Handbook-style spawn row fallback: {ex.Message}");
                return false;
            }
        }

        private void ReplaceHandbookRowTextWithClampedOverlay(RectTransform row, CachedItemRow rowData, float reservedWidth)
        {
            if (row == null || rowData?.Item == null)
            {
                return;
            }

            var existing = row.Find("ULE_RowTextOverlay");
            if (existing != null)
            {
                Destroy(existing.gameObject);
            }

            HideNativeHandbookRowText(row);

            var holder = CreateRect("ULE_RowTextOverlay", row);
            holder.anchorMin = Vector2.zero;
            holder.anchorMax = Vector2.one;
            holder.pivot = new Vector2(0.5f, 0.5f);
            holder.offsetMin = new Vector2(NativeRowTextLeftInset, 0f);
            holder.offsetMax = new Vector2(-(reservedWidth + NativeRowTextRightPadding), 0f);
            holder.localScale = Vector3.one;
            SetLayoutIgnore(holder, true);

            var name = CreateNativeLabel(holder, rowData.DisplayName ?? string.Empty, 13f, bold: true);
            if (name != null)
            {
                name.name = "ULE_RowDisplayName";
                name.color = TextColor;
                NormalizeNativeTextVisual(name);
                name.alignment = TextAlignmentOptions.MidlineLeft;
                name.raycastTarget = false;
                ConfigureHandbookRowTextLayout(name);
                SetStretchTop(name.rectTransform, 0f, 10f, 0f, 28f);
                SetLayoutIgnore(name.rectTransform, true);
            }

            var category = CreateNativeLabel(holder, BuildSpawnItemInfoText(rowData.Item), 11f, bold: false);
            if (category != null)
            {
                category.name = "ULE_RowCategory";
                category.color = SubtleTextColor;
                NormalizeNativeTextVisual(category);
                category.alignment = TextAlignmentOptions.MidlineLeft;
                category.raycastTarget = false;
                ConfigureHandbookRowTextLayout(category);
                SetStretchTop(category.rectTransform, 0f, 38f, 0f, 22f);
                SetLayoutIgnore(category.rectTransform, true);
            }
        }

        private static void HideNativeHandbookRowText(RectTransform row)
        {
            if (row == null)
            {
                return;
            }

            foreach (var text in row.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null ||
                    text.name.StartsWith("ULE_", StringComparison.Ordinal) ||
                    IsUnderUleRowOverlayOrControl(row, text.transform))
                {
                    continue;
                }

                text.text = string.Empty;
                text.gameObject.SetActive(false);
            }
        }

        private static bool IsUnderUleRowOverlayOrControl(RectTransform row, Transform child)
        {
            if (row == null || child == null)
            {
                return false;
            }

            for (var current = child.parent; current != null && current != row; current = current.parent)
            {
                if (current.name.StartsWith("ULE_", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void CreateFallbackHandbookStyleSpawnItemRow(RectTransform parent, CachedItemRow rowData)
        {
            var item = rowData?.Item;
            if (parent == null || item == null)
            {
                return;
            }

            var row = CreateRect("ULE_HandbookSpawnItemRowFallback", parent);
            SetLayout(row, minHeight: 52f, preferredHeight: 52f, flexibleWidth: 1f, flexibleHeight: 0f);

            var background = row.gameObject.AddComponent<Image>();
            background.color = new Color(0.275f, 0.275f, 0.275f, 0.314f);
            background.raycastTarget = false;
            ApplyHandbookRowChrome(row);

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(7, Mathf.RoundToInt(GetNativeRowReservedWidth(rowData)), 4, 4);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;

            var iconFrame = CreatePanel(row, "IconFrame", new Color(0f, 0f, 0f, 0.18f));
            SetLayout(iconFrame, minWidth: 44f, minHeight: 44f, preferredWidth: 44f, preferredHeight: 44f, flexibleWidth: 0f, flexibleHeight: 0f);
            var iconOutline = iconFrame.gameObject.AddComponent<Outline>();
            iconOutline.effectColor = new Color(0.42f, 0.48f, 0.50f, 0.75f);
            iconOutline.effectDistance = new Vector2(1f, -1f);

            var textColumn = CreateRect("TextColumn", row);
            SetLayout(textColumn, minHeight: 44f, flexibleWidth: 1f);
            var textLayout = textColumn.gameObject.AddComponent<VerticalLayoutGroup>();
            textLayout.spacing = 0f;
            textLayout.childControlWidth = true;
            textLayout.childForceExpandWidth = true;
            textLayout.childControlHeight = false;
            textLayout.childForceExpandHeight = false;

            var name = CreateNativeLabel(textColumn, rowData.DisplayName ?? string.Empty, 13f, bold: true, flexibleWidth: 1f);
            ConfigureHandbookRowTextLayout(name);
            var info = CreateNativeLabel(textColumn, BuildSpawnItemInfoText(item), 11f, bold: false, flexibleWidth: 1f);
            if (info != null)
            {
                ConfigureHandbookRowTextLayout(info);
                info.color = SubtleTextColor;
            }

            AddNativeActionButtonToSpawnItemRow(row, rowData);
        }

        private void AddNativeActionButtonToSpawnItemRow(RectTransform row, CachedItemRow rowData)
        {
            if (row == null || rowData?.Item == null)
            {
                return;
            }

            var isAddRow = rowData.AddCandidate != null;
            HideHandbookRowRightEdgeGlyphs(row.gameObject);

            var existing = row.Find("ULE_ItemRowControls");
            if (existing != null)
            {
                Destroy(existing.gameObject);
            }

            AddNativeCompareStatusLabel(row, rowData);
            AddNativeRemoveItemButton(row, rowData);

            var holder = CreateRect("ULE_ItemRowControls", row);
            holder.anchorMin = new Vector2(1f, 0.5f);
            holder.anchorMax = new Vector2(1f, 0.5f);
            holder.pivot = new Vector2(1f, 0.5f);
            holder.anchoredPosition = new Vector2(-NativeRowControlInset, NativeRowControlYOffset);
            holder.sizeDelta = new Vector2(NativeRowControlWidth, NativeResultRowHeight - 10f);
            var layout = holder.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;

            var stack = holder.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.spacing = 2f;
            stack.padding = new RectOffset(0, 0, 2, 2);
            stack.childAlignment = TextAnchor.MiddleLeft;
            stack.childControlWidth = true;
            stack.childForceExpandWidth = false;
            stack.childControlHeight = true;
            stack.childForceExpandHeight = false;

            var canEditWeight = !isAddRow && _viewMode == EditorViewMode.Edited;
            var weightRow = CreateRect("ULE_ItemWeightRow", holder);
            SetLayout(weightRow, minWidth: NativeRowControlWidth, preferredWidth: NativeRowControlWidth, minHeight: NativeRowWeightHeight, preferredHeight: NativeRowWeightHeight, flexibleWidth: 0f, flexibleHeight: 0f);
            var weightSlot = CreateRect("ULE_ItemWeightSlot", weightRow);
            SetTopLeft(weightSlot, NativeRowWeightLeftOffset, 0f, NativeRowWeightWidth, NativeRowWeightHeight);
            SetLayout(weightSlot, minWidth: NativeRowWeightWidth, preferredWidth: NativeRowWeightWidth, minHeight: NativeRowWeightHeight, preferredHeight: NativeRowWeightHeight, flexibleWidth: 0f, flexibleHeight: 0f);
            SetLayoutIgnore(weightSlot, true);
            weightSlot.gameObject.AddComponent<RectMask2D>();
            var weightInput = CreateNativeInput(
                weightSlot,
                "ULE_ItemWeight",
                rowData.Item.Weight.ToString("0.###", CultureInfo.InvariantCulture),
                TMP_InputField.ContentType.DecimalNumber,
                TMP_InputField.CharacterValidation.Decimal,
                string.Empty,
                showSearchIcon: false,
                textFontSize: 11f);
            var weightRect = weightInput != null ? weightInput.GetComponent<RectTransform>() : null;
            SetLayout(weightRect, minWidth: NativeRowWeightWidth, preferredWidth: NativeRowWeightWidth, minHeight: NativeRowWeightHeight, preferredHeight: NativeRowWeightHeight, flexibleWidth: 0f, flexibleHeight: 0f);
            ForceNativeRowWeightInputSize(weightSlot, weightInput);
            if (weightInput != null)
            {
                weightInput.characterLimit = 5;
                weightInput.interactable = canEditWeight;
                weightInput.readOnly = !canEditWeight;
                if (weightInput.textComponent != null)
                {
                    weightInput.textComponent.alignment = TextAlignmentOptions.Center;
                }

                if (canEditWeight)
                {
                    weightInput.onEndEdit.AddListener(value => ApplyNativeSpawnItemWeight(rowData, value));
                }
            }

            var canUseAction = isAddRow
                ? _viewMode == EditorViewMode.Edited
                : _viewMode == EditorViewMode.Edited && PresetPreviewBridge.CanOpen(rowData.Item);
            CreateNativeButtonOnly(
                holder,
                isAddRow ? "ADD" : "EDIT",
                canUseAction,
                () => HandleSpawnItemRowActivated(rowData),
                NativeRowControlWidth);
            holder.SetAsLastSibling();
        }

        private void AddNativeCompareStatusLabel(RectTransform row, CachedItemRow rowData)
        {
            if (row == null || string.IsNullOrWhiteSpace(rowData?.CompareStatusText))
            {
                return;
            }

            var existing = row.Find("ULE_CompareStatusLabel");
            if (existing != null)
            {
                Destroy(existing.gameObject);
            }

            var label = CreateNativeLabel(row, rowData.CompareStatusText, 11f, bold: false);
            if (label == null)
            {
                return;
            }

            label.name = "ULE_CompareStatusLabel";
            label.color = TextColor;
            NormalizeNativeTextVisual(label);
            label.enableAutoSizing = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            var rect = label.rectTransform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(
                -(NativeRowControlInset + NativeRowControlWidth + NativeCompareStatusSpacing),
                NativeRowControlYOffset);
            rect.sizeDelta = new Vector2(NativeCompareStatusWidth, 24f);
            SetLayoutIgnore(rect, true);
            label.transform.SetAsLastSibling();
        }

        private void AddNativeRemoveItemButton(RectTransform row, CachedItemRow rowData)
        {
            if (row == null ||
                rowData?.Item == null ||
                rowData.AddCandidate != null ||
                !string.IsNullOrWhiteSpace(rowData.CompareStatusText) ||
                _viewMode != EditorViewMode.Edited)
            {
                return;
            }

            var existing = row.Find("ULE_RemoveItemButton");
            if (existing != null)
            {
                Destroy(existing.gameObject);
            }

            var template = ResolveNativeCloseButtonTemplate();
            var buttonSize = GetNativeCloseButtonSize(template);
            var rect = CreateRect("ULE_RemoveItemButton", row);
            var removeTop = 12f;
            SetTopRight(rect, NativeRowControlInset, removeTop, buttonSize.x, buttonSize.y);
            SetLayoutIgnore(rect, true);

            Button button = null;
            if (template != null)
            {
                try
                {
                    var clone = Instantiate(template.gameObject, rect, false);
                    clone.name = "NativeCloseButton";
                    clone.SetActive(true);
                    SetLayerRecursively(clone, row.gameObject.layer);
                    var cloneRect = clone.transform as RectTransform;
                    SetTopLeft(cloneRect, 0f, 0f, buttonSize.x, buttonSize.y);
                    SetLayoutIgnore(cloneRect, true);
                    button = clone.GetComponent<Button>() ?? clone.GetComponentInChildren<Button>(true);
                    foreach (var childButton in clone.GetComponentsInChildren<Button>(true))
                    {
                        childButton.onClick.RemoveAllListeners();
                        childButton.interactable = true;
                    }
                }
                catch (Exception ex)
                {
                    _log?.LogDebug($"[ULE] Failed to clone native row remove button: {ex.Message}");
                    button = null;
                }
            }

            if (button == null)
            {
                var fallback = CreatePanel(rect, "FallbackCloseButton", new Color(0.38f, 0.035f, 0.03f, 0.95f));
                SetTopLeft(fallback, 0f, 0f, buttonSize.x, buttonSize.y);
                button = fallback.gameObject.AddComponent<Button>();
                button.targetGraphic = fallback.GetComponent<Image>();
                var label = CreateNativeLabel(fallback, "X", 12f, bold: true);
                if (label != null)
                {
                    label.alignment = TextAlignmentOptions.Center;
                    label.color = TextColor;
                    Stretch(label.rectTransform);
                }
            }

            rect.SetAsLastSibling();

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.interactable = true;
                button.onClick.AddListener(() => RemoveNativeSpawnItem(rowData.Item));
                foreach (var graphic in button.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.raycastTarget = true;
                }
            }
        }

        private Button ResolveNativeCloseButtonTemplate()
        {
            var root = ResolveInspectWindowRoot(_inspectWindowShell, _inspectPanelShell);
            return root?.Find("Inner/Caption Panel/Close Button")?.GetComponent<Button>()
                   ?? _inspectWindowShell?.CloseButton;
        }

        private static Vector2 GetNativeCloseButtonSize(Button template)
        {
            var rect = template != null ? template.transform as RectTransform : null;
            if (rect != null)
            {
                var width = rect.rect.width > 1f
                    ? rect.rect.width
                    : Mathf.Abs(rect.sizeDelta.x) > 1f
                        ? Mathf.Abs(rect.sizeDelta.x)
                        : NativeRowRemoveButtonSize;
                var height = rect.rect.height > 1f
                    ? rect.rect.height
                    : Mathf.Abs(rect.sizeDelta.y) > 1f
                        ? Mathf.Abs(rect.sizeDelta.y)
                        : NativeRowRemoveButtonSize;

                return new Vector2(width, height);
            }

            return new Vector2(NativeRowRemoveButtonSize, NativeRowRemoveButtonSize);
        }

        private void RemoveNativeSpawnItem(LootItem item)
        {
            var active = _viz?.ActiveSpawn;
            if (active == null || active.Items == null || item == null || !active.Items.Contains(item))
            {
                return;
            }

            PushUndoSnapshot($"remove-item:{GetStableItemKey(item)}", coalesce: false);
            active.Items.Remove(item);
            active.ItemCountSummary = active.Items.Count;
            active.DataVersion++;
            _viz.MarkActiveUnsaved();
            ShowStatus("Item removed.");
            MarkDirtyAndRebuild();
        }

        private static void ForceNativeRowWeightInputSize(RectTransform slot, TMP_InputField input)
        {
            if (slot == null)
            {
                return;
            }

            for (var i = 0; i < slot.childCount; i++)
            {
                if (slot.GetChild(i) is RectTransform child)
                {
                    SetTopLeft(child, 0f, 0f, NativeRowWeightWidth, NativeRowWeightHeight);
                }
            }

            var inputRect = input != null ? input.transform as RectTransform : null;
            SetTopLeft(inputRect, 0f, 0f, NativeRowWeightWidth, NativeRowWeightHeight);

            if (input != null)
            {
                NormalizeNativeInputViewport(input, 3f, 3f);
                if (input.textComponent != null)
                {
                    input.textComponent.fontSize = 11f;
                    input.textComponent.alignment = TextAlignmentOptions.Center;
                    input.textComponent.enableAutoSizing = false;
                    input.textComponent.enableWordWrapping = false;
                    input.textComponent.overflowMode = TextOverflowModes.Overflow;
                }
            }

            foreach (var layout in slot.GetComponentsInChildren<LayoutElement>(true))
            {
                layout.minWidth = NativeRowWeightWidth;
                layout.preferredWidth = NativeRowWeightWidth;
                layout.flexibleWidth = 0f;
                layout.minHeight = NativeRowWeightHeight;
                layout.preferredHeight = NativeRowWeightHeight;
                layout.flexibleHeight = 0f;
            }

            foreach (var fitter in slot.GetComponentsInChildren<ContentSizeFitter>(true))
            {
                fitter.enabled = false;
            }
        }

        private void ApplyNativeSpawnItemWeight(CachedItemRow rowData, string value)
        {
            var item = rowData?.Item;
            if (item == null || rowData.AddCandidate != null || _viewMode != EditorViewMode.Edited)
            {
                MarkDirtyAndRebuild();
                return;
            }

            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
            {
                MarkDirtyAndRebuild();
                return;
            }

            weight = Mathf.Max(0f, weight);
            if (Math.Abs(weight - item.Weight) <= 0.0001f)
            {
                MarkDirtyAndRebuild();
                return;
            }

            PushUndoSnapshot($"weight:{GetStableItemKey(item)}", coalesce: false);
            item.Weight = weight;
            if (_viz?.ActiveSpawn != null)
            {
                _viz.ActiveSpawn.DataVersion++;
            }

            _viz?.MarkActiveUnsaved();
            ShowStatus("Item weight updated.");
            MarkDirtyAndRebuild();
        }

        private static string BuildSpawnItemInfoText(LootItem item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            var parts = new List<string>
            {
                $"TPL: {item.Tpl}",
                $"Weight: {item.Weight:0.###}"
            };

            if (!string.IsNullOrWhiteSpace(item.PresetName))
            {
                parts.Add($"Preset: {item.PresetName.Trim()}");
            }

            if (PresetPreviewBridge.CanOpen(item))
            {
                var childCount = item.Children?.Count ?? 0;
                if (childCount > 0)
                {
                    parts.Add($"+{childCount}");
                }
            }

            return string.Join("  |  ", parts);
        }

        private static EntityListElement ResolveHandbookEntityRowTemplate()
        {
            if (_handbookEntityRowTemplate != null)
            {
                return _handbookEntityRowTemplate;
            }

            EnsureEntitiesPanelFields();

            foreach (var panel in Resources.FindObjectsOfTypeAll<EntitiesPanel>())
            {
                if (panel == null)
                {
                    continue;
                }

                var element = _entitiesPanelElementField?.GetValue(panel) as EntityListElement;
                if (IsValidHandbookTemplate(element))
                {
                    _handbookEntityRowTemplate = element;
                    return _handbookEntityRowTemplate;
                }
            }

            _handbookEntityRowTemplate = Resources
                .FindObjectsOfTypeAll<EntityListElement>()
                .Where(IsValidHandbookTemplate)
                .OrderByDescending(row => GetTransformPath(row.transform).IndexOf("HandbookScreen", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenBy(row => row.gameObject.activeSelf)
                .FirstOrDefault();
            return _handbookEntityRowTemplate;
        }

        private static GameObject ResolveHandbookEntityScrollTemplate()
        {
            if (_handbookEntityScrollTemplate != null)
            {
                return _handbookEntityScrollTemplate;
            }

            EnsureEntitiesPanelFields();

            foreach (var panel in Resources.FindObjectsOfTypeAll<EntitiesPanel>())
            {
                if (panel == null)
                {
                    continue;
                }

                var content = _entitiesPanelContentField?.GetValue(panel) as RectTransform;
                var scroll = content != null ? content.GetComponentInParent<ScrollRect>(true) : null;
                if (IsValidHandbookScroll(scroll))
                {
                    _handbookEntityScrollTemplate = scroll.gameObject;
                    return _handbookEntityScrollTemplate;
                }
            }

            var rowTemplate = ResolveHandbookEntityRowTemplate();
            var rowScroll = rowTemplate != null ? rowTemplate.GetComponentInParent<ScrollRect>(true) : null;
            if (IsValidHandbookScroll(rowScroll))
            {
                _handbookEntityScrollTemplate = rowScroll.gameObject;
                return _handbookEntityScrollTemplate;
            }

            _handbookEntityScrollTemplate = Resources
                .FindObjectsOfTypeAll<ScrollRect>()
                .Where(IsValidHandbookScroll)
                .OrderByDescending(scroll => GetTransformPath(scroll.transform).IndexOf("HandbookScreen", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(scroll => scroll.gameObject)
                .FirstOrDefault();

            return _handbookEntityScrollTemplate;
        }

        private static bool IsValidHandbookTemplate(EntityListElement row)
        {
            if (row == null || row.gameObject == null)
            {
                return false;
            }

            var path = GetTransformPath(row.transform);
            return path.IndexOf("ULE_", StringComparison.OrdinalIgnoreCase) < 0 &&
                   row.gameObject.name.IndexOf("EntityElement", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsValidHandbookScroll(ScrollRect scroll)
        {
            if (scroll == null || scroll.gameObject == null)
            {
                return false;
            }

            var path = GetTransformPath(scroll.transform);
            return path.IndexOf("ULE_", StringComparison.OrdinalIgnoreCase) < 0 &&
                   path.IndexOf("HandbookScreen", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   path.IndexOf("EntitiesView", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
            {
                names.Push(current.name ?? string.Empty);
            }

            return string.Join("/", names.ToArray());
        }

        private static void EnsureEntitiesPanelFields()
        {
            _entitiesPanelContentField ??= typeof(EntitiesPanel).GetField("_entityListContent", BindingFlags.Instance | BindingFlags.NonPublic);
            _entitiesPanelElementField ??= typeof(EntitiesPanel).GetField("_entityListElement", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static void EnsureHandbookRowFields()
        {
            _handbookEntityNameField ??= typeof(EntityListElement).GetField("_name", BindingFlags.Instance | BindingFlags.NonPublic);
            _handbookEntityCategoryField ??= typeof(EntityListElement).GetField("_itemCategory", BindingFlags.Instance | BindingFlags.NonPublic);
            _handbookEntityBackgroundField ??= typeof(EntityListElement).GetField("_background", BindingFlags.Instance | BindingFlags.NonPublic);
            _handbookEntityIconField ??= typeof(EntityListElement).GetField("_icon", BindingFlags.Instance | BindingFlags.NonPublic);
            _handbookEntityWishlistPanelField ??= typeof(EntityListElement).GetField("_wishlistPanel", BindingFlags.Instance | BindingFlags.NonPublic);
            _handbookEntityNewNodeObjectField ??= typeof(EntityListElement).GetField("_newNodeObject", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static void KeepHandbookIconFrameOnly(GameObject iconObject)
        {
            if (iconObject == null)
            {
                return;
            }

            iconObject.SetActive(true);
            foreach (var behaviour in iconObject.GetComponents<Component>())
            {
                if (behaviour is Behaviour b)
                {
                    b.enabled = false;
                }
            }

            foreach (var graphic in iconObject.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null)
                {
                    continue;
                }

                var name = graphic.gameObject != null ? graphic.gameObject.name : string.Empty;
                var keep = name.IndexOf("border", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           name.IndexOf("back", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           name.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           name.IndexOf("color", StringComparison.OrdinalIgnoreCase) >= 0;
                graphic.enabled = keep;
                graphic.raycastTarget = false;
            }
        }

        private void BuildNativeSkeletonCompare(SpawnPointData vanilla, SpawnPointData edited)
        {
            if (vanilla == null || !vanilla.DetailsLoaded)
            {
                CreateNativeLabel(_content, _viz.IsActiveVanillaLoading
                    ? "Vanilla loot table loading..."
                    : "Vanilla loot table is not ready yet.", 13f, bold: false);
                return;
            }

            var rows = BuildNativeCompareRows(vanilla, edited)
                .Where(row => string.IsNullOrWhiteSpace(_itemFilter) ||
                              row.SearchText.IndexOf(_itemFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            var emptyMessage = string.IsNullOrWhiteSpace(_itemFilter)
                ? "No compare differences."
                : "No compare differences match this search.";

            CreateNativeListSection("COMPARE RESULTS", rows, emptyMessage)
                .UseNativePanel(TryBuildHandbookSpawnItemsPanel)
                .UseFallbackScroll("NativeCompareScroll", 210f, 260f, (content, rowData) =>
                {
                    if (!TryCreateHandbookStyleSpawnItemRow(content, rowData))
                    {
                        CreateFallbackHandbookStyleSpawnItemRow(content, rowData);
                    }
                })
                .Build();
        }

        private RectTransform CreateNativeRow(Transform parent, string name, float height)
        {
            var row = CreateRect(name, parent);
            SetLayout(row, minHeight: height, preferredHeight: height, flexibleHeight: 0f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = NativeButtonSpacing;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            return row;
        }

        private NativeListSection<T> CreateNativeListSection<T>(string title, IReadOnlyList<T> rows, string emptyMessage)
        {
            return new NativeListSection<T>(this, title, rows, emptyMessage);
        }

        private NativeButtonStrip CreateNativeButtonStrip(Transform parent, string name, float height)
        {
            return new NativeButtonStrip(this, parent, name, height);
        }

        private RectTransform CreateNativeFreeRow(Transform parent, string name, float height)
        {
            var row = CreateRect(name, parent);
            SetLayout(row, minHeight: height, preferredHeight: height, flexibleHeight: 0f);
            return row;
        }

        private TextMeshProUGUI CreateNativeLabel(Transform parent, string text, float fontSize, bool bold, float minWidth = 0f, float flexibleWidth = 0f)
        {
            var template = GetNativeTextTemplate();
            if (template == null || parent == null)
            {
                return null;
            }

            var label = Instantiate(template, parent, false);
            label.name = "ULE_NativeLabel";
            label.gameObject.SetActive(true);
            StripNativeTextBindings(label.gameObject);
            label.text = text ?? string.Empty;
            label.fontSize = fontSize;
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            NormalizeNativeTextVisual(label);
            SetLayout(
                label.rectTransform,
                minHeight: Mathf.Max(GetNativeButtonHeight(), fontSize + 10f),
                minWidth: minWidth,
                preferredWidth: flexibleWidth > 0f ? 0f : minWidth > 0f ? minWidth : -1f,
                flexibleWidth: flexibleWidth);
            if (minWidth > 0f && flexibleWidth <= 0f)
            {
                label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, minWidth);
            }
            return label;
        }

        private static void NormalizeNativeTextVisual(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            text.enableVertexGradient = false;
            text.alpha = text.color.a;
        }

        private TextMeshProUGUI CreateNativeLabelAt(Transform parent, string text, float fontSize, bool bold, float x, float y, float width, float height)
        {
            var label = CreateNativeLabel(parent, text, fontSize, bold);
            if (label != null)
            {
                SetTopLeft(label.rectTransform, x, y, width, height);
            }

            return label;
        }

        private TextMeshProUGUI GetNativeTextTemplate()
        {
            var root = ResolveInspectWindowRoot(_inspectWindowShell, _inspectPanelShell);
            return root?.Find("Inner/Caption Panel/Item Type")?.GetComponentInChildren<TextMeshProUGUI>(true)
                   ?? _inspectWindowShell?.Caption
                   ?? root?.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        private static void StripNativeTextBindings(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            foreach (var component in go.GetComponents<Component>())
            {
                var typeName = component != null ? component.GetType().Name : string.Empty;
                if (typeName.IndexOf("Localized", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Destroy(component);
                }
            }
        }

        private bool CreateNativeButtonOnly(Transform parent, string text, bool enabled, Action onClick, float width, bool selected = false)
        {
            return TryCreateNativeInspectButton(parent, text, enabled, onClick, width, selected, out _);
        }

        private float GetNativeButtonHeight()
        {
            var rect = _inspectActionTemplate?.Transform as RectTransform;
            if (rect != null)
            {
                var layout = rect.GetComponent<LayoutElement>();
                if (layout != null && layout.preferredHeight > 1f)
                {
                    return layout.preferredHeight;
                }

                if (rect.sizeDelta.y > 1f)
                {
                    return rect.sizeDelta.y;
                }

                if (rect.rect.height > 1f)
                {
                    return rect.rect.height;
                }
            }

            return NativeButtonHeight;
        }

        private void ConfigureInspectActionButtons(SpawnPointData spawn)
        {
            if (!_usingInspectWindowShell || _inspectActionContainer == null || _inspectActionTemplate == null || _inspectActionHost == null)
            {
                return;
            }

            ResetInspectActionButtons();
            ConfigureInspectFooterAddSearch(spawn);

            CreateInspectActionButton("UNDO", CanUndoActiveSpawn, UndoActiveSpawnChange);
            CreateInspectActionButton("REDO", CanRedoActiveSpawn, RedoActiveSpawnChange);
            CreateInspectActionButton("SAVE", spawn != null, () => CloseEditor(save: true));
            PositionInspectFooterActions();
        }

        private void ConfigureInspectFooterAddSearch(SpawnPointData spawn)
        {
            if (_inspectFooterPanel == null)
            {
                return;
            }

            var existing = _inspectFooterPanel.Find("ULE_AddItemSearch") as RectTransform;
            if (existing != null)
            {
                Destroy(existing.gameObject);
            }

            var canAdd = _viewMode == EditorViewMode.Edited && spawn != null && spawn.DetailsLoaded;
            if (!canAdd)
            {
                _addQuery = string.Empty;
                _suggestions.Clear();
                return;
            }

            const float searchBottom = 38f;
            var input = CreateNativeInput(
                _inspectFooterPanel,
                "ULE_AddItemSearch",
                _addQuery,
                TMP_InputField.ContentType.Standard,
                TMP_InputField.CharacterValidation.None,
                "Search items to add",
                showSearchIcon: true,
                textFontSize: 14f);
            var rect = input != null ? input.GetComponent<RectTransform>() : null;
            SetBottomCenter(rect, searchBottom, NativeAddSearchWidth, NativeFooterInputHeight);
            SetLayoutIgnore(rect, true);

            if (input != null)
            {
                input.onEndEdit.AddListener(value =>
                {
                    _addQuery = value ?? string.Empty;
                    RefreshSuggestions();
                    MarkDirtyAndRebuild();
                });
            }
        }

        private void PositionInspectFooterActions()
        {
            if (_inspectActionHost == null)
            {
                return;
            }

            var actionWidth = NativeButtonWidth * 3f + NativeButtonSpacing * 2f;
            SetBottomCenter(_inspectActionHost, 16f, actionWidth, GetNativeButtonHeight());
            _inspectActionHost.SetAsLastSibling();
            SetLayoutIgnore(_inspectActionHost, true);
        }

        private void CreateInspectActionButton(string text, bool enabled, Action onClick)
        {
            try
            {
                var action = enabled && onClick != null ? onClick : new Action(() => { });
                var button = _inspectActionContainer.method_1(
                    "ULE_" + text,
                    text,
                    _inspectActionTemplate,
                    _inspectActionHost,
                    null,
                    action,
                    null,
                    false,
                    false);
                if (button == null)
                {
                    return;
                }

                button.name = "ULE_Action_" + text;
                var transform = button.Transform;
                if (transform != null)
                {
                    transform.name = "ULE_Action_" + text;
                    transform.gameObject.SetActive(true);
                }

                ConfigureNativeButtonSize(button, NativeButtonWidth, NativeButtonHeight);
                SetInspectActionButtonEnabled(button, enabled);
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to create native inspect action button '{text}': {ex.Message}");
            }
        }

        private static void SetInspectActionButtonEnabled(SimpleContextMenuButton button, bool enabled)
        {
            if (button == null)
            {
                return;
            }

            foreach (var unityButton in button.GetComponentsInChildren<Button>(true))
            {
                unityButton.interactable = enabled;
            }

            foreach (var canvasGroup in button.GetComponentsInChildren<CanvasGroup>(true))
            {
                canvasGroup.interactable = enabled;
                canvasGroup.blocksRaycasts = enabled;
                canvasGroup.alpha = enabled ? 1f : 0.45f;
            }
        }

        private void ResetInspectActionButtons()
        {
            if (_inspectActionHost == null)
            {
                return;
            }

            for (var i = _inspectActionHost.childCount - 1; i >= 0; i--)
            {
                var child = _inspectActionHost.GetChild(i);
                if (child.name.StartsWith("ULE_Action_", StringComparison.Ordinal) ||
                    child.name.IndexOf("(Clone)", StringComparison.Ordinal) >= 0)
                {
                    Destroy(child.gameObject);
                    continue;
                }

                if (child.GetComponentInChildren<SimpleContextMenuButton>(true) != null)
                {
                    child.gameObject.SetActive(false);
                }
            }
        }

        private void BuildStatusRow()
        {
            _statusText = CreateText(_content, "Status", string.Empty, 12, FontStyle.Normal, AccentColor);
            SetLayout(_statusText.rectTransform, minHeight: 20f);
            UpdateStatusLabel();
        }

        private void BuildModeAndNavigation(SpawnPointData spawn)
        {
            var row = CreatePanel(_content, "Modes", SectionColor);
            SetLayout(row, minHeight: 40f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 6, 6);
            layout.spacing = 6f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;

            CreateButton(row, "<", _viz.CanNavigateEditorPrevious, () => Navigate(-1), width: 36f);
            CreateButton(row, ">", _viz.CanNavigateEditorNext, () => Navigate(1), width: 36f);

            var count = Mathf.Max(1, _viz.EditorCandidateCount);
            var active = Mathf.Clamp(_viz.ActiveEditorCandidateIndex + 1, 1, count);
            var indexText = CreateText(row, "CandidateIndex", $"{active}/{count}", 12, FontStyle.Normal, SubtleTextColor);
            indexText.alignment = TextAnchor.MiddleCenter;
            SetLayout(indexText.rectTransform, minWidth: 54f);

            CreateModeButton(row, "Edited", EditorViewMode.Edited);
            CreateModeButton(row, "Vanilla", EditorViewMode.Vanilla);
            CreateModeButton(row, "Compare", EditorViewMode.Compare);

            var spacer = CreateRect("Spacer", row);
            SetLayout(spacer, flexibleWidth: 1f);

            var canRevert = _viz.ActiveVanillaSpawn != null && _viz.ActiveVanillaSpawn.DetailsLoaded;
            CreateButton(row, "Revert Vanilla", canRevert, () =>
            {
                PushUndoSnapshot("revert-vanilla", coalesce: false);
                if (_viz.RevertActiveToVanilla())
                {
                    _viewMode = EditorViewMode.Edited;
                    ResetSearchAndFilters();
                    ShowStatus("Reverted to vanilla spawn data.");
                    MarkDirtyAndRebuild();
                }
            }, width: 124f);
        }

        private void BuildSpawnChance(SpawnPointData spawn, bool editable, SpawnPointData vanilla)
        {
            var panel = CreatePanel(_content, "SpawnChance", SectionColor);
            SetLayout(panel, minHeight: editable && SupportsAlwaysSpawnUi(spawn, vanilla) ? 92f : 68f);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            BuildSmallText("SPAWN CHANCE [0..1]", TextColor, panel);

            var row = CreateRect("ChanceRow", panel);
            SetLayout(row, minHeight: 28f);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandHeight = true;

            _chanceSlider = row.gameObject.AddComponent<Slider>();
            var sliderRoot = CreateSliderVisual(row, _chanceSlider);
            SetLayout(sliderRoot, flexibleWidth: 1f, minWidth: 320f);
            _chanceSlider.minValue = 0f;
            _chanceSlider.maxValue = 1f;
            _chanceSlider.value = spawn != null ? Mathf.Clamp01(spawn.SpawnChance) : 0f;
            _chanceSlider.interactable = editable;

            _chanceInput = CreateInput(row, "ChanceInput", spawn != null ? FormatSpawnChance(spawn.SpawnChance) : FormatSpawnChance(0f));
            SetLayout(_chanceInput.GetComponent<RectTransform>(), minWidth: 90f, preferredWidth: 90f);
            _chanceInput.interactable = editable;

            if (editable)
            {
                _chanceSlider.onValueChanged.AddListener(value =>
                {
                    var active = _viz.ActiveSpawn;
                    if (active == null)
                    {
                        return;
                    }

                    if (Math.Abs(active.SpawnChance - value) <= 0.0001f)
                    {
                        return;
                    }

                    PushUndoSnapshot("spawn-chance", coalesce: true);
                    ApplyEditedSpawnChance(active, value);
                    if (_chanceInput != null)
                    {
                        _chanceInput.SetTextWithoutNotify(FormatSpawnChance(active.SpawnChance));
                    }
                });

                _chanceInput.onEndEdit.AddListener(text =>
                {
                    if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        var active = _viz.ActiveSpawn;
                        if (active != null)
                        {
                            PushUndoSnapshot("spawn-chance", coalesce: false);
                            ApplyEditedSpawnChance(active, Mathf.Clamp01(value));
                            MarkDirtyAndRebuild();
                        }
                    }
                    else
                    {
                        MarkDirtyAndRebuild();
                    }
                });
            }

            EnsureAlwaysSpawnSupportFromVanilla(spawn, vanilla);
            if (SupportsAlwaysSpawnUi(spawn, vanilla))
            {
                var alwaysRow = CreateRect("AlwaysSpawnRow", panel);
                SetLayout(alwaysRow, minHeight: 24f);
                var alwaysLayout = alwaysRow.gameObject.AddComponent<HorizontalLayoutGroup>();
                alwaysLayout.spacing = 8f;
                alwaysLayout.childControlHeight = true;
                alwaysLayout.childForceExpandHeight = true;

                _alwaysSpawnToggle = CreateToggle(alwaysRow, "Always Spawn", spawn != null && spawn.IsAlwaysSpawn);
                _alwaysSpawnToggle.interactable = editable;
                SetLayout(_alwaysSpawnToggle.GetComponent<RectTransform>(), minWidth: 150f);

                var modeText = spawn != null && spawn.IsAlwaysSpawn ? "Forced" : "Probability roll";
                var label = CreateText(alwaysRow, "AlwaysSpawnMode", modeText, 12, FontStyle.Normal, SubtleTextColor);
                label.alignment = TextAnchor.MiddleLeft;
                SetLayout(label.rectTransform, flexibleWidth: 1f);

                if (editable)
                {
                    _alwaysSpawnToggle.onValueChanged.AddListener(enabled =>
                    {
                        var active = _viz.ActiveSpawn;
                        if (active == null || active.IsAlwaysSpawn == enabled)
                        {
                            return;
                        }

                        PushUndoSnapshot("always-spawn", coalesce: false);
                        ApplyAlwaysSpawnToggle(active, _viz.ActiveVanillaSpawn, enabled);
                        MarkDirtyAndRebuild();
                    });
                }
            }
        }

        private void BuildItemList(SpawnPointData spawn, bool editable)
        {
            var panel = CreatePanel(_content, "Items", SectionColor);
            SetLayout(panel, flexibleHeight: 1f, minHeight: 250f);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            var filterRow = CreateRect("ItemFilterRow", panel);
            SetLayout(filterRow, minHeight: 30f);
            var filterLayout = filterRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            filterLayout.spacing = 8f;
            filterLayout.childControlHeight = true;
            filterLayout.childForceExpandHeight = true;

            BuildSmallText(editable ? "ITEMS AT THIS SPAWN" : "ITEMS IN THIS VIEW", TextColor, filterRow, minWidth: 150f);
            var filterInput = CreateInput(filterRow, "Filter", _itemFilter);
            SetLayout(filterInput.GetComponent<RectTransform>(), flexibleWidth: 1f);
            filterInput.onEndEdit.AddListener(value =>
            {
                _itemFilter = value ?? string.Empty;
                MarkDirtyAndRebuild();
            });

            var scroll = CreateScrollView(panel, "ItemsScroll", out var content);
            SetLayout(scroll, flexibleHeight: 1f, minHeight: 190f);

            if (spawn == null)
            {
                BuildSmallText("No spawn data is available for this view.", SubtleTextColor, content);
                return;
            }

            if (!spawn.DetailsLoaded)
            {
                BuildSmallText("Full loot table loading...", SubtleTextColor, content);
                return;
            }

            var rows = BuildItemRows(spawn)
                .Where(row => string.IsNullOrWhiteSpace(_itemFilter) ||
                              row.SearchText.IndexOf(_itemFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(row => row.Item.Weight)
                .ThenBy(row => row.Item.Tpl, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (rows.Count == 0)
            {
                BuildSmallText("No items match this filter.", SubtleTextColor, content);
                return;
            }

            for (var i = 0; i < rows.Count; i++)
            {
                BuildItemRow(content, rows[i], editable, i);
            }
        }

        private void BuildItemRow(RectTransform parent, CachedItemRow row, bool editable, int index)
        {
            var item = row.Item;
            var panel = CreatePanel(parent, "ItemRow", index % 2 == 0 ? RowColor : RowAltColor);
            SetLayout(panel, minHeight: 48f);
            var layout = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 5, 5);
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;

            var nameBlock = CreateRect("NameBlock", panel);
            SetLayout(nameBlock, flexibleWidth: 1f, minWidth: 250f);
            var nameLayout = nameBlock.gameObject.AddComponent<VerticalLayoutGroup>();
            nameLayout.spacing = 2f;
            nameLayout.childControlWidth = true;
            nameLayout.childForceExpandWidth = true;
            var name = CreateText(nameBlock, "Name", row.DisplayName, 12, FontStyle.Bold, TextColor);
            name.alignment = TextAnchor.LowerLeft;
            var tpl = CreateText(nameBlock, "Tpl", $"TPL: {item.Tpl}", 11, FontStyle.Normal, SubtleTextColor);
            tpl.alignment = TextAnchor.UpperLeft;

            if (PresetPreviewBridge.CanOpen(item))
            {
                CreateButton(panel, "Edit", true, () => OpenPresetScreen(item, readOnly: !editable), width: 54f);
            }
            else
            {
                var spacer = CreateRect("EditSpacer", panel);
                SetLayout(spacer, minWidth: 54f, preferredWidth: 54f);
            }

            BuildSmallText("W", SubtleTextColor, panel, minWidth: 14f);
            if (editable)
            {
                var weightInput = CreateInput(panel, "Weight", item.Weight.ToString("0.###", CultureInfo.InvariantCulture));
                SetLayout(weightInput.GetComponent<RectTransform>(), minWidth: 64f, preferredWidth: 64f);
                weightInput.onEndEdit.AddListener(value =>
                {
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
                    {
                        weight = Mathf.Max(0f, weight);
                        if (Math.Abs(weight - item.Weight) > 0.0001f)
                        {
                            PushUndoSnapshot($"weight:{GetStableItemKey(item)}", coalesce: false);
                            item.Weight = weight;
                            _viz.MarkActiveUnsaved();
                            MarkDirtyAndRebuild();
                        }
                    }
                    else
                    {
                        MarkDirtyAndRebuild();
                    }
                });

                CreateButton(panel, "X", true, () =>
                {
                    var active = _viz.ActiveSpawn;
                    if (active == null || active.Items == null || !active.Items.Contains(item))
                    {
                        return;
                    }

                    PushUndoSnapshot($"remove-item:{GetStableItemKey(item)}", coalesce: false);
                    active.Items.Remove(item);
                    active.ItemCountSummary = active.Items.Count;
                    active.DataVersion++;
                    _viz.MarkActiveUnsaved();
                    ShowStatus("Item removed.");
                    MarkDirtyAndRebuild();
                }, width: 34f);
            }
            else
            {
                var weight = CreateText(panel, "WeightText", item.Weight.ToString("0.###", CultureInfo.InvariantCulture), 12, FontStyle.Normal, TextColor);
                weight.alignment = TextAnchor.MiddleCenter;
                SetLayout(weight.rectTransform, minWidth: 64f, preferredWidth: 64f);
                var spacer = CreateRect("RemoveSpacer", panel);
                SetLayout(spacer, minWidth: 34f, preferredWidth: 34f);
            }
        }

        private void BuildAddItemSection(SpawnPointData spawn)
        {
            var panel = CreatePanel(_content, "AddItem", SectionColor);
            SetLayout(panel, minHeight: _suggestions.Count > 0 ? 220f : 76f);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            BuildSmallText("ADD ITEM BY TPL OR NAME", TextColor, panel);
            var row = CreateRect("SearchRow", panel);
            SetLayout(row, minHeight: 30f);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandHeight = true;

            var input = CreateInput(row, "AddQuery", _addQuery);
            SetLayout(input.GetComponent<RectTransform>(), flexibleWidth: 1f);
            input.onEndEdit.AddListener(value =>
            {
                _addQuery = value ?? string.Empty;
                RefreshSuggestions();
                MarkDirtyAndRebuild();
            });

            CreateButton(row, "Search", true, () =>
            {
                _addQuery = input.text ?? string.Empty;
                RefreshSuggestions();
                MarkDirtyAndRebuild();
            }, width: 82f);

            CreateButton(row, "Add First", _suggestions.Count > 0, () =>
            {
                if (_suggestions.Count > 0)
                {
                    AddSuggestionToSpawn(spawn, _suggestions[0]);
                }
            }, width: 92f);

            if (_suggestions.Count == 0)
            {
                return;
            }

            var scroll = CreateScrollView(panel, "SearchResults", out var content);
            SetLayout(scroll, minHeight: 130f, flexibleHeight: 1f);
            foreach (var suggestion in _suggestions.Take(MaxSearchResults))
            {
                var candidate = suggestion;
                var resultRow = CreatePanel(content, "SearchResult", RowColor);
                SetLayout(resultRow, minHeight: 48f);
                var resultLayout = resultRow.gameObject.AddComponent<HorizontalLayoutGroup>();
                resultLayout.padding = new RectOffset(8, 8, 5, 5);
                resultLayout.spacing = 8f;
                resultLayout.childControlHeight = true;
                resultLayout.childForceExpandHeight = true;

                var labelBlock = CreateRect("ResultText", resultRow);
                SetLayout(labelBlock, flexibleWidth: 1f);
                var labelLayout = labelBlock.gameObject.AddComponent<VerticalLayoutGroup>();
                labelLayout.spacing = 2f;
                var primary = CreateText(labelBlock, "Name", candidate.DisplayName, 12, FontStyle.Bold, TextColor);
                primary.alignment = TextAnchor.LowerLeft;
                var secondaryText = candidate.IsPreset && !string.IsNullOrWhiteSpace(candidate.TemplateItem?.PresetName)
                    ? $"TPL: {candidate.Tpl} | Preset: {candidate.TemplateItem.PresetName}"
                    : $"TPL: {candidate.Tpl}";
                var secondary = CreateText(labelBlock, "Tpl", secondaryText, 11, FontStyle.Normal, SubtleTextColor);
                secondary.alignment = TextAnchor.UpperLeft;

                CreateButton(resultRow, "Add", true, () => AddSuggestionToSpawn(spawn, candidate), width: 54f);
            }
        }

        private void BuildCompareView(SpawnPointData vanilla, SpawnPointData edited)
        {
            BuildCompareChance(vanilla, edited);

            var panel = CreatePanel(_content, "CompareItems", SectionColor);
            SetLayout(panel, flexibleHeight: 1f, minHeight: 300f);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            if (vanilla == null || !vanilla.DetailsLoaded)
            {
                BuildSmallText(_viz.IsActiveVanillaLoading
                    ? "Vanilla loot table loading..."
                    : "Vanilla loot table is not ready yet.", SubtleTextColor, panel);
                return;
            }

            var rows = BuildCompareRows(vanilla, edited)
                .Where(row => string.IsNullOrWhiteSpace(_itemFilter) ||
                              row.SearchText.IndexOf(_itemFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            var summary = CreateText(
                panel,
                "Summary",
                $"Changed: {rows.Count(row => row.Status == "Changed")} | Added: {rows.Count(row => row.Status == "Added")} | Removed: {rows.Count(row => row.Status == "Removed")}",
                12,
                FontStyle.Bold,
                TextColor);
            SetLayout(summary.rectTransform, minHeight: 22f);

            var scroll = CreateScrollView(panel, "CompareScroll", out var content);
            SetLayout(scroll, flexibleHeight: 1f, minHeight: 240f);

            foreach (var row in rows)
            {
                var line = CreatePanel(content, "CompareRow", RowColor);
                SetLayout(line, minHeight: 36f);
                var lineLayout = line.gameObject.AddComponent<HorizontalLayoutGroup>();
                lineLayout.padding = new RectOffset(8, 8, 4, 4);
                lineLayout.spacing = 8f;
                lineLayout.childControlHeight = true;
                lineLayout.childForceExpandHeight = true;

                BuildSmallText(row.DisplayName, TextColor, line, flexibleWidth: 1f, minWidth: 220f);
                BuildSmallText($"V: {row.VanillaWeightText}", SubtleTextColor, line, minWidth: 72f);
                BuildSmallText($"E: {row.EditedWeightText}", SubtleTextColor, line, minWidth: 72f);
                BuildSmallText(row.Status, AccentColor, line, minWidth: 72f);
            }
        }

        private void BuildCompareChance(SpawnPointData vanilla, SpawnPointData edited)
        {
            var panel = CreatePanel(_content, "CompareChance", SectionColor);
            SetLayout(panel, minHeight: 56f);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 4f;

            var vanillaChance = vanilla?.SpawnChance ?? 0f;
            var editedChance = edited?.SpawnChance ?? 0f;
            var delta = editedChance - vanillaChance;
            BuildSmallText($"SPAWN CHANCE: Vanilla {FormatSpawnChance(vanillaChance)} | Edited {FormatSpawnChance(editedChance)} | Delta {FormatSpawnChanceDelta(delta)}", TextColor, panel);
            if (SupportsAlwaysSpawnUi(edited, vanilla))
            {
                BuildSmallText($"ALWAYS SPAWN: Vanilla {(vanilla != null && vanilla.IsAlwaysSpawn ? "Yes" : "No")} | Edited {(edited != null && edited.IsAlwaysSpawn ? "Yes" : "No")}", SubtleTextColor, panel);
            }
        }

        private void Navigate(int direction)
        {
            if (_viz.NavigateEditorSelection(direction))
            {
                ResetSearchAndFilters();
                _lastBuildKey = string.Empty;
            }
        }

        private void OpenPresetScreen(LootItem item, bool readOnly)
        {
            if (item == null)
            {
                return;
            }

            if (!readOnly)
            {
                CapturePendingPreviewUndoSnapshot();
            }

            _openingPresetScreen = true;
            _ignoreNativeCloseUntil = 0f;
            var opened = false;
            string error = null;
            try
            {
                opened = PresetPreviewBridge.TryOpen(item, _log, applyOnClose: !readOnly, out error);
            }
            finally
            {
                _openingPresetScreen = false;
            }

            if (opened)
            {
                ReleaseInputCapture();
                DestroyEditorEventSystem();
                HideRoot();
                _presetScreenOpen = true;
                ClearStatus();
                return;
            }

            ClearPendingPreviewUndoSnapshot();
            ShowStatus(string.IsNullOrWhiteSpace(error) ? "Failed to open the preset screen." : error);
        }

        private void HandleNativeWindowCloseRequest()
        {
            var previewTransitionClose = _openingPresetScreen || _presetScreenOpen || PresetPreviewBridge.IsPreviewTransitionActiveOrOpen;
            var returningFromPresetClose = Time.unscaledTime < _ignoreNativeCloseUntil;
            if (previewTransitionClose || returningFromPresetClose)
            {
                _inspectInputWindow?.ResetInterceptedClose();
                if (previewTransitionClose)
                {
                    HideRoot();
                    ReleaseInputCapture();
                }

                return;
            }

            CloseEditor(save: false);
        }

        private void CloseEditor(bool save)
        {
            if (save && _viz?.ActiveSpawn != null)
            {
                _viz.CommitEdits(_viz.ActiveSpawn);
            }

            ReleaseInputCapture();
            _viz?.CloseEditorWithoutSaving();
            LootEditorGUI.Open = false;
            DestroyRoot();
            ResetWindowSessionState();
        }

        private void SyncActiveSpawnSession()
        {
            var spawn = _viz.ActiveSpawn;
            if (spawn == null)
            {
                return;
            }

            if (!string.Equals(_activeSpawnId, spawn.Id, StringComparison.Ordinal))
            {
                _activeSpawnId = spawn.Id ?? string.Empty;
                _activeSpawnDataVersion = spawn.DataVersion;
                _viewMode = EditorViewMode.Edited;
                ResetSearchAndFilters();
                ResetUndoRedoHistory();
                _lastBuildKey = string.Empty;
                return;
            }

            if (_activeSpawnDataVersion != spawn.DataVersion)
            {
                _activeSpawnDataVersion = spawn.DataVersion;
                _lastBuildKey = string.Empty;
            }
        }

        private string BuildUiKey()
        {
            var spawn = _viz.ActiveSpawn;
            if (spawn == null)
            {
                return "closed";
            }

            return string.Join("|",
                spawn.Id ?? string.Empty,
                spawn.DataVersion.ToString(CultureInfo.InvariantCulture),
                spawn.DetailsLoaded ? "loaded" : "unloaded",
                _viz.IsActiveSpawnLoading ? "loading" : "ready",
                _viz.IsActiveVanillaLoading ? "vloading" : "vready",
                _viz.ActiveVanillaSpawn?.DataVersion.ToString(CultureInfo.InvariantCulture) ?? "nov",
                _viz.ActiveEditorCandidateIndex.ToString(CultureInfo.InvariantCulture),
                _viz.EditorCandidateCount.ToString(CultureInfo.InvariantCulture),
                _viewMode.ToString(),
                _itemFilter ?? string.Empty,
                _addQuery ?? string.Empty,
                _suggestions.Count.ToString(CultureInfo.InvariantCulture),
                CanUndoActiveSpawn ? "undo" : "noundo",
                CanRedoActiveSpawn ? "redo" : "noredo");
        }

        private void MarkDirtyAndRebuild()
        {
            _lastBuildKey = string.Empty;
        }

        private void ResetWindowSessionState()
        {
            _activeSpawnId = string.Empty;
            _activeSpawnDataVersion = -1;
            _viewMode = EditorViewMode.Edited;
            ResetSearchAndFilters();
            ResetUndoRedoHistory();
            ClearPendingPreviewUndoSnapshot();
            ClearStatus();
            _presetScreenOpen = false;
            _ignoreNativeCloseUntil = 0f;
            _lastBuildKey = string.Empty;
        }

        private void ResetSearchAndFilters()
        {
            _itemFilter = string.Empty;
            _addQuery = string.Empty;
            _suggestions.Clear();
        }

        private void RefreshSuggestions()
        {
            var query = (_addQuery ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                _suggestions.Clear();
                return;
            }

            _suggestions = TplCache.Search(query, Plugin.IncludeUnsafeSearchItems)
                .Distinct(new SearchCandidateKeyComparer())
                .OrderBy(candidate => GetSuggestionRank(candidate, query))
                .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Tpl, StringComparer.OrdinalIgnoreCase)
                .Take(Plugin.SearchMaxRankedMatches)
                .ToList();
        }

        private void AddSuggestionToSpawn(SpawnPointData spawn, SearchCandidate candidate)
        {
            if (spawn == null || candidate == null || string.IsNullOrWhiteSpace(candidate.Tpl))
            {
                return;
            }

            var newItem = candidate.TemplateItem?.Clone() ?? new LootItem { Tpl = candidate.Tpl };
            newItem.ComposedKey = Util.GenerateComposedKey();
            newItem.Weight = 1f;
            LootAmmoAutoFill.FillMagazineAmmo(newItem);

            PushUndoSnapshot($"add-item:{candidate.StableKey}", coalesce: false);
            spawn.Items.Add(newItem);
            spawn.ItemCountSummary = spawn.Items.Count;
            spawn.DataVersion++;
            _viz.MarkActiveUnsaved();
            _suggestions.Clear();
            _addQuery = string.Empty;
            ShowStatus("Item added.");
            MarkDirtyAndRebuild();
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
            spawn.SpawnChance = enabled ? 1f : GetProbabilityWhenAlwaysSpawnDisabled(spawn, vanilla);
            _viz.MarkActiveUnsaved();
        }

        private static void EnsureAlwaysSpawnSupportFromVanilla(SpawnPointData spawn, SpawnPointData vanilla)
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
            MarkDirtyAndRebuild();
        }

        private void UndoActiveSpawnChange()
        {
            if (!CanUndoActiveSpawn)
            {
                return;
            }

            var current = CaptureActiveSpawnSnapshot();
            var previous = _undoHistory.Pop();
            PushHistorySnapshot(_redoHistory, current);
            RestoreActiveSpawnSnapshot(previous);
            ResetUndoCoalescing();
            ShowStatus("Undo applied.");
        }

        private void RedoActiveSpawnChange()
        {
            if (!CanRedoActiveSpawn)
            {
                return;
            }

            var current = CaptureActiveSpawnSnapshot();
            var next = _redoHistory.Pop();
            PushHistorySnapshot(_undoHistory, current);
            RestoreActiveSpawnSnapshot(next);
            ResetUndoCoalescing();
            ShowStatus("Redo applied.");
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
            _viewMode = EditorViewMode.Edited;
            ResetSearchAndFilters();
            ClearPendingPreviewUndoSnapshot();
            _viz.MarkActiveUnsaved();
            MarkDirtyAndRebuild();
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

        private void ShowStatus(string message, float durationSeconds = 4f)
        {
            _statusMessage = message ?? string.Empty;
            _statusMessageUntil = Time.unscaledTime + Mathf.Max(1f, durationSeconds);
            UpdateStatusLabel();
        }

        private void ClearStatus()
        {
            _statusMessage = string.Empty;
            _statusMessageUntil = 0f;
            UpdateStatusLabel();
        }

        private void UpdateStatusLabel()
        {
            if (_statusText == null)
            {
                return;
            }

            _statusText.text = !string.IsNullOrWhiteSpace(_statusMessage) && Time.unscaledTime <= _statusMessageUntil
                ? _statusMessage
                : string.Empty;
        }

        private void MaintainInputCapture()
        {
            if (!_inputCaptured)
            {
                _previousCursorVisible = Cursor.visible;
                _previousCursorLockState = Cursor.lockState;
                _raidInputIgnoredByEditor = true;
                SetRaidInputIgnored(ignore: true);
                _inputCaptured = true;
                ApplyEditorCursor();
            }
            else if (_raidInputIgnoredByEditor)
            {
                SetRaidInputIgnored(ignore: true);
            }

            if (!Cursor.visible)
            {
                Cursor.visible = true;
            }

            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
            }

            if (Time.unscaledTime - _lastEditorCursorApplyAt >= EditorCursorReapplyIntervalSeconds)
            {
                ApplyEditorCursor(force: true);
            }
        }

        private void SetEditorHoverCursor(ECursorType? cursor)
        {
            _editorHoverCursor = cursor;
            ApplyEditorCursor();
        }

        private void SetEditorForcedCursor(ECursorType? cursor)
        {
            _editorForcedCursor = cursor;
            ApplyEditorCursor();
        }

        private void ApplyEditorCursor(bool force = false)
        {
            var cursor = _editorForcedCursor ?? _editorHoverCursor ?? ECursorType.Idle;
            if (!force && _lastAppliedEditorCursor.HasValue && _lastAppliedEditorCursor.Value == cursor)
            {
                return;
            }

            try
            {
                if (force)
                {
                    ForceApplyEditorCursor(cursor);
                }
                else
                {
                    GClass3746.SetCursor(cursor);
                }

                _lastAppliedEditorCursor = cursor;
                _lastEditorCursorApplyAt = Time.unscaledTime;
            }
            catch
            {
                // EFT cursor assets may not be initialized in every context.
            }
        }

        private static void ForceApplyEditorCursor(ECursorType cursor)
        {
            var actualCursor = cursor == ECursorType.Idle ? GClass3746.EcursorType_0 : cursor;
            if (GClass3746.Dictionary_0 != null &&
                GClass3746.Dictionary_0.TryGetValue(actualCursor, out var cursorData) &&
                cursorData != null)
            {
                GClass3746.PreviousType = cursor;
                GClass3746.EcursorType_1 = actualCursor;
                GClass3746.smethod_0(cursorData);
                return;
            }

            GClass3746.SetCursor(cursor);
        }

        private void ReleaseInputCapture()
        {
            if (!_inputCaptured)
            {
                return;
            }

            if (_raidInputIgnoredByEditor)
            {
                SetRaidInputIgnored(ignore: false);
            }

            _raidInputIgnoredByEditor = false;
            _editorHoverCursor = null;
            _editorForcedCursor = null;
            _lastAppliedEditorCursor = null;
            ApplyEditorCursor(force: true);
            Cursor.visible = _previousCursorVisible;
            Cursor.lockState = _previousCursorLockState;
            _inputCaptured = false;
        }

        private void SetRaidInputIgnored(bool ignore)
        {
            try
            {
                GamePlayerOwner.SetIgnoreInput(ignore);
                GamePlayerOwner.SetIgnoreInputWithKeepResetLook(ignore);
                return;
            }
            catch
            {
                // Older or unusual contexts can fail direct calls; fall back to cached reflection below.
            }

            try
            {
                if (!_setIgnoreInputLookupAttempted)
                {
                    _setIgnoreInputLookupAttempted = true;
                    var gamePlayerOwnerType = Type.GetType("EFT.GamePlayerOwner, Assembly-CSharp", throwOnError: false);
                    _setIgnoreInputMethod = gamePlayerOwnerType?.GetMethod(
                        "SetIgnoreInput",
                        BindingFlags.Public | BindingFlags.Static,
                        binder: null,
                        types: new[] { typeof(bool) },
                        modifiers: null);
                    _setIgnoreInputWithKeepResetLookMethod = gamePlayerOwnerType?.GetMethod(
                        "SetIgnoreInputWithKeepResetLook",
                        BindingFlags.Public | BindingFlags.Static,
                        binder: null,
                        types: new[] { typeof(bool) },
                        modifiers: null);
                }

                _setIgnoreInputMethod?.Invoke(null, new object[] { ignore });
                _setIgnoreInputWithKeepResetLookMethod?.Invoke(null, new object[] { ignore });
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to toggle raid input ignore state: {ex.Message}");
            }
        }

        private void HideRoot()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        private void DestroyRoot()
        {
            UnregisterInspectInputWindow();

            if (_root != null)
            {
                Destroy(_root);
            }

            DestroyEditorEventSystem();

            _root = null;
            _window = null;
            _content = null;
            _statusText = null;
            _usingInspectWindowShell = false;
            _usingNativeInspectWindowContainer = false;
            _inspectWindowShell = null;
            _inspectInputWindow = null;
            _inspectPanelShell = null;
            _inspectActionContainer = null;
            _inspectActionTemplate = null;
            _inspectActionHost = null;
            _inspectFooterPanel = null;
        }

        private void EnsureEditorEventSystem()
        {
            if (_editorEventSystemRoot != null)
            {
                var existing = _editorEventSystemRoot.GetComponent<EventSystem>();
                if (existing != null)
                {
                    existing.enabled = true;
                    EventSystem.current = existing;
                }

                return;
            }

            var nativeEventSystem = EventSystem.current ?? FindObjectOfType<EventSystem>();
            if (nativeEventSystem != null && EnsureNativeEventSystemUsable(nativeEventSystem))
            {
                EventSystem.current = nativeEventSystem;
                return;
            }

            _previousEventSystem = EventSystem.current;
            _editorEventSystemRoot = new GameObject("ULE_TarkovEditorEventSystem");
            var eventSystem = _editorEventSystemRoot.AddComponent<EventSystem>();
            _editorEventSystemRoot.AddComponent<StandaloneInputModule>();
            EventSystem.current = eventSystem;
        }

        private static bool EnsureNativeEventSystemUsable(EventSystem eventSystem)
        {
            if (eventSystem == null)
            {
                return false;
            }

            if (!eventSystem.enabled)
            {
                eventSystem.enabled = true;
            }

            var modules = eventSystem.GetComponents<BaseInputModule>();
            foreach (var module in modules)
            {
                if (module == null)
                {
                    continue;
                }

                if (!module.enabled)
                {
                    module.enabled = true;
                }

                if (module.isActiveAndEnabled)
                {
                    return true;
                }
            }

            return false;
        }

        private void DestroyEditorEventSystem()
        {
            if (_editorEventSystemRoot != null)
            {
                Destroy(_editorEventSystemRoot);
                _editorEventSystemRoot = null;
            }

            if (_previousEventSystem != null)
            {
                EventSystem.current = _previousEventSystem;
                _previousEventSystem = null;
            }
        }

        private void NormalizeInspectWindowInteractionState()
        {
            if (_root == null)
            {
                return;
            }

            foreach (var group in _root.GetComponentsInChildren<CanvasGroup>(true))
            {
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;
                group.ignoreParentGroups = false;
            }

            foreach (var button in _root.GetComponentsInChildren<Button>(true))
            {
                foreach (var graphic in button.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.raycastTarget = true;
                }
            }

            foreach (var stretchArea in _root.GetComponentsInChildren<StretchArea>(true))
            {
                foreach (var graphic in stretchArea.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.raycastTarget = true;
                }
            }
        }

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var eventSystem = new GameObject("ULE_EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
            DontDestroyOnLoad(eventSystem);
        }

        private RectTransform CreatePanel(Transform parent, string name, Color color)
        {
            var rect = CreateRect(name, parent);
            AddImage(rect.gameObject, color);
            return rect;
        }

        private RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null)
            {
                go.layer = parent.gameObject.layer;
            }
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        private static Image AddImage(GameObject go, Color color)
        {
            var image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private Text CreateText(Transform parent, string name, string text, int fontSize, FontStyle style, Color color)
        {
            var rect = CreateRect(name, parent);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = _font;
            label.text = text ?? string.Empty;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private void BuildSmallText(string text, Color color, Transform parent = null, float minWidth = 0f, float flexibleWidth = 0f)
        {
            var label = CreateText(parent ?? _content, "Text", text, 12, FontStyle.Normal, color);
            SetLayout(label.rectTransform, minHeight: 22f, minWidth: minWidth, flexibleWidth: flexibleWidth);
        }

        private Button CreateButton(Transform parent, string text, bool enabled, Action onClick, float width)
        {
            if (TryCreateNativeInspectButton(parent, text, enabled, onClick, width, selected: false, out var nativeButton))
            {
                return nativeButton;
            }

            var rect = CreatePanel(parent, "Button_" + text, enabled ? ButtonColor : ButtonDisabledColor);
            SetLayout(rect, minWidth: width, preferredWidth: width, minHeight: NativeButtonHeight, preferredHeight: NativeButtonHeight, flexibleHeight: 0f);
            var button = rect.gameObject.AddComponent<Button>();
            button.interactable = enabled;
            button.targetGraphic = rect.GetComponent<Image>();
            if (enabled && onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            var label = CreateText(rect, "Label", text, 12, FontStyle.Bold, enabled ? TextColor : SubtleTextColor);
            label.alignment = TextAnchor.MiddleCenter;
            Stretch(label.rectTransform);
            return button;
        }

        private ButtonSpec CreateButtonSpec(string text, Action onClick)
        {
            return new ButtonSpec { Text = text, OnClick = onClick };
        }

        private void BuildButtonRow(params ButtonSpec[] buttons)
        {
            var row = CreateRect("ButtonRow", _content);
            SetLayout(row, minHeight: NativeButtonHeight + 6f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;
            foreach (var spec in buttons ?? Array.Empty<ButtonSpec>())
            {
                CreateButton(row, spec.Text, true, spec.OnClick, 88f);
            }
        }

        private void CreateModeButton(Transform parent, string text, EditorViewMode mode)
        {
            var selected = _viewMode == mode;
            if (TryCreateNativeInspectButton(parent, text, true, () =>
                {
                    _viewMode = mode;
                    _lastBuildKey = string.Empty;
                },
                82f,
                selected,
                out _))
            {
                return;
            }

            var rect = CreatePanel(parent, "Mode_" + text, selected ? AccentColor : ButtonColor);
            SetLayout(rect, minWidth: 82f, preferredWidth: 82f, minHeight: NativeButtonHeight, preferredHeight: NativeButtonHeight, flexibleHeight: 0f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(() =>
            {
                _viewMode = mode;
                _lastBuildKey = string.Empty;
            });
            var label = CreateText(rect, "Label", text, 12, FontStyle.Bold, TextColor);
            label.alignment = TextAnchor.MiddleCenter;
            Stretch(label.rectTransform);
        }

        private bool TryCreateNativeInspectButton(Transform parent, string text, bool enabled, Action onClick, float width, bool selected, out Button unityButton)
        {
            unityButton = null;
            if (!_usingInspectWindowShell || _inspectActionContainer == null || _inspectActionTemplate == null || parent == null)
            {
                return false;
            }

            var container = parent as RectTransform;
            if (container == null)
            {
                return false;
            }

            try
            {
                var action = enabled && onClick != null ? onClick : new Action(() => { });
                var button = _inspectActionContainer.method_1(
                    "ULE_BODY_" + SanitizeButtonKey(text) + "_" + container.childCount.ToString(CultureInfo.InvariantCulture),
                    text,
                    _inspectActionTemplate,
                    container,
                    null,
                    action,
                    null,
                    false,
                    false);
                if (button == null)
                {
                    return false;
                }

                button.name = "ULE_NativeButton_" + text;
                var transform = button.Transform as RectTransform;
                if (transform != null)
                {
                    transform.name = "ULE_NativeButton_" + text;
                }

                ConfigureNativeButtonSize(button, width, NativeButtonHeight);
                button.Blocked = selected;
                SetInspectActionButtonEnabled(button, enabled);

                unityButton = button.GetComponentsInChildren<Button>(true).FirstOrDefault();
                return unityButton != null;
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to create native inspect body button '{text}': {ex.Message}");
                return false;
            }
        }

        private static string SanitizeButtonKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "BUTTON";
            }

            var chars = text
                .Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_')
                .ToArray();
            return new string(chars);
        }

        private InputField CreateInput(Transform parent, string name, string value)
        {
            var rect = CreatePanel(parent, name, new Color(0.015f, 0.018f, 0.020f, 0.92f));
            SetLayout(rect, minHeight: NativeButtonHeight, preferredHeight: NativeButtonHeight, flexibleHeight: 0f);

            var textArea = CreateText(rect, "Text", value ?? string.Empty, 12, FontStyle.Normal, TextColor);
            textArea.alignment = TextAnchor.MiddleLeft;
            textArea.horizontalOverflow = HorizontalWrapMode.Overflow;
            textArea.verticalOverflow = VerticalWrapMode.Truncate;
            Stretch(textArea.rectTransform, 6f, 1f, 6f, 1f);

            var input = rect.gameObject.AddComponent<InputField>();
            input.textComponent = textArea;
            input.text = value ?? string.Empty;
            input.caretColor = TextColor;
            input.selectionColor = new Color(0.35f, 0.62f, 0.72f, 0.45f);
            input.targetGraphic = rect.GetComponent<Image>();
            return input;
        }

        private TMP_InputField CreateNativeInput(
            Transform parent,
            string name,
            string value,
            TMP_InputField.ContentType contentType = TMP_InputField.ContentType.DecimalNumber,
            TMP_InputField.CharacterValidation validation = TMP_InputField.CharacterValidation.Decimal,
            string placeholderText = "",
            bool showSearchIcon = false,
            float textFontSize = 13f)
        {
            if (TryCreateClonedNativeInput(parent, name, value, contentType, validation, placeholderText, showSearchIcon, textFontSize, out var nativeInput))
            {
                return nativeInput;
            }

            return CreateFallbackNativeInput(parent, name, value, contentType, validation, placeholderText, showSearchIcon, textFontSize);
        }

        private bool TryCreateClonedNativeInput(
            Transform parent,
            string name,
            string value,
            TMP_InputField.ContentType contentType,
            TMP_InputField.CharacterValidation validation,
            string placeholderText,
            bool showSearchIcon,
            float textFontSize,
            out TMP_InputField input)
        {
            input = null;
            var template = ResolveNativeTextInputTemplate();
            var source = template != null ? template.transform as RectTransform : null;
            if (source == null || parent == null)
            {
                return false;
            }

            try
            {
                var clone = Instantiate(source.gameObject, parent, false);
                clone.name = name;
                clone.SetActive(true);
                SetLayerRecursively(clone, parent.gameObject.layer);
                StripNativeTextBindingsRecursive(clone);
                SetNativeInputSearchDecorations(clone, showSearchIcon);

                input = clone.GetComponent<TMP_InputField>() ?? clone.GetComponentInChildren<TMP_InputField>(true);
                if (input == null)
                {
                    Destroy(clone);
                    return false;
                }

                var rect = input.transform as RectTransform;
                if (rect != null)
                {
                    rect.localScale = Vector3.one;
                    SetLayout(rect, minHeight: NativeButtonHeight, preferredHeight: NativeButtonHeight, flexibleHeight: 0f);
                }

                input.onValueChanged.RemoveAllListeners();
                input.onEndEdit.RemoveAllListeners();
                input.lineType = TMP_InputField.LineType.SingleLine;
                input.contentType = contentType;
                input.characterValidation = validation;
                input.interactable = true;
                input.readOnly = false;
                input.selectionColor = new Color(0.35f, 0.62f, 0.72f, 0.45f);

                if (input.textComponent != null)
                {
                    input.textComponent.fontSize = textFontSize;
                    input.textComponent.enableAutoSizing = false;
                    input.textComponent.enableWordWrapping = false;
                    input.textComponent.overflowMode = TextOverflowModes.Overflow;
                    input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
                    NormalizeNativeTextVisual(input.textComponent);
                }

                NormalizeNativeInputViewport(input, showSearchIcon ? 6f : 4f, showSearchIcon ? 34f : 4f);

                input.SetTextWithoutNotify(value ?? string.Empty);
                input.caretPosition = input.text.Length;
                input.stringPosition = input.text.Length;
                input.customCaretColor = true;
                input.caretColor = TextColor;
                input.caretWidth = 1;
                input.caretBlinkRate = 0.85f;
                EnsureNativeInputPlaceholder(input, placeholderText, textFontSize);

                return true;
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[ULE] Failed to clone native EFT input '{name}': {ex.Message}");
                input = null;
                return false;
            }
        }

        private void EnsureNativeInputPlaceholder(TMP_InputField input, string placeholderText, float textFontSize)
        {
            if (input == null || string.IsNullOrWhiteSpace(placeholderText))
            {
                return;
            }

            var parent = input.textViewport != null
                ? input.textViewport
                : input.transform as RectTransform;
            if (parent == null)
            {
                return;
            }

            if (input.placeholder is TMP_Text oldPlaceholder &&
                oldPlaceholder.transform != input.textComponent?.transform &&
                oldPlaceholder.name != "ULE_NativeInputPlaceholder")
            {
                oldPlaceholder.gameObject.SetActive(false);
            }

            var placeholder = parent.Find("ULE_NativeInputPlaceholder")?.GetComponent<TMP_Text>();
            if (placeholder == null)
            {
                placeholder = CreateNativeLabel(parent, placeholderText, textFontSize, bold: false);
                if (placeholder == null)
                {
                    return;
                }

                placeholder.name = "ULE_NativeInputPlaceholder";
            }

            input.placeholder = placeholder;
            placeholder.text = placeholderText;
            placeholder.fontSize = textFontSize;
            placeholder.enableAutoSizing = false;
            placeholder.enableWordWrapping = false;
            placeholder.overflowMode = TextOverflowModes.Ellipsis;
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            placeholder.color = SubtleTextColor;
            NormalizeNativeTextVisual(placeholder);
            placeholder.raycastTarget = false;
            Stretch(placeholder.rectTransform, 2f, 0f, 2f, 0f);
            placeholder.transform.SetAsLastSibling();
            placeholder.gameObject.SetActive(string.IsNullOrEmpty(input.text));
        }

        private TMP_InputField CreateFallbackNativeInput(
            Transform parent,
            string name,
            string value,
            TMP_InputField.ContentType contentType,
            TMP_InputField.CharacterValidation validation,
            string placeholderText,
            bool showSearchIcon,
            float textFontSize)
        {
            var rect = CreatePanel(parent, name, new Color(0.015f, 0.018f, 0.020f, 0.98f));
            SetLayout(rect, minHeight: NativeButtonHeight, preferredHeight: NativeButtonHeight, flexibleHeight: 0f);
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.42f, 0.48f, 0.50f, 0.85f);
            outline.effectDistance = new Vector2(1f, -1f);

            var viewport = CreateRect("Text Area", rect);
            Stretch(viewport, showSearchIcon ? 6f : 5f, 1f, showSearchIcon ? 34f : 5f, 1f);

            var text = CreateNativeLabel(viewport, value ?? string.Empty, textFontSize, bold: false);
            if (text != null)
            {
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.enableAutoSizing = false;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Overflow;
                text.raycastTarget = false;
                NormalizeNativeTextVisual(text);
                Stretch(text.rectTransform, 2f, 0f, 2f, 0f);
            }

            var placeholder = CreateNativeLabel(viewport, placeholderText ?? string.Empty, textFontSize, bold: false);
            if (placeholder != null)
            {
                placeholder.alignment = TextAlignmentOptions.MidlineLeft;
                placeholder.color = SubtleTextColor;
                placeholder.enableAutoSizing = false;
                placeholder.enableWordWrapping = false;
                placeholder.overflowMode = TextOverflowModes.Ellipsis;
                placeholder.raycastTarget = false;
                NormalizeNativeTextVisual(placeholder);
                Stretch(placeholder.rectTransform, 2f, 0f, 2f, 0f);
                placeholder.gameObject.SetActive(string.IsNullOrEmpty(value) && !string.IsNullOrWhiteSpace(placeholderText));
            }

            var input = rect.gameObject.AddComponent<TMP_InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.textViewport = viewport;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = contentType;
            input.characterValidation = validation;
            input.text = value ?? string.Empty;
            input.customCaretColor = true;
            input.caretColor = TextColor;
            input.caretWidth = 1;
            input.caretBlinkRate = 0.85f;
            input.selectionColor = new Color(0.35f, 0.62f, 0.72f, 0.45f);
            input.targetGraphic = rect.GetComponent<Image>();
            return input;
        }

        private static void SetNativeInputSearchDecorations(GameObject root, bool visible)
        {
            if (root == null)
            {
                return;
            }

            foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect == null || ReferenceEquals(rect.gameObject, root))
                {
                    continue;
                }

                if (!LooksLikeSearchDecoration(rect.gameObject.name))
                {
                    continue;
                }

                if (rect.GetComponent<TMP_InputField>() != null ||
                    rect.GetComponent<TMP_Text>() != null)
                {
                    continue;
                }

                rect.gameObject.SetActive(visible);
            }
        }

        private static bool LooksLikeSearchDecoration(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var lower = name.ToLowerInvariant();
            return lower == "search" ||
                   lower.Contains("searchicon") ||
                   lower.Contains("search icon") ||
                   lower.Contains("magnif") ||
                   lower.Contains("loupe") ||
                   lower.Contains("lens") ||
                   lower.Contains("findicon") ||
                   lower.Contains("find icon");
        }

        private static void NormalizeNativeInputViewport(TMP_InputField input, float left, float right)
        {
            if (input == null || input.textViewport == null)
            {
                return;
            }

            Stretch(input.textViewport, left, 1f, right, 1f);

            if (input.textComponent != null)
            {
                NormalizeNativeTextVisual(input.textComponent);
                Stretch(input.textComponent.rectTransform, 2f, 0f, 2f, 0f);
            }

            if (input.placeholder is TMP_Text placeholder)
            {
                NormalizeNativeTextVisual(placeholder);
                Stretch(placeholder.rectTransform, 2f, 0f, 2f, 0f);
            }
        }

        private TMP_InputField ResolveNativeTextInputTemplate()
        {
            if (_nativeTextInputTemplate != null)
            {
                return _nativeTextInputTemplate;
            }

            if (_browseSearchInputField == null)
            {
                _browseSearchInputField = typeof(BrowseCategoriesPanel).GetField("SearchInputField", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            if (_browseSearchInputField != null)
            {
                foreach (var panel in Resources.FindObjectsOfTypeAll<BrowseCategoriesPanel>())
                {
                    var input = _browseSearchInputField.GetValue(panel) as TMP_InputField;
                    if (IsUsableNativeTextInputTemplate(input))
                    {
                        _nativeTextInputTemplate = input;
                        return _nativeTextInputTemplate;
                    }
                }
            }

            _nativeTextInputTemplate = Resources.FindObjectsOfTypeAll<TMP_InputField>()
                .Where(IsUsableNativeTextInputTemplate)
                .OrderByDescending(input =>
                {
                    var path = GetTransformPath(input.transform);
                    var score = 0;
                    if (input.name.IndexOf("SearchInputField", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 100;
                    }
                    if (path.IndexOf("HandbookScreen", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 50;
                    }
                    if (path.IndexOf("Common UI", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 25;
                    }
                    return score;
                })
                .FirstOrDefault();

            return _nativeTextInputTemplate;
        }

        private static bool IsUsableNativeTextInputTemplate(TMP_InputField input)
        {
            if (input == null || input.transform == null)
            {
                return false;
            }

            var path = GetTransformPath(input.transform);
            if (path.IndexOf("ULE_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("TarkovStyleEditor", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return input.textComponent != null;
        }

        private static void StripNativeTextBindingsRecursive(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            foreach (var component in go.GetComponentsInChildren<Component>(true))
            {
                var typeName = component != null ? component.GetType().Name : string.Empty;
                if (typeName.IndexOf("Localized", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Destroy(component);
                }
            }
        }

        private Toggle CreateToggle(Transform parent, string labelText, bool value)
        {
            var row = CreateRect("Toggle_" + labelText, parent);
            SetLayout(row, minHeight: 24f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;

            var box = CreatePanel(row, "Box", new Color(0.02f, 0.025f, 0.03f, 0.95f));
            SetLayout(box, minWidth: 20f, preferredWidth: 20f, minHeight: 20f, preferredHeight: 20f);
            var check = CreatePanel(box, "Check", AccentColor);
            Stretch(check, 4f, 4f, 4f, 4f);

            var label = CreateText(row, "Label", labelText, 12, FontStyle.Normal, TextColor);
            SetLayout(label.rectTransform, minWidth: 120f);

            var toggle = row.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = box.GetComponent<Image>();
            toggle.graphic = check.GetComponent<Image>();
            toggle.isOn = value;
            return toggle;
        }

        private RectTransform CreateSliderVisual(Transform parent, Slider slider)
        {
            var root = CreateRect("SliderRoot", parent);
            SetLayout(root, minHeight: 28f);

            var background = CreatePanel(root, "Background", new Color(0.02f, 0.025f, 0.03f, 0.95f));
            Stretch(background, 0f, 10f, 0f, 10f);

            var fillArea = CreateRect("FillArea", root);
            Stretch(fillArea, 2f, 10f, 2f, 10f);
            var fill = CreatePanel(fillArea, "Fill", AccentColor);
            Stretch(fill);

            var handleArea = CreateRect("HandleArea", root);
            Stretch(handleArea, 0f, 4f, 0f, 4f);
            var handle = CreatePanel(handleArea, "Handle", TextColor);
            handle.sizeDelta = new Vector2(14f, 20f);

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            return root;
        }

        private RectTransform CreateScrollView(Transform parent, string name, out RectTransform content)
        {
            var root = CreatePanel(parent, name, new Color(0.01f, 0.012f, 0.014f, 0.55f));
            var scrollRect = root.gameObject.AddComponent<ScrollRect>();

            var viewport = CreatePanel(root, "Viewport", new Color(0f, 0f, 0f, 0f));
            Stretch(viewport);
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 35f;

            return root;
        }

        private void BuildCenteredMessage(string message)
        {
            var label = CreateText(_content, "Message", message, 14, FontStyle.Bold, TextColor);
            label.alignment = TextAnchor.MiddleCenter;
            SetLayout(label.rectTransform, flexibleHeight: 1f, minHeight: 180f);
        }

        private static void SetLayout(RectTransform rect, float minWidth = -1f, float minHeight = -1f, float preferredWidth = -1f, float preferredHeight = -1f, float flexibleWidth = -1f, float flexibleHeight = -1f)
        {
            if (rect == null)
            {
                return;
            }

            var layout = rect.gameObject.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            if (minWidth >= 0f)
            {
                layout.minWidth = minWidth;
            }
            if (minHeight >= 0f)
            {
                layout.minHeight = minHeight;
            }
            if (preferredWidth >= 0f)
            {
                layout.preferredWidth = preferredWidth;
            }
            if (preferredHeight >= 0f)
            {
                layout.preferredHeight = preferredHeight;
            }
            if (flexibleWidth >= 0f)
            {
                layout.flexibleWidth = flexibleWidth;
            }
            if (flexibleHeight >= 0f)
            {
                layout.flexibleHeight = flexibleHeight;
            }
        }

        private static void SetTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            SetLayout(rect, minWidth: width, minHeight: height, preferredWidth: width, preferredHeight: height, flexibleWidth: 0f, flexibleHeight: 0f);
        }

        private static void SetStretchTop(RectTransform rect, float left, float top, float right, float height)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
            SetLayout(rect, minHeight: height, preferredHeight: height, flexibleWidth: 1f, flexibleHeight: 0f);
        }

        private static void SetTopCenter(RectTransform rect, float y, float width, float height)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -y);
            rect.sizeDelta = new Vector2(width, height);
            SetLayout(rect, minWidth: width, minHeight: height, preferredWidth: width, preferredHeight: height, flexibleWidth: 0f, flexibleHeight: 0f);
        }

        private static void SetTopRight(RectTransform rect, float right, float y, float width, float height)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-right, -y);
            rect.sizeDelta = new Vector2(width, height);
            SetLayout(rect, minWidth: width, minHeight: height, preferredWidth: width, preferredHeight: height, flexibleWidth: 0f, flexibleHeight: 0f);
        }

        private static void SetBottomCenter(RectTransform rect, float bottom, float width, float height)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, bottom);
            rect.sizeDelta = new Vector2(width, height);
            SetLayout(rect, minWidth: width, minHeight: height, preferredWidth: width, preferredHeight: height, flexibleWidth: 0f, flexibleHeight: 0f);
        }

        private static void SetLayoutIgnore(RectTransform rect, bool ignore)
        {
            if (rect == null)
            {
                return;
            }

            var layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = ignore;
        }

        private static void Stretch(RectTransform rect, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void ClearChildren(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        private static List<CachedItemRow> BuildItemRows(SpawnPointData spawn)
        {
            return (spawn?.Items ?? new List<LootItem>())
                .Where(item => item != null)
                .Select(item =>
                {
                    var displayName = FormatItemDisplayName(item);
                    return new CachedItemRow
                    {
                        Item = item,
                        DisplayName = displayName,
                        SearchText = BuildItemSearchText(item, displayName)
                    };
                })
                .ToList();
        }

        private static List<CachedItemRow> BuildNativeCompareRows(SpawnPointData vanilla, SpawnPointData edited)
        {
            var vanillaByKey = (vanilla?.Items ?? new List<LootItem>())
                .Where(item => item != null)
                .ToDictionary(GetStableItemKey, item => item, StringComparer.Ordinal);

            var editedByKey = (edited?.Items ?? new List<LootItem>())
                .Where(item => item != null)
                .ToDictionary(GetStableItemKey, item => item, StringComparer.Ordinal);

            return vanillaByKey.Keys
                .Concat(editedByKey.Keys)
                .Distinct(StringComparer.Ordinal)
                .Select(key =>
                {
                    vanillaByKey.TryGetValue(key, out var vanillaItem);
                    editedByKey.TryGetValue(key, out var editedItem);

                    var item = editedItem ?? vanillaItem;
                    if (item == null)
                    {
                        return null;
                    }

                    var status = BuildNativeCompareStatus(vanillaItem, editedItem);
                    if (string.IsNullOrWhiteSpace(status))
                    {
                        return null;
                    }

                    var displayName = FormatItemDisplayName(item);
                    return new CachedItemRow
                    {
                        Item = item,
                        DisplayName = displayName,
                        SearchText = BuildItemSearchText(item, displayName) + "\n" + status,
                        CompareStatusText = status,
                        CompareStatusRank = GetNativeCompareStatusRank(status)
                    };
                })
                .Where(row => row != null)
                .OrderBy(row => row.CompareStatusRank)
                .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Item?.Tpl, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildNativeCompareStatus(LootItem vanillaItem, LootItem editedItem)
        {
            if (vanillaItem == null && editedItem != null)
            {
                return "Added";
            }

            if (vanillaItem != null && editedItem == null)
            {
                return "Removed";
            }

            if (vanillaItem == null || editedItem == null)
            {
                return string.Empty;
            }

            if (Math.Abs(vanillaItem.Weight - editedItem.Weight) > 0.0001f)
            {
                return $"Weight changed: {vanillaItem.Weight:0.###} -> {editedItem.Weight:0.###}";
            }

            return AreLootItemsEquivalent(vanillaItem, editedItem)
                ? string.Empty
                : "Changed";
        }

        private static int GetNativeCompareStatusRank(string status)
        {
            if (status.StartsWith("Weight changed", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            switch (status)
            {
                case "Added":
                    return 1;
                case "Removed":
                    return 2;
                case "Changed":
                    return 3;
                default:
                    return 4;
            }
        }

        private static List<CompareItemRow> BuildCompareRows(SpawnPointData vanilla, SpawnPointData edited)
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

                    var displayName = FormatItemDisplayName(item);
                    return new CompareItemRow
                    {
                        DisplayName = displayName,
                        Tpl = item?.Tpl ?? string.Empty,
                        VanillaWeightText = vanillaItem != null ? vanillaItem.Weight.ToString("0.###", CultureInfo.InvariantCulture) : "-",
                        EditedWeightText = editedItem != null ? editedItem.Weight.ToString("0.###", CultureInfo.InvariantCulture) : "-",
                        Status = status,
                        SearchText = BuildItemSearchText(item, displayName) + "\n" + status
                    };
                })
                .OrderBy(row => CompareStatusRank(row.Status))
                .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Tpl, StringComparer.OrdinalIgnoreCase)
                .ToList();
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

            if (displayName.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                presetName.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return candidate.IsPreset ? 4 : 5;
            }

            if (tpl.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.IsPreset ? 6 : 7;
            }

            return candidate.IsPreset ? 8 : 9;
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

        private sealed class ButtonSpec
        {
            public string Text;
            public Action OnClick;
        }

        private sealed class NativeButtonStrip
        {
            private readonly TarkovLootEditorUI _ui;
            private readonly RectTransform _row;
            private readonly float _height;

            public NativeButtonStrip(TarkovLootEditorUI ui, Transform parent, string name, float height)
            {
                _ui = ui;
                _height = Mathf.Max(1f, height);
                _row = ui != null ? ui.CreateNativeRow(parent, name, _height) : null;
            }

            public NativeButtonStrip At(float x, float y, float width, float height = -1f)
            {
                if (_row != null)
                {
                    SetTopLeft(_row, x, y, width, height > 0f ? height : _height);
                    SetLayoutIgnore(_row, true);
                }

                return this;
            }

            public NativeButtonStrip CenteredAt(float y, float width, float height = -1f)
            {
                if (_row != null)
                {
                    SetTopCenter(_row, y, width, height > 0f ? height : _height);
                    SetLayoutIgnore(_row, true);
                }

                return this;
            }

            public NativeButtonStrip RightAt(float right, float y, float width, float height = -1f)
            {
                if (_row != null)
                {
                    SetTopRight(_row, right, y, width, height > 0f ? height : _height);
                    SetLayoutIgnore(_row, true);
                }

                return this;
            }

            public NativeButtonStrip AddButton(
                string text,
                bool enabled,
                Action onClick,
                float width,
                string targetName = null,
                bool selected = false)
            {
                if (_ui == null || _row == null)
                {
                    return this;
                }

                _ui.CreateNativeButtonOnly(_row, text, enabled, onClick, width, selected);
                return this;
            }

            public NativeButtonStrip AddLabel(
                string text,
                float width,
                string targetName = null,
                TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
            {
                if (_ui == null || _row == null)
                {
                    return this;
                }

                var label = _ui.CreateNativeLabel(_row, text, 13f, bold: false, minWidth: width);
                if (label != null)
                {
                    label.alignment = alignment;
                    SetLayout(label.rectTransform, minWidth: width, preferredWidth: width, flexibleWidth: 0f);
                }

                return this;
            }
        }

        private sealed class NativeListSection<T>
        {
            private readonly TarkovLootEditorUI _ui;
            private readonly string _title;
            private readonly IReadOnlyList<T> _rows;
            private readonly string _emptyMessage;
            private Func<IReadOnlyList<T>, bool> _nativePanelBuilder;
            private Action<RectTransform, T> _fallbackRowBuilder;
            private string _fallbackScrollName = "NativeListScroll";
            private float _fallbackMinHeight = 210f;
            private float _fallbackPreferredHeight = 260f;

            public NativeListSection(TarkovLootEditorUI ui, string title, IReadOnlyList<T> rows, string emptyMessage)
            {
                _ui = ui;
                _title = title ?? string.Empty;
                _rows = rows ?? Array.Empty<T>();
                _emptyMessage = emptyMessage ?? string.Empty;
            }

            public NativeListSection<T> UseNativePanel(Func<IReadOnlyList<T>, bool> builder)
            {
                _nativePanelBuilder = builder;
                return this;
            }

            public NativeListSection<T> UseFallbackScroll(
                string name,
                float minHeight,
                float preferredHeight,
                Action<RectTransform, T> rowBuilder)
            {
                _fallbackScrollName = string.IsNullOrWhiteSpace(name) ? _fallbackScrollName : name;
                _fallbackMinHeight = minHeight;
                _fallbackPreferredHeight = preferredHeight;
                _fallbackRowBuilder = rowBuilder;
                return this;
            }

            public void Build()
            {
                if (_ui == null || _ui._content == null)
                {
                    return;
                }

                _ui.CreateNativeLabel(_ui._content, _title, 14f, bold: true);

                if (_rows.Count == 0)
                {
                    _ui.CreateNativeLabel(_ui._content, _emptyMessage, 13f, bold: false);
                    return;
                }

                if (_nativePanelBuilder != null && _nativePanelBuilder(_rows))
                {
                    return;
                }

                if (_fallbackRowBuilder == null)
                {
                    return;
                }

                var scroll = _ui.CreateScrollView(_ui._content, _fallbackScrollName, out var content);
                SetLayout(scroll, minHeight: _fallbackMinHeight, preferredHeight: _fallbackPreferredHeight, flexibleHeight: 1f);
                foreach (var row in _rows)
                {
                    _fallbackRowBuilder(content, row);
                }
            }
        }

        private sealed class CachedItemRow
        {
            public LootItem Item;
            public string DisplayName;
            public string SearchText;
            public SearchCandidate AddCandidate;
            public string CompareStatusText;
            public int CompareStatusRank;
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

        private sealed class DraggableWindow : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            private RectTransform _target;
            private Vector2 _startPointer;
            private Vector2 _startPosition;

            public void Init(RectTransform target)
            {
                _target = target;
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (_target == null)
                {
                    return;
                }

                _startPointer = eventData.position;
                TryGetPointerLocal(_target, eventData, out _startPointer);
                _startPosition = _target.anchoredPosition;
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (_target == null)
                {
                    return;
                }

                if (!TryGetPointerLocal(_target, eventData, out var pointer))
                {
                    return;
                }

                var delta = pointer - _startPointer;
                _target.anchoredPosition = ClampWindowPosition(_target, _startPosition + new Vector2(delta.x, delta.y));
            }
        }

        private sealed class ResizableWindow : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            private RectTransform _target;
            private Vector2 _startPointer;
            private Vector2 _startSize;
            private float _minWidth;
            private float _minHeight;

            public void Init(RectTransform target, float minWidth, float minHeight)
            {
                _target = target;
                _minWidth = minWidth;
                _minHeight = minHeight;
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (_target == null)
                {
                    return;
                }

                _startPointer = eventData.position;
                TryGetPointerLocal(_target, eventData, out _startPointer);
                _startSize = _target.sizeDelta;
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (_target == null)
                {
                    return;
                }

                if (!TryGetPointerLocal(_target, eventData, out var pointer))
                {
                    return;
                }

                var delta = pointer - _startPointer;
                var areaSize = GetClampAreaSize(_target);
                var maxWidth = Mathf.Max(_minWidth, areaSize.x - WindowMargin * 2f);
                var maxHeight = Mathf.Max(_minHeight, areaSize.y - WindowMargin * 2f);
                var nextSize = new Vector2(
                    Mathf.Clamp(_startSize.x + delta.x, _minWidth, maxWidth),
                    Mathf.Clamp(_startSize.y - delta.y, _minHeight, maxHeight));
                _target.sizeDelta = nextSize;

                var layout = _target.GetComponent<LayoutElement>();
                if (layout != null)
                {
                    layout.preferredWidth = nextSize.x;
                    layout.preferredHeight = nextSize.y;
                }

                _target.anchoredPosition = ClampWindowPosition(_target, _target.anchoredPosition);
            }
        }

        private sealed class ResizeCursorZone : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
        {
            private Action<ECursorType?> _hoverCursor;
            private Action<ECursorType?> _forcedCursor;

            public void Init(Action<ECursorType?> hoverCursor, Action<ECursorType?> forcedCursor)
            {
                _hoverCursor = hoverCursor;
                _forcedCursor = forcedCursor;
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                _hoverCursor?.Invoke(ECursorType.StretchCorner);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                _hoverCursor?.Invoke(null);
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                _forcedCursor?.Invoke(ECursorType.StretchCorner);
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                _forcedCursor?.Invoke(null);
            }

            private void OnDisable()
            {
                _hoverCursor?.Invoke(null);
                _forcedCursor?.Invoke(null);
            }
        }

        private static bool TryGetPointerLocal(RectTransform target, PointerEventData eventData, out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            if (target == null || eventData == null)
            {
                return false;
            }

            var parent = target.parent as RectTransform;
            if (parent == null)
            {
                var canvas = target.GetComponentInParent<Canvas>();
                parent = canvas != null ? canvas.transform as RectTransform : null;
            }

            if (parent == null)
            {
                localPoint = eventData.position;
                return true;
            }

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent,
                eventData.position,
                eventData.pressEventCamera,
                out localPoint);
        }

        private static Vector2 ClampWindowPosition(RectTransform window, Vector2 position)
        {
            if (window == null)
            {
                return position;
            }

            var width = window.rect.width;
            var height = window.rect.height;
            var areaSize = GetClampAreaSize(window);
            var minX = WindowMargin;
            var maxX = width >= areaSize.x ? minX : Mathf.Max(minX, areaSize.x - width - WindowMargin);
            var maxY = -WindowMargin;
            var minY = height >= areaSize.y ? maxY : Mathf.Min(maxY, -(areaSize.y - height - WindowMargin));
            return new Vector2(
                Mathf.Clamp(position.x, minX, maxX),
                Mathf.Clamp(position.y, minY, maxY));
        }

        private static Vector2 GetClampAreaSize(RectTransform window)
        {
            if (window != null)
            {
                var parent = window.parent as RectTransform;
                if (parent != null && parent.rect.width > 1f && parent.rect.height > 1f)
                {
                    return parent.rect.size;
                }

                var canvas = window.GetComponentInParent<Canvas>();
                var canvasRect = canvas != null ? canvas.transform as RectTransform : null;
                if (canvasRect != null && canvasRect.rect.width > 1f && canvasRect.rect.height > 1f)
                {
                    return canvasRect.rect.size;
                }
            }

            return new Vector2(Screen.width, Screen.height);
        }
    }
}
#endregion
