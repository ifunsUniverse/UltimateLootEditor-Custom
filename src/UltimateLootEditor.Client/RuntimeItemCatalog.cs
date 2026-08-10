#region RuntimeItemCatalog.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

namespace ULE.SpawnEditor
{
    internal static class RuntimeItemCatalog
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, RuntimeItemInfo> Items = new Dictionary<string, RuntimeItemInfo>(StringComparer.OrdinalIgnoreCase);
        private static int _lastTemplateCount = -1;
        private static bool _hasLoggedBuild;

        private static readonly HashSet<string> AbstractInternalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Item",
            "Money",
            "Key",
            "Pistol",
            "Smg",
            "AssaultRifle",
            "AssaultCarbine",
            "Shotgun",
            "MarksmanRifle",
            "SniperRifle",
            "MachineGun",
            "GrenadeLauncher",
            "Knife",
            "BarterItem",
            "Stimulator",
            "Pockets",
            "SearchableItem",
            "Electronics",
            "Battery",
            "SimpleContainer",
            "ArmBand",
            "Revolver",
            "RocketLauncher",
            "Rocket",
            "Customization",
        };

        private static readonly HashSet<string> PreviewInternalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Pistol",
            "Revolver",
            "Smg",
            "AssaultRifle",
            "AssaultCarbine",
            "Shotgun",
            "MarksmanRifle",
            "SniperRifle",
            "MachineGun",
            "GrenadeLauncher",
            "ArmorVest",
            "Headwear",
            "Helmet",
        };

        public static string NameFromTpl(string tpl)
        {
            if (string.IsNullOrWhiteSpace(tpl))
            {
                return null;
            }

            EnsureBuilt(null);
            lock (Sync)
            {
                return Items.TryGetValue(tpl, out var info) ? info.DisplayName : null;
            }
        }

        public static bool ContainsTpl(string tpl)
        {
            if (string.IsNullOrWhiteSpace(tpl))
            {
                return false;
            }

            EnsureBuilt(null);
            lock (Sync)
            {
                return Items.ContainsKey(tpl);
            }
        }

        public static string SourceNameFromTpl(string tpl)
        {
            if (string.IsNullOrWhiteSpace(tpl))
            {
                return string.Empty;
            }

            EnsureBuilt(null);
            lock (Sync)
            {
                if (Items.TryGetValue(tpl, out var info) && !string.IsNullOrWhiteSpace(info.SourceName))
                {
                    return info.SourceName;
                }
            }

            return RuntimeItemSourceCatalog.SourceFromTpl(tpl);
        }

        public static bool IsQuestItem(string tpl)
        {
            if (string.IsNullOrWhiteSpace(tpl))
            {
                return false;
            }

            EnsureBuilt(null);
            lock (Sync)
            {
                return Items.TryGetValue(tpl, out var info) && info.QuestItem;
            }
        }

        public static bool IsUnsafeItem(string tpl)
        {
            if (string.IsNullOrWhiteSpace(tpl))
            {
                return false;
            }

            EnsureBuilt(null);
            lock (Sync)
            {
                return Items.TryGetValue(tpl, out var info) && info.Unsafe;
            }
        }

        public static bool SupportsPresetPreview(string tpl)
        {
            if (string.IsNullOrWhiteSpace(tpl))
            {
                return false;
            }

            EnsureBuilt(null);
            lock (Sync)
            {
                return Items.TryGetValue(tpl, out var info) && info.SupportsPresetPreview;
            }
        }

        public static IEnumerable<SearchCandidate> Search(string query, bool includeUnsafe = false)
        {
            query = (query ?? string.Empty).Trim();
            if (query.Length == 0)
            {
                yield break;
            }

            EnsureBuilt(null);

            List<RuntimeItemInfo> snapshot;
            lock (Sync)
            {
                snapshot = Items.Values.ToList();
            }

            foreach (var info in snapshot
                .Where(info => includeUnsafe || !info.Unsafe)
                .Where(info => MatchesText(info.Tpl, query)
                               || MatchesText(info.DisplayName, query)
                               || MatchesText(info.ShortName, query)
                               || MatchesText(SourceNameForSearch(info), query)
                               || MatchesText(info.InternalName, query))
                .OrderBy(info => info.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(info => info.Tpl, StringComparer.OrdinalIgnoreCase))
            {
                yield return new SearchCandidate
                {
                    Tpl = info.Tpl,
                    DisplayName = BuildDisplayName(info, includeSafetyBadges: includeUnsafe),
                    IsPreset = false,
                    TemplateItem = new LootItem
                    {
                        Tpl = info.Tpl
                    }
                };
            }
        }

        private static void EnsureBuilt(BepInEx.Logging.ManualLogSource log)
        {
            try
            {
                if (!Singleton<EFT.ItemFactory>.Instantiated)
                {
                    return;
                }

                var itemFactory = Singleton<EFT.ItemFactory>.Instance;
                var templates = itemFactory?.ItemTemplates;
                var count = templates?.Count ?? 0;
                if (count == 0)
                {
                    return;
                }

                lock (Sync)
                {
                    if (_lastTemplateCount == count && Items.Count > 0)
                    {
                        return;
                    }

                    Items.Clear();
                    foreach (var pair in templates)
                    {
                        var template = pair.Value;
                        if (template == null || template._type != NodeType.Item)
                        {
                            continue;
                        }

                        var tpl = pair.Key.ToString();
                        if (string.IsNullOrWhiteSpace(tpl))
                        {
                            continue;
                        }

                        var displayName = ResolveDisplayName(template);
                        var shortName = ResolveShortName(template);
                        var internalName = template._name ?? string.Empty;
                        var unsafeItem = IsUnsafeForSearch(template, displayName, shortName, templates);

                        Items[tpl] = new RuntimeItemInfo
                        {
                            Tpl = tpl,
                            DisplayName = displayName,
                            ShortName = shortName,
                            InternalName = internalName,
                            SourceName = RuntimeItemSourceCatalog.SourceFromTpl(tpl),
                            QuestItem = template.QuestItem,
                            Unsafe = unsafeItem,
                            SupportsPresetPreview = SupportsPreview(template, templates)
                        };
                    }

                    _lastTemplateCount = count;
                    if (!_hasLoggedBuild)
                    {
                        _hasLoggedBuild = true;
                        log?.LogInfo($"[ULE] Runtime item catalog detected {Items.Count} live item templates.");
                    }
                }
            }
            catch
            {
                // Runtime template access can be unavailable during early boot; the disk catalog remains as fallback.
            }
        }

        private static string ResolveDisplayName(ItemTemplate template)
        {
            if (template == null)
            {
                return string.Empty;
            }

            var key = template.NameLocalizationKey;
            var localized = ResolveLocalized(key);
            if (!string.IsNullOrWhiteSpace(localized) && !string.Equals(localized, key, StringComparison.OrdinalIgnoreCase))
            {
                return localized.Trim();
            }

            if (!string.IsNullOrWhiteSpace(template.Name))
            {
                return template.Name.Trim();
            }

            if (!string.IsNullOrWhiteSpace(template.ShortName))
            {
                return template.ShortName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(template._name))
            {
                return template._name.Trim();
            }

            return template._id.ToString();
        }

        private static string ResolveShortName(ItemTemplate template)
        {
            if (template == null)
            {
                return string.Empty;
            }

            var key = template.ShortNameLocalizationKey;
            var localized = ResolveLocalized(key);
            if (!string.IsNullOrWhiteSpace(localized) && !string.Equals(localized, key, StringComparison.OrdinalIgnoreCase))
            {
                return localized.Trim();
            }

            return template.ShortName ?? string.Empty;
        }

        private static string ResolveLocalized(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            try
            {
                return key.Localized(null);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool SupportsPreview(ItemTemplate template, IDictionary<MongoID, ItemTemplate> templates)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = template;
            while (current != null && visited.Add(current._id.ToString()))
            {
                if (PreviewInternalNames.Contains(current._name ?? string.Empty))
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

        private static bool IsUnsafeForSearch(
            ItemTemplate template,
            string displayName,
            string shortName,
            IDictionary<MongoID, ItemTemplate> templates)
        {
            if (template == null)
            {
                return true;
            }

            var internalName = template._name ?? string.Empty;
            if (AbstractInternalNames.Contains(internalName))
            {
                return true;
            }

            if (IsBuiltInInsertTemplate(template, templates))
            {
                return true;
            }

            if (IsAirdropSupplyContainer(internalName, displayName, shortName))
            {
                return true;
            }

            if (string.Equals(displayName, "Item", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ContainsDoNotUseMarker(displayName)
                   || ContainsDoNotUseMarker(shortName)
                   || ContainsDoNotUseMarker(template.Description)
                   || ContainsDoNotUseMarker(internalName);
        }

        private static bool IsBuiltInInsertTemplate(ItemTemplate template, IDictionary<MongoID, ItemTemplate> templates)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

        private static bool IsAirdropSupplyContainer(string internalName, string displayName, string shortName)
        {
            var normalizedInternal = (internalName ?? string.Empty).Trim();
            if (normalizedInternal.IndexOf("event_container_airdrop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedInternal.IndexOf("air_drop_supply", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return LooksLikeAirdropSupplyContainerName(displayName) ||
                   LooksLikeAirdropSupplyContainerName(shortName);
        }

        private static bool LooksLikeAirdropSupplyContainerName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.IndexOf("airdrop", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   (value.IndexOf("crate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("supply", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string BuildDisplayName(RuntimeItemInfo info, bool includeSafetyBadges)
        {
            if (info == null)
            {
                return string.Empty;
            }

            var name = string.IsNullOrWhiteSpace(info.DisplayName) ? info.Tpl : info.DisplayName;
            name = RuntimeItemSourceCatalog.PrefixDisplayName(info.Tpl, name);

            if (!includeSafetyBadges)
            {
                return name;
            }

            if (info.QuestItem)
            {
                return $"{name} [QUEST]";
            }

            return info.Unsafe ? $"{name} [UNSAFE]" : name;
        }

        private static bool ContainsDoNotUseMarker(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim();
            return normalized.IndexOf("!!!DO_NOT_USE!!!", StringComparison.OrdinalIgnoreCase) >= 0
                   || normalized.IndexOf("!!!DO NOT USE!!!", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string SourceNameForSearch(RuntimeItemInfo info)
        {
            if (info == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(info.SourceName)
                ? RuntimeItemSourceCatalog.SourceFromTpl(info.Tpl)
                : info.SourceName;
        }

        private static bool MatchesText(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value)
                   && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class RuntimeItemInfo
        {
            public string Tpl;
            public string DisplayName;
            public string ShortName;
            public string InternalName;
            public string SourceName;
            public bool QuestItem;
            public bool Unsafe;
            public bool SupportsPresetPreview;
        }
    }
}
#endregion
