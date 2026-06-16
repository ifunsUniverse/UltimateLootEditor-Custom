using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ULE.SpawnEditor
{
    internal static class LootAmmoAutoFill
    {
        private const string MagazineBaseInternalName = "Magazine";
        private const string CartridgesSlotId = "cartridges";

        private static readonly object Sync = new object();
        private static readonly Random Rng = new Random();
        private static Dictionary<string, ItemTemplate> _templates;
        private static Dictionary<string, List<string>> _compatibleAmmoByMagazine;
        private static List<string> _ammoTemplates;
        private static bool _loadAttempted;

        public static void FillMagazineAmmo(IEnumerable<LootItem> items)
        {
            if (items == null)
            {
                return;
            }

            EnsureLoaded();
            if (!HasTemplateData)
            {
                return;
            }

            foreach (var item in items)
            {
                FillMagazineAmmo(item);
            }
        }

        public static void FillMagazineAmmo(LootItem item)
        {
            if (item == null)
            {
                return;
            }

            EnsureLoaded();
            if (!HasTemplateData)
            {
                return;
            }

            FillNode(item, GetDefaultAmmoForWeapon(item.Tpl));
        }

        private static void FillNode(LootItemNode node, string inheritedWeaponDefaultAmmo)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Tpl))
            {
                return;
            }

            var nodeDefaultAmmo = GetDefaultAmmoForWeapon(node.Tpl);
            var defaultAmmo = string.IsNullOrWhiteSpace(nodeDefaultAmmo)
                ? inheritedWeaponDefaultAmmo
                : nodeDefaultAmmo;

            RemoveChamberAmmoChildren(node);

            if (IsMagazine(node.Tpl))
            {
                FillMagazine(node, defaultAmmo);
            }

            if (node.Children == null)
            {
                node.Children = new List<LootItemNode>();
            }

            foreach (var child in node.Children.ToArray())
            {
                FillNode(child, defaultAmmo);
            }
        }

        private static void FillMagazine(LootItemNode magazine, string weaponDefaultAmmo)
        {
            if (magazine == null)
            {
                return;
            }

            var template = GetTemplate(magazine.Tpl);
            var capacity = GetMagazineCapacity(template);
            if (capacity <= 0)
            {
                return;
            }

            var ammoTpl = ChooseAmmoForMagazine(template, weaponDefaultAmmo);
            if (string.IsNullOrWhiteSpace(ammoTpl))
            {
                return;
            }

            if (magazine.Children == null)
            {
                magazine.Children = new List<LootItemNode>();
            }

            magazine.Children.RemoveAll(child =>
                child != null &&
                string.Equals(child.SlotId, CartridgesSlotId, StringComparison.OrdinalIgnoreCase) &&
                IsAmmo(child.Tpl));

            var roundCount = NextInclusive(1, capacity);
            magazine.Children.Add(new LootItemNode
            {
                Tpl = ammoTpl,
                SlotId = CartridgesSlotId,
                StackMin = roundCount,
                StackMax = roundCount,
                UpdJson = BuildStackUpdJson(roundCount),
                Children = new List<LootItemNode>()
            });
        }

        private static void RemoveChamberAmmoChildren(LootItemNode node)
        {
            if (node?.Children == null)
            {
                return;
            }

            node.Children.RemoveAll(IsChamberAmmoNode);
        }

        private static bool IsChamberAmmoNode(LootItemNode node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.SlotId) || !IsAmmo(node.Tpl))
            {
                return false;
            }

            return node.SlotId.StartsWith("patron_in_weapon", StringComparison.OrdinalIgnoreCase) ||
                   node.SlotId.StartsWith("camora", StringComparison.OrdinalIgnoreCase);
        }

        private static string ChooseAmmoForMagazine(ItemTemplate magazineTemplate, string weaponDefaultAmmo)
        {
            if (!string.IsNullOrWhiteSpace(weaponDefaultAmmo) &&
                IsAmmo(weaponDefaultAmmo) &&
                MagazineAllowsAmmo(magazineTemplate, weaponDefaultAmmo))
            {
                return weaponDefaultAmmo;
            }

            var compatible = GetCompatibleAmmo(magazineTemplate);
            if (compatible.Count == 0)
            {
                return string.Empty;
            }

            return compatible[NextInclusive(0, compatible.Count - 1)];
        }

        private static List<string> GetCompatibleAmmo(ItemTemplate magazineTemplate)
        {
            if (magazineTemplate == null || string.IsNullOrWhiteSpace(magazineTemplate.Id))
            {
                return new List<string>();
            }

            lock (Sync)
            {
                if (_compatibleAmmoByMagazine.TryGetValue(magazineTemplate.Id, out var cached))
                {
                    return cached;
                }

                var compatible = _ammoTemplates
                    .Where(ammoTpl => MagazineAllowsAmmo(magazineTemplate, ammoTpl))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(tpl => tpl, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _compatibleAmmoByMagazine[magazineTemplate.Id] = compatible;
                return compatible;
            }
        }

        private static bool MagazineAllowsAmmo(ItemTemplate magazineTemplate, string ammoTpl)
        {
            if (magazineTemplate?.Props?.Cartridges == null || string.IsNullOrWhiteSpace(ammoTpl))
            {
                return false;
            }

            foreach (var cartridgeSlot in magazineTemplate.Props.Cartridges)
            {
                if (cartridgeSlot?.Props?.Filters == null)
                {
                    continue;
                }

                foreach (var filter in cartridgeSlot.Props.Filters)
                {
                    if (filter?.Filter == null)
                    {
                        continue;
                    }

                    foreach (var filterTpl in filter.Filter)
                    {
                        if (TemplateMatchesFilter(ammoTpl, filterTpl))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool TemplateMatchesFilter(string tpl, string filterTpl)
        {
            if (string.IsNullOrWhiteSpace(tpl) || string.IsNullOrWhiteSpace(filterTpl))
            {
                return false;
            }

            if (string.Equals(tpl, filterTpl, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var template = GetTemplate(tpl);
            while (template != null && visited.Add(template.Id))
            {
                if (string.Equals(template.ParentId, filterTpl, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(template.ParentId))
                {
                    break;
                }

                template = GetTemplate(template.ParentId);
            }

            return false;
        }

        private static string GetDefaultAmmoForWeapon(string tpl)
        {
            var template = GetTemplate(tpl);
            var defaultAmmo = template?.Props?.DefaultAmmo;
            return IsAmmo(defaultAmmo) ? defaultAmmo : string.Empty;
        }

        private static int GetMagazineCapacity(ItemTemplate template)
        {
            if (template?.Props?.Cartridges == null)
            {
                return 0;
            }

            return template.Props.Cartridges
                .Where(slot => slot != null)
                .Select(slot => slot.MaxCount)
                .DefaultIfEmpty(0)
                .Max();
        }

        private static bool IsMagazine(string tpl)
        {
            var template = GetTemplate(tpl);
            return template != null &&
                   GetMagazineCapacity(template) > 0 &&
                   InheritsFromInternalName(template, MagazineBaseInternalName);
        }

        private static bool IsAmmo(string tpl)
        {
            var template = GetTemplate(tpl);
            return template?.Props != null &&
                   !string.IsNullOrWhiteSpace(template.Props.Caliber);
        }

        private static bool InheritsFromInternalName(ItemTemplate template, string internalName)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (template != null && visited.Add(template.Id))
            {
                if (string.Equals(template.InternalName, internalName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(template.ParentId))
                {
                    break;
                }

                template = GetTemplate(template.ParentId);
            }

            return false;
        }

        private static ItemTemplate GetTemplate(string tpl)
        {
            if (string.IsNullOrWhiteSpace(tpl))
            {
                return null;
            }

            lock (Sync)
            {
                return _templates != null && _templates.TryGetValue(tpl, out var template)
                    ? template
                    : null;
            }
        }

        private static bool HasTemplateData
        {
            get
            {
                lock (Sync)
                {
                    return _templates != null && _templates.Count > 0;
                }
            }
        }

        private static void EnsureLoaded()
        {
            lock (Sync)
            {
                if (_loadAttempted)
                {
                    return;
                }

                _loadAttempted = true;
                _templates = new Dictionary<string, ItemTemplate>(StringComparer.OrdinalIgnoreCase);
                _compatibleAmmoByMagazine = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                _ammoTemplates = new List<string>();

                var itemsPath = DbPaths.ResolveItemsPath();
                if (string.IsNullOrWhiteSpace(itemsPath) || !File.Exists(itemsPath))
                {
                    return;
                }

                try
                {
                    var raw = File.ReadAllText(itemsPath);
                    var loaded = JsonConvert.DeserializeObject<Dictionary<string, ItemTemplate>>(raw)
                                 ?? new Dictionary<string, ItemTemplate>();
                    _templates = new Dictionary<string, ItemTemplate>(loaded, StringComparer.OrdinalIgnoreCase);

                    foreach (var pair in _templates)
                    {
                        if (pair.Value != null && string.IsNullOrWhiteSpace(pair.Value.Id))
                        {
                            pair.Value.Id = pair.Key;
                        }
                    }

                    _ammoTemplates = _templates
                        .Where(pair => IsAmmoTemplate(pair.Value))
                        .Select(pair => pair.Key)
                        .OrderBy(tpl => tpl, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch
                {
                    _templates.Clear();
                    _ammoTemplates.Clear();
                    _compatibleAmmoByMagazine.Clear();
                }
            }
        }

        private static bool IsAmmoTemplate(ItemTemplate template)
        {
            return template?.Props != null &&
                   !string.IsNullOrWhiteSpace(template.Props.Caliber);
        }

        private static int NextInclusive(int min, int max)
        {
            if (max <= min)
            {
                return min;
            }

            lock (Rng)
            {
                return Rng.Next(min, max + 1);
            }
        }

        private static string BuildStackUpdJson(int stackCount)
        {
            return new JObject
            {
                ["StackObjectsCount"] = Math.Max(1, stackCount)
            }.ToString(Formatting.None);
        }

        private sealed class ItemTemplate
        {
            [JsonProperty("_id")]
            public string Id { get; set; }

            [JsonProperty("_name")]
            public string InternalName { get; set; }

            [JsonProperty("_parent")]
            public string ParentId { get; set; }

            [JsonProperty("_props")]
            public ItemTemplateProps Props { get; set; }
        }

        private sealed class ItemTemplateProps
        {
            [JsonProperty("Cartridges")]
            public List<CartridgeSlot> Cartridges { get; set; }

            [JsonProperty("Caliber")]
            public string Caliber { get; set; }

            [JsonProperty("defAmmo")]
            public string DefaultAmmo { get; set; }
        }

        private sealed class CartridgeSlot
        {
            [JsonProperty("_max_count")]
            public int MaxCount { get; set; }

            [JsonProperty("_props")]
            public SlotProps Props { get; set; }
        }

        private sealed class SlotProps
        {
            [JsonProperty("filters")]
            public List<SlotFilter> Filters { get; set; }
        }

        private sealed class SlotFilter
        {
            [JsonProperty("Filter")]
            public List<string> Filter { get; set; }
        }
    }
}
