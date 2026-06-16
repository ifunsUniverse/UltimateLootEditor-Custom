using System;
using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.WeaponModding;
using UnityEngine;

namespace ULE.SpawnEditor
{
    internal sealed class PresetWorkbenchSession : IDisposable
    {
        internal sealed class AttachmentRow
        {
            public LootItemNode Node;
            public LootItemNode Parent;
            public int Depth;
            public string SlotLabel;
            public string DisplayName;
            public string Tpl;
            public bool CanRemove;
        }

        private readonly List<AttachmentRow> _rows = new List<AttachmentRow>(64);

        private BepInEx.Logging.ManualLogSource _log;
        private WeaponPreviewPool _previewPool;
        private WeaponPreview _preview;
        private RenderTexture _previewTexture;
        private int _previewWidth;
        private int _previewHeight;
        private Item _runtimeItem;
        private LootItem _sourceItem;
        private LootItem _originalItem;
        private LootItem _workingItem;
        private bool _readOnly;
        private bool _liveCameraOverlay;

        public bool IsOpen => _workingItem != null;

        public bool IsReadOnly => _readOnly;

        public bool UsesLiveCameraOverlay => _liveCameraOverlay;

        public LootItem WorkingItem => _workingItem;

        public Texture PreviewTexture => _previewTexture;

