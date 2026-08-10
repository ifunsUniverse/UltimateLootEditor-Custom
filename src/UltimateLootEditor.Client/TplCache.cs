// TplCache.cs
#region TplCache.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace ULE.SpawnEditor
{
    internal static class TplCache
    {
        private static Dictionary<string, string> _tplToName;
        private static Dictionary<string, string> _nameToTpl;
        private static HashSet<string> _questItems;
        private static HashSet<string> _unsafeItems;
        private static HashSet<string> _presetPreviewItems;
        private static List<SearchCandidate> _presetCandidates;
        private static List<SearchCandidate> _runtimePresetCandidates;
        private static Dictionary<string, string> _localeNames;
        private static bool _runtimePresetCandidatesLoaded;
        private static DateTime _nextRuntimePresetRetryUtc = DateTime.MinValue;
        private const string RuntimeItemPresetsRoute = "/ule/runtime/item-presets";

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

        private static readonly HashSet<string> WeaponPreviewInternalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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
        };

        private static readonly HashSet<string> GearPreviewInternalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ArmorVest",
            "Headwear",
            "Helmet",
        };

        private sealed class ItemTemplate
        {
            [JsonProperty("_name")]
            public string InternalName { get; set; }

            [JsonProperty("_parent")]
            public string ParentId { get; set; }

            [JsonProperty("_type")]
            public string Type { get; set; }

            [JsonProperty("_props")]
            public ItemTemplateProps Props { get; set; }
        }

        private sealed class ItemTemplateProps
        {
            [JsonProperty("QuestItem")]
            public bool QuestItem { get; set; }

            [JsonProperty("Unlootable")]
            public bool Unlootable { get; set; }

            [JsonProperty("IsSpecialSlotOnly")]
            public bool IsSpecialSlotOnly { get; set; }
        }

        private sealed class GlobalsFile
        {
            [JsonProperty("ItemPresets")]
            public Dictionary<string, PresetTemplate> ItemPresets { get; set; }
                = new Dictionary<string, PresetTemplate>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class PresetTemplate
        {
            [JsonProperty("_id")]
            public string Id { get; set; }

            [JsonProperty("_name")]
            public string Name { get; set; }

            [JsonProperty("_items")]
            public List<PresetTemplateItem> Items { get; set; } = new List<PresetTemplateItem>();
        }

        private sealed class PresetTemplateItem
        {
            [JsonProperty("_id")]
            public string Id { get; set; }

            [JsonProperty("_tpl")]
            public string Tpl { get; set; }

            [JsonProperty("parentId")]
            public string ParentId { get; set; }

            [JsonProperty("slotId")]
            public string SlotId { get; set; }

            [JsonProperty("location")]
            public JToken Location { get; set; }

            [JsonProperty("upd")]
            public JToken Upd { get; set; }
        }

        private sealed class PresetRecord
        {
            public string Id;
            public string ParentId;
            public string Tpl;
            public string SlotId;
            public string LocationJson;
            public string UpdJson;
            public int? StackCount;
            public List<PresetRecord> Children = new List<PresetRecord>();
        }

        public static void BuildIfMissing(BepInEx.Logging.ManualLogSource log)
        {
            string enPath = DbPaths.ResolveLocalesEnPath();
            if (string.IsNullOrEmpty(enPath) || !File.Exists(enPath))
            {
                log?.LogWarning("[ULE] en.json not found; item names will be unavailable.");
                _tplToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _nameToTpl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _questItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _unsafeItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _presetPreviewItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _presetCandidates = new List<SearchCandidate>();
                _runtimePresetCandidates = new List<SearchCandidate>();
                _localeNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            string itemsPath = DbPaths.ResolveItemsPath();
            string globalsPath = DbPaths.ResolveGlobalsPath();

            try
            {
                var text = File.ReadAllText(enPath);
                var localeNames = JsonConvert.DeserializeObject<Dictionary<string, string>>(text)
                                  ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _localeNames = localeNames;

                var templates = new Dictionary<string, ItemTemplate>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(itemsPath) && File.Exists(itemsPath))
                {
                    var itemText = File.ReadAllText(itemsPath);
                    templates = JsonConvert.DeserializeObject<Dictionary<string, ItemTemplate>>(itemText)
                                ?? new Dictionary<string, ItemTemplate>(StringComparer.OrdinalIgnoreCase);
                }

                var globals = new GlobalsFile();
                if (!string.IsNullOrEmpty(globalsPath) && File.Exists(globalsPath))
                {
                    var globalsText = File.ReadAllText(globalsPath);
                    globals = JsonConvert.DeserializeObject<GlobalsFile>(globalsText) ?? new GlobalsFile();
                }

                var rx = new Regex("^([0-9a-f]{24})\\s+Name$", RegexOptions.IgnoreCase);

                _tplToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _nameToTpl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _questItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _unsafeItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _presetPreviewItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _presetCandidates = new List<SearchCandidate>();
                _runtimePresetCandidates = new List<SearchCandidate>();
                _runtimePresetCandidatesLoaded = false;
                _nextRuntimePresetRetryUtc = DateTime.MinValue;

                foreach (var kv in localeNames)
                {
                    var m = rx.Match(kv.Key);
                    if (!m.Success)
                    {
                        continue;
                    }

                    string tpl = m.Groups[1].Value;
                    string name = kv.Value?.Trim();
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    if (templates.Count > 0 && !templates.ContainsKey(tpl))
                    {
                        continue;
                    }

                    _tplToName[tpl] = name;
                    _nameToTpl[name] = tpl;
                    if (templates.TryGetValue(tpl, out var template))
                    {
                        if (template?.Props?.QuestItem == true)
                        {
                            _questItems.Add(tpl);
                        }

                        if (IsUnsafeForSearch(tpl, template, name, localeNames, templates))
                        {
                            _unsafeItems.Add(tpl);
                        }

                        if (SupportsPresetPreview(tpl, templates))
                        {
                            _presetPreviewItems.Add(tpl);
                        }
                    }
                }

                _presetCandidates = BuildPresetCandidates(globals.ItemPresets, localeNames, templates);

                log?.LogInfo($"[ULE] TPL data built in memory with {_tplToName.Count} items and {_presetCandidates.Count} presets.");
                log?.LogInfo($"[ULE] TPL names source: {enPath}");
                if (!string.IsNullOrEmpty(itemsPath))
                {
                    log?.LogInfo($"[ULE] TPL item source: {itemsPath}");
                }
                if (!string.IsNullOrEmpty(globalsPath))
                {
                    log?.LogInfo($"[ULE] TPL preset source: {globalsPath}");
                }
                log?.LogInfo($"[ULE] Safe-item filter marked {_unsafeItems.Count} items as hidden by default.");
            }
            catch (Exception ex)
            {
                log?.LogError($"[ULE] Failed to build TPL data from source files. Error: {ex}");
                _tplToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _nameToTpl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _questItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _unsafeItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _presetPreviewItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _presetCandidates = new List<SearchCandidate>();
                _runtimePresetCandidates = new List<SearchCandidate>();
                _localeNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public static string NameFromTpl(string tpl)
        {
            if (_tplToName == null)
            {
                return RuntimeItemCatalog.NameFromTpl(tpl) ?? tpl;
            }

            return _tplToName.TryGetValue(tpl ?? string.Empty, out var name)
                ? name
                : RuntimeItemCatalog.NameFromTpl(tpl) ?? tpl;
        }

        public static bool TemplateExists(string tpl)
        {
            if (string.IsNullOrWhiteSpace(tpl))
            {
                return false;
            }

            return (_tplToName != null && _tplToName.ContainsKey(tpl)) || RuntimeItemCatalog.ContainsTpl(tpl);
        }

        public static IEnumerable<SearchCandidate> Search(string query, bool includeUnsafe = false)
        {
            query = (query ?? string.Empty).Trim();
            if (query.Length == 0)
            {
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var runtimePresetCandidates = GetRuntimePresetCandidates();
            var presetRootTpls = BuildPresetRootTplSet(_presetCandidates, runtimePresetCandidates);

            if (_tplToName != null)
            {
                foreach (var kv in _tplToName
                    .Where(kv => includeUnsafe || !IsUnsafeItem(kv.Key))
                    .Where(kv => !ShouldHideRawTemplateResult(kv.Key, query, presetRootTpls))
                    .Where(kv => MatchesText(kv.Key, query) || MatchesText(kv.Value, query))
                    .OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var candidate = new SearchCandidate
                    {
                        Tpl = kv.Key,
                        DisplayName = DisplayNameFromTpl(kv.Key, includeSafetyBadges: includeUnsafe),
                        IsPreset = false,
                        TemplateItem = new LootItem
                        {
                            Tpl = kv.Key
                        }
                    };

                    if (seen.Add(candidate.StableKey))
                    {
                        yield return candidate;
                    }
                }

                foreach (var candidate in (_presetCandidates ?? new List<SearchCandidate>())
                    .Where(candidate => candidate?.TemplateItem != null)
                    .Where(candidate => includeUnsafe || !IsUnsafeItem(candidate.Tpl))
                    .Where(candidate => MatchesPresetCandidate(candidate, query))
                    .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(candidate => candidate.Tpl, StringComparer.OrdinalIgnoreCase))
                {
                    var clone = CloneSearchCandidate(candidate);
                    if (seen.Add(clone.StableKey))
                    {
                        yield return clone;
                    }
                }
            }

            foreach (var candidate in runtimePresetCandidates
                .Where(candidate => candidate?.TemplateItem != null)
                .Where(candidate => includeUnsafe || !IsUnsafeItem(candidate.Tpl))
                .Where(candidate => MatchesPresetCandidate(candidate, query))
                .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Tpl, StringComparer.OrdinalIgnoreCase))
            {
                var clone = CloneSearchCandidate(candidate);
                if (seen.Add(clone.StableKey))
                {
                    yield return clone;
                }
            }

            foreach (var candidate in RuntimeItemCatalog.Search(query, includeUnsafe))
            {
                if (candidate != null &&
                    !ShouldHideRawTemplateResult(candidate.Tpl, query, presetRootTpls) &&
                    seen.Add(candidate.StableKey))
                {
                    yield return candidate;
                }
            }
        }

        public static string TplFromName(string name)
        {
            if (_nameToTpl == null)
            {
                return null;
            }

            return _nameToTpl.TryGetValue(name ?? string.Empty, out var tpl) ? tpl : null;
        }

        public static bool IsQuestItem(string tpl)
        {
            return !string.IsNullOrEmpty(tpl)
                && ((_questItems != null && _questItems.Contains(tpl)) || RuntimeItemCatalog.IsQuestItem(tpl));
        }

        public static bool IsUnsafeItem(string tpl)
        {
            return !string.IsNullOrEmpty(tpl)
                && ((_unsafeItems != null && _unsafeItems.Contains(tpl)) || RuntimeItemCatalog.IsUnsafeItem(tpl));
        }

        public static bool SupportsPresetPreview(string tpl)
        {
            return !string.IsNullOrEmpty(tpl)
                && ((_presetPreviewItems != null && _presetPreviewItems.Contains(tpl)) || RuntimeItemCatalog.SupportsPresetPreview(tpl));
        }

        public static string DisplayNameFromTpl(string tpl, bool includeSafetyBadges = true)
        {
            if (!string.IsNullOrWhiteSpace(tpl) && !TemplateExists(tpl))
            {
                return $"Unknown item [MISSING MOD]: {tpl}";
            }

            var name = NameFromTpl(tpl);
            name = RuntimeItemSourceCatalog.PrefixDisplayName(tpl, name);
            if (!includeSafetyBadges || string.IsNullOrWhiteSpace(tpl))
            {
                return name;
            }

            if (IsQuestItem(tpl))
            {
                return $"{name} [QUEST]";
            }

            if (IsUnsafeItem(tpl))
            {
                return $"{name} [UNSAFE]";
            }

            return name;
        }

        private static List<SearchCandidate> GetRuntimePresetCandidates()
        {
            if (_runtimePresetCandidatesLoaded)
            {
                return _runtimePresetCandidates ?? new List<SearchCandidate>();
            }

            if (DateTime.UtcNow < _nextRuntimePresetRetryUtc)
            {
                return _runtimePresetCandidates ?? new List<SearchCandidate>();
            }

            try
            {
                var json = RequestHandler.GetJson(RuntimeItemPresetsRoute);
                if (string.IsNullOrWhiteSpace(json) ||
                    string.Equals(json.Trim(), "null", StringComparison.OrdinalIgnoreCase))
                {
                    _runtimePresetCandidatesLoaded = true;
                    _runtimePresetCandidates = new List<SearchCandidate>();
                    return _runtimePresetCandidates;
                }

                var trimmed = json.TrimStart();
                if (trimmed.StartsWith("{", StringComparison.Ordinal))
                {
                    var obj = JObject.Parse(json);
                    var errorToken = GetPropertyOrDefault(obj, "error") ?? GetPropertyOrDefault(obj, "err");
                    if (errorToken != null && errorToken.Type != JTokenType.Null)
                    {
                        MarkRuntimePresetRetry();
                        return _runtimePresetCandidates ?? new List<SearchCandidate>();
                    }
                }

                var presets = JsonConvert.DeserializeObject<Dictionary<string, PresetTemplate>>(json)
                              ?? new Dictionary<string, PresetTemplate>(StringComparer.OrdinalIgnoreCase);
                var candidates = BuildPresetCandidates(
                    presets,
                    _localeNames ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    null);

                _runtimePresetCandidates = candidates;
                _runtimePresetCandidatesLoaded = candidates.Count > 0 || presets.Count == 0;
                if (!_runtimePresetCandidatesLoaded)
                {
                    MarkRuntimePresetRetry();
                }

                return _runtimePresetCandidates;
            }
            catch
            {
                MarkRuntimePresetRetry();
                return _runtimePresetCandidates ?? new List<SearchCandidate>();
            }
        }

        private static void MarkRuntimePresetRetry()
        {
            _runtimePresetCandidatesLoaded = false;
            _nextRuntimePresetRetryUtc = DateTime.UtcNow.AddSeconds(5);
        }

        private static JToken GetPropertyOrDefault(JObject obj, string name)
        {
            if (obj == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            return obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var value)
                ? value
                : null;
        }

        private static HashSet<string> BuildPresetRootTplSet(params IEnumerable<SearchCandidate>[] candidateSources)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (candidateSources == null)
            {
                return result;
            }

            foreach (var source in candidateSources)
            {
                if (source == null)
                {
                    continue;
                }

                foreach (var candidate in source)
                {
                    if (!string.IsNullOrWhiteSpace(candidate?.Tpl))
                    {
                        result.Add(candidate.Tpl);
                    }
                }
            }

            return result;
        }

        private static bool ShouldHideRawTemplateResult(string tpl, string query, HashSet<string> presetRootTpls)
        {
            if (string.IsNullOrWhiteSpace(tpl) || presetRootTpls == null || !presetRootTpls.Contains(tpl))
            {
                return false;
            }

            return !string.Equals(tpl, query?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static List<SearchCandidate> BuildPresetCandidates(
            IDictionary<string, PresetTemplate> presets,
            IDictionary<string, string> localeNames,
            IDictionary<string, ItemTemplate> templates)
        {
            var candidates = new List<SearchCandidate>();
            if (presets == null)
            {
                return candidates;
            }

            foreach (var pair in presets)
            {
                var preset = pair.Value;
                if (preset?.Items == null || preset.Items.Count == 0)
                {
                    continue;
                }

                var rootItem = BuildPresetRoot(pair.Key, preset, localeNames);
                if (rootItem == null || string.IsNullOrWhiteSpace(rootItem.Tpl))
                {
                    continue;
                }

                if (!TemplateExists(rootItem.Tpl))
                {
                    continue;
                }

                var presetLabel = ResolvePresetLabel(pair.Key, preset, localeNames);
                var displayName = BuildPresetDisplayName(presetLabel, rootItem.Tpl, rootItem);
                if (ContainsDoNotUseMarker(displayName))
                {
                    continue;
                }

                candidates.Add(new SearchCandidate
                {
                    Tpl = rootItem.Tpl,
                    DisplayName = displayName,
                    IsPreset = true,
                    TemplateItem = rootItem
                });
            }

            return candidates;
        }

        private static bool SupportsPresetPreview(string tpl, IDictionary<string, ItemTemplate> templates)
        {
            if (string.IsNullOrWhiteSpace(tpl) || templates == null || !templates.TryGetValue(tpl, out var template))
            {
                return false;
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (template != null && visited.Add(tpl))
            {
                var internalName = template.InternalName ?? string.Empty;
                if (WeaponPreviewInternalNames.Contains(internalName) || GearPreviewInternalNames.Contains(internalName))
                {
                    return true;
                }

                tpl = template.ParentId;
                if (string.IsNullOrWhiteSpace(tpl) || !templates.TryGetValue(tpl, out template))
                {
                    break;
                }
            }

            return false;
        }

        private static LootItem BuildPresetRoot(string presetId, PresetTemplate preset, IDictionary<string, string> localeNames)
        {
            var records = new List<PresetRecord>();
            var byId = new Dictionary<string, PresetRecord>(StringComparer.Ordinal);

            foreach (var item in preset.Items ?? new List<PresetTemplateItem>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Tpl))
                {
                    continue;
                }

                var record = new PresetRecord
                {
                    Id = item.Id ?? string.Empty,
                    ParentId = item.ParentId ?? string.Empty,
                    Tpl = item.Tpl,
                    SlotId = item.SlotId ?? string.Empty,
                    LocationJson = item.Location?.ToString(Formatting.None),
                    UpdJson = item.Upd?.ToString(Formatting.None),
                    StackCount = ExtractStackCount(item.Upd)
                };

                records.Add(record);
                if (!string.IsNullOrWhiteSpace(record.Id) && !byId.ContainsKey(record.Id))
                {
                    byId[record.Id] = record;
                }
            }

            PresetRecord root = null;
            foreach (var record in records)
            {
                if (!string.IsNullOrWhiteSpace(record.ParentId) && byId.TryGetValue(record.ParentId, out var parent))
                {
                    parent.Children.Add(record);
                    continue;
                }

                root ??= record;
            }

            if (root == null)
            {
                return null;
            }

            var rootItem = new LootItem
            {
                Tpl = root.Tpl,
                PresetId = string.IsNullOrWhiteSpace(preset.Id) ? presetId : preset.Id,
                PresetName = ResolvePresetLabel(string.IsNullOrWhiteSpace(preset.Id) ? presetId : preset.Id, preset, localeNames),
                UpdJson = root.UpdJson,
                StackMin = root.StackCount,
                StackMax = root.StackCount,
                Children = root.Children.Select(BuildPresetChild).ToList()
            };

            NormalizeLootItemTree(rootItem);
            return rootItem;
        }

        private static LootItemNode BuildPresetChild(PresetRecord record)
        {
            return new LootItemNode
            {
                Tpl = record.Tpl,
                SlotId = record.SlotId,
                LocationJson = record.LocationJson,
                UpdJson = record.UpdJson,
                StackMin = record.StackCount,
                StackMax = record.StackCount,
                Children = record.Children.Select(BuildPresetChild).ToList()
            };
        }

        private static int? ExtractStackCount(JToken updToken)
        {
            if (updToken == null || updToken.Type != JTokenType.Object)
            {
                return null;
            }

            var stackToken = updToken["StackObjectsCount"];
            if (stackToken == null)
            {
                return null;
            }

            var value = stackToken.Value<int?>();
            return value.HasValue && value.Value > 0 ? value.Value : null;
        }

        private static string BuildPresetDisplayName(string presetName, string rootTpl, LootItem rootItem)
        {
            var baseName = NameFromTpl(rootTpl);
            var label = string.IsNullOrWhiteSpace(presetName)
                ? baseName
                : HumanizePresetName(presetName);

            if (string.Equals(label, baseName, StringComparison.OrdinalIgnoreCase))
            {
                label = $"{baseName} [PRESET]";
            }
            else
            {
                label = ContainsBaseName(label, baseName)
                    ? $"{label} [PRESET]"
                    : $"{baseName} {label} [PRESET]";
            }

            var attachmentCount = CountChildNodes(rootItem?.Children);
            if (attachmentCount > 0)
            {
                label = $"{label} [Parts: {attachmentCount}]";
            }

            return RuntimeItemSourceCatalog.PrefixDisplayName(rootTpl, label);
        }

        private static string ResolvePresetLabel(string presetId, PresetTemplate preset, IDictionary<string, string> localeNames)
        {
            if (!string.IsNullOrWhiteSpace(presetId) &&
                localeNames != null &&
                localeNames.TryGetValue(presetId, out var localizedName) &&
                !string.IsNullOrWhiteSpace(localizedName))
            {
                return localizedName.Trim();
            }

            return HumanizePresetName(preset?.Name);
        }

        private static string HumanizePresetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var result = value.Trim();
            result = result.Replace('_', ' ');
            result = Regex.Replace(result, @"\s+", " ").Trim();
            return result;
        }

        private static bool ContainsBaseName(string label, string baseName)
        {
            return !string.IsNullOrWhiteSpace(label)
                   && !string.IsNullOrWhiteSpace(baseName)
                   && label.IndexOf(baseName, StringComparison.OrdinalIgnoreCase) >= 0;
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

        private static SearchCandidate CloneSearchCandidate(SearchCandidate candidate)
        {
            return new SearchCandidate
            {
                Tpl = candidate.Tpl,
                DisplayName = RuntimeItemSourceCatalog.PrefixDisplayName(candidate.Tpl, candidate.DisplayName),
                IsPreset = candidate.IsPreset,
                TemplateItem = candidate.TemplateItem?.Clone()
            };
        }

        private static bool MatchesPresetCandidate(SearchCandidate candidate, string query)
        {
            if (candidate == null)
            {
                return false;
            }

            return MatchesText(candidate.DisplayName, query)
                   || MatchesText(candidate.Tpl, query)
                   || MatchesText(NameFromTpl(candidate.Tpl), query)
                   || MatchesText(RuntimeItemSourceCatalog.SourceFromTpl(candidate.Tpl), query)
                   || MatchesText(candidate.TemplateItem?.PresetName, query);
        }

        private static bool MatchesText(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value)
                   && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void NormalizeLootItemTree(LootItem item)
        {
            if (item == null)
            {
                return;
            }

            if (item.Children == null)
            {
                item.Children = new List<LootItemNode>();
            }

            NormalizeChildNodes(item.Children);
        }

        private static void NormalizeChildNodes(List<LootItemNode> nodes)
        {
            if (nodes == null)
            {
                return;
            }

            foreach (var node in nodes)
            {
                if (node == null)
                {
                    continue;
                }

                if (node.Children == null)
                {
                    node.Children = new List<LootItemNode>();
                }

                NormalizeChildNodes(node.Children);
            }
        }

        private static bool IsUnsafeForSearch(
            string tpl,
            ItemTemplate template,
            string localizedName,
            IDictionary<string, string> localeNames,
            IDictionary<string, ItemTemplate> templates)
        {
            if (template == null)
            {
                return true;
            }

            if (!string.Equals(template.Type, "Item", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var props = template.Props;
            if (props == null)
            {
                return true;
            }

            var internalName = template.InternalName ?? string.Empty;
            if (AbstractInternalNames.Contains(internalName))
            {
                return true;
            }

            if (IsBuiltInInsertTemplate(tpl, template, templates))
            {
                return true;
            }

            if (string.Equals(localizedName, "Item", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var localizedShortName = TryGetLocaleValue(localeNames, tpl, "ShortName");
            if (IsAirdropSupplyContainer(internalName, localizedName, localizedShortName))
            {
                return true;
            }

            var localizedDescription = TryGetLocaleValue(localeNames, tpl, "Description");

            if (ContainsDoNotUseMarker(localizedName) ||
                ContainsDoNotUseMarker(localizedShortName) ||
                ContainsDoNotUseMarker(localizedDescription) ||
                ContainsDoNotUseMarker(internalName))
            {
                return true;
            }

            return false;
        }

        private static bool IsAirdropSupplyContainer(string internalName, string localizedName, string localizedShortName)
        {
            var normalizedInternal = (internalName ?? string.Empty).Trim();
            if (normalizedInternal.IndexOf("event_container_airdrop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalizedInternal.IndexOf("air_drop_supply", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return LooksLikeAirdropSupplyContainerName(localizedName) ||
                   LooksLikeAirdropSupplyContainerName(localizedShortName);
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

        private static bool IsBuiltInInsertTemplate(string tpl, ItemTemplate template, IDictionary<string, ItemTemplate> templates)
        {
            if (template == null)
            {
                return false;
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentTpl = tpl;
            var current = template;
            while (current != null && visited.Add(currentTpl ?? string.Empty))
            {
                if (string.Equals(current.InternalName, "BuiltInInserts", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                currentTpl = current.ParentId;
                if (string.IsNullOrWhiteSpace(currentTpl) || templates == null || !templates.TryGetValue(currentTpl, out current))
                {
                    break;
                }
            }

            return false;
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

        private static string TryGetLocaleValue(IDictionary<string, string> localeNames, string tpl, string suffix)
        {
            if (localeNames == null || string.IsNullOrWhiteSpace(tpl) || string.IsNullOrWhiteSpace(suffix))
            {
                return string.Empty;
            }

            return localeNames.TryGetValue($"{tpl} {suffix}", out var value)
                ? value ?? string.Empty
                : string.Empty;
        }
    }
}
#endregion