        public string HeaderTitle
        {
            get
            {
                if (_workingItem == null)
                {
                    return "Preset Workbench";
                }

                var baseName = TplCache.DisplayNameFromTpl(_workingItem.Tpl, includeSafetyBadges: false);
                if (string.IsNullOrWhiteSpace(_workingItem.PresetName))
                {
                    return baseName;
                }

                if (baseName.IndexOf(_workingItem.PresetName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return baseName;
                }

                return $"{baseName} - {_workingItem.PresetName}";
            }
        }

        public int AttachmentCount => Mathf.Max(0, _rows.Count - 1);

        public bool TryOpen(LootItem sourceItem, bool readOnly, BepInEx.Logging.ManualLogSource log, out string error)
        {
            return TryOpen(sourceItem, readOnly, log, false, out error);
        }

        public bool TryOpen(LootItem sourceItem, bool readOnly, BepInEx.Logging.ManualLogSource log, bool liveCameraOverlay, out string error)
        {
            Close();

            error = null;
            if (sourceItem == null)
            {
                error = "This loot entry is missing.";
                return false;
            }

            if (!PresetPreviewBridge.CanOpen(sourceItem))
            {
                error = "This item does not support the preset workbench.";
                return false;
            }

            _log = log;
            _sourceItem = sourceItem;
            _originalItem = sourceItem.Clone();
            _workingItem = sourceItem.Clone();
            _readOnly = readOnly;
            _liveCameraOverlay = liveCameraOverlay;

            if (!AcquirePreview(out error))
            {
                Close();
                return false;
            }

            if (!RebuildPreview(out error))
            {
                Close();
                return false;
            }

            return true;
        }

        public void EnsurePreviewTexture(int width, int height)
        {
            if (_preview == null)
            {
                return;
            }

            if (_liveCameraOverlay)
            {
                ConfigurePreviewCamera();
                return;
            }

            var clampedWidth = Mathf.Clamp(width, 128, 1024);
            var clampedHeight = Mathf.Clamp(height, 128, 1024);
            if (_previewTexture != null && _previewWidth == clampedWidth && _previewHeight == clampedHeight)
            {
                return;
            }

            ReleasePreviewTexture();

            _previewWidth = clampedWidth;
            _previewHeight = clampedHeight;
            _previewTexture = new RenderTexture(_previewWidth, _previewHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "ULE_PresetWorkbenchPreview",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };

            ConfigurePreviewCamera();
        }

        public void RotatePreview(float deltaX, float deltaY)
        {
            _preview?.Rotate(deltaX, deltaY, -85f, 85f);
        }

        public void ZoomPreview(float delta)
        {
            if (_preview == null || Math.Abs(delta) < 0.0001f)
            {
                return;
            }

            _preview.Zoom(Mathf.Clamp(delta, -0.25f, 0.25f));
        }

        public IReadOnlyList<AttachmentRow> GetRows()
        {
            return _rows;
        }

        public bool TryApply()
        {
            if (_readOnly || _sourceItem == null || _workingItem == null)
            {
                return false;
            }

            CopyLootItem(_workingItem, _sourceItem);
            return true;
        }

        public bool TryReset(out string error)
        {
            error = null;
            if (_originalItem == null)
            {
                return false;
            }

            _workingItem = _originalItem.Clone();
            return RebuildPreview(out error);
        }

        public bool TryRemoveNode(AttachmentRow row, out string error)
        {
            error = null;
            if (_readOnly)
            {
                error = "This workbench view is read-only.";
                return false;
            }

            if (row == null || row.Node == null || row.Parent == null || !row.CanRemove)
            {
                error = "That part cannot be removed from this view.";
                return false;
            }

            var parentChildren = row.Parent.Children;
            if (parentChildren == null)
            {
                error = "The selected part is not attached correctly.";
                return false;
            }

            var index = parentChildren.IndexOf(row.Node);
            if (index < 0)
            {
                error = "The selected part is no longer present.";
                return false;
            }

            var removed = row.Node.CloneNode();
            parentChildren.RemoveAt(index);

            if (RebuildPreview(out error))
            {
                return true;
            }

            parentChildren.Insert(index, removed);
            RebuildPreview(out _);
            return false;
        }

        public void Close()
        {
            ReleasePreviewTexture();

            if (_preview != null)
            {
                try
                {
                    if (_preview.WeaponPreviewCamera != null)
                    {
                        _preview.WeaponPreviewCamera.targetTexture = null;
                    }

                    _preview.Hide();
                }
                catch
                {
                    // Ignore cleanup issues during teardown.
                }

                try
                {
                    _previewPool?.ReturnToPool(_preview);
                }
                catch
                {
                    // Ignore pool-return errors during teardown.
                }
            }

            _rows.Clear();
            _preview = null;
            _previewPool = null;
            _runtimeItem = null;
            _sourceItem = null;
            _originalItem = null;
            _workingItem = null;
            _log = null;
            _readOnly = false;
            _liveCameraOverlay = false;
        }

        public void Dispose()
        {
            Close();
        }

        private bool AcquirePreview(out string error)
        {
            error = null;

            var uiContext = ItemUiContext.Instance;
            _previewPool = uiContext != null
                ? uiContext.WeaponPreviewPool
                : null;

            if (_previewPool == null)
            {
                _previewPool = Resources.FindObjectsOfTypeAll<WeaponPreviewPool>().FirstOrDefault();
            }

            if (_previewPool == null)
            {
                error = "Weapon preview pool is not ready yet.";
                return false;
            }

            _preview = _previewPool.GetWeaponPreview();
            if (_preview == null)
            {
                error = "Failed to acquire a weapon preview instance.";
                return false;
            }

            return true;
        }

        private bool RebuildPreview(out string error)
        {
            error = null;

            if (_workingItem == null)
            {
                error = "The workbench item is missing.";
                return false;
            }

            if (_preview == null && !AcquirePreview(out error))
            {
                return false;
            }

            if (!PresetPreviewBridge.TryCreateRuntimeItem(_workingItem, _log, out var runtimeItem, out error))
            {
                return false;
            }

            _runtimeItem = runtimeItem;
            _preview.SetupItemPreview(_runtimeItem, null, null, null, true, null, true);
            ConfigurePreviewCamera();
            RebuildRows();
            return true;
        }

        private void ConfigurePreviewCamera()
        {
            var camera = _preview?.WeaponPreviewCamera;
            if (camera == null)
            {
                return;
            }

            camera.enabled = true;
            camera.targetTexture = _liveCameraOverlay ? null : _previewTexture;
            camera.clearFlags = _liveCameraOverlay
                ? CameraClearFlags.Depth
                : CameraClearFlags.SolidColor;
            camera.backgroundColor = _liveCameraOverlay
                ? new Color(0f, 0f, 0f, 0f)
                : new Color(0.11f, 0.12f, 0.14f, 1f);
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.useOcclusionCulling = false;
            camera.renderingPath = RenderingPath.Forward;
            camera.depth = _liveCameraOverlay
                ? 15f
                : camera.depth;

            var previewLayer = LayerMask.NameToLayer("Weapon Preview");
            if (previewLayer >= 0)
            {
                camera.cullingMask = 1 << previewLayer;
            }
        }

        private void ReleasePreviewTexture()
        {
            if (_previewTexture == null)
            {
                return;
            }

            try
            {
                _previewTexture.Release();
            }
            catch
            {
                // Ignore release errors during teardown.
            }

            UnityEngine.Object.Destroy(_previewTexture);
            _previewTexture = null;
            _previewWidth = 0;
            _previewHeight = 0;
        }

        private void RebuildRows()
        {
            _rows.Clear();
            if (_workingItem == null)
            {
                return;
            }

            _rows.Add(new AttachmentRow
            {
                Node = _workingItem,
                Parent = null,
                Depth = 0,
                SlotLabel = "ROOT",
                DisplayName = HeaderTitle,
                Tpl = _workingItem.Tpl,
                CanRemove = false
            });

            AppendRows(_workingItem, _workingItem.Children, depth: 1);
        }

        private void AppendRows(LootItemNode parent, IEnumerable<LootItemNode> children, int depth)
        {
            if (children == null)
            {
                return;
            }

            foreach (var child in children)
            {
                if (child == null)
                {
                    continue;
                }

                _rows.Add(new AttachmentRow
                {
                    Node = child,
                    Parent = parent,
                    Depth = depth,
                    SlotLabel = string.IsNullOrWhiteSpace(child.SlotId) ? "PART" : child.SlotId.ToUpperInvariant(),
                    DisplayName = BuildNodeDisplayName(child),
                    Tpl = child.Tpl,
                    CanRemove = true
                });

                AppendRows(child, child.Children, depth + 1);
            }
        }

        private static string BuildNodeDisplayName(LootItemNode node)
        {
            if (node == null)
            {
                return "Unknown part";
            }

            var name = TplCache.DisplayNameFromTpl(node.Tpl, includeSafetyBadges: false);
            var stackCount = node.StackMin.HasValue && node.StackMin.Value > 1
                ? $" x{node.StackMin.Value}"
                : string.Empty;
            return string.IsNullOrWhiteSpace(name) ? (node.Tpl ?? "Unknown part") + stackCount : name + stackCount;
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
    }
}
