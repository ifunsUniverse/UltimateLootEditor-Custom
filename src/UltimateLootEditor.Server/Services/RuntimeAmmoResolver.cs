using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using UltimateLootEditor.Util;

namespace UltimateLootEditor.Services;

public static class RuntimeAmmoResolver
{
    private const string MagazineBaseInternalName = "Magazine";
    private const string CartridgesSlotId = "cartridges";

    private static readonly object Sync = new();
    private static readonly Random Rng = new();
    private static readonly Dictionary<string, List<string>> CompatibleAmmoByMagazine = new(StringComparer.OrdinalIgnoreCase);

    private static TemplateTable? _templateTable;
    private static Dictionary<string, TemplateItem> _templates = new(StringComparer.OrdinalIgnoreCase);
    private static List<string> _ammoTemplates = [];
    private static DateTime _lastRefreshUtc = DateTime.MinValue;
    private static bool _initialized;

    public static void Initialize(TemplateTable templateTable)
    {
        if (templateTable == null)
        {
            return;
        }

        lock (Sync)
        {
            _templateTable = templateTable;
            RebuildLocked(templateTable, "loaded");
        }
    }

    public static void RepairGeneratedAmmo(List<Item> items, string? rootTpl)
    {
        if (items == null || items.Count == 0)
        {
            return;
        }

        lock (Sync)
        {
            RefreshIfNeededLocked();
            if (!_initialized || _templates.Count == 0)
            {
                return;
            }

            RemoveChamberAmmoChildrenLocked(items);

            var byId = items
                .Where(item => item != null)
                .GroupBy(item => item.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var rootDefaultAmmo = GetDefaultAmmoForWeaponLocked(rootTpl);
            var magazines = items
                .Where(item => item != null && IsMagazineLocked(item.Template.ToString()))
                .ToArray();

            foreach (var magazine in magazines)
            {
                var magazineId = magazine.Id.ToString();
                if (string.IsNullOrWhiteSpace(magazineId))
                {
                    continue;
                }

                items.RemoveAll(child =>
                    child != null &&
                    string.Equals(child.ParentId, magazineId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(child.SlotId, CartridgesSlotId, StringComparison.OrdinalIgnoreCase) &&
                    IsAmmoLocked(child.Template.ToString()));

                var magazineTemplate = GetTemplateLocked(magazine.Template.ToString());
                var capacity = GetMagazineCapacityLocked(magazineTemplate);
                if (capacity <= 0)
                {
                    continue;
                }

                var inheritedAmmo = FindInheritedWeaponDefaultAmmoLocked(magazine, byId, rootDefaultAmmo);
                var ammoTpl = ChooseAmmoForMagazineLocked(magazineTemplate, inheritedAmmo);
                if (string.IsNullOrWhiteSpace(ammoTpl))
                {
                    continue;
                }

                var roundCount = NextInclusive(1, capacity);
                items.Add(new Item
                {
                    Id = new MongoId(),
                    Template = new MongoId(ammoTpl),
                    ParentId = magazineId,
                    SlotId = CartridgesSlotId,
                    Upd = new Upd
                    {
                        StackObjectsCount = roundCount
                    }
                });
            }
        }
    }

    private static void RefreshIfNeededLocked()
    {
        if (_templateTable == null)
        {
            return;
        }

        if (!_initialized || _templates.Count == 0)
        {
            RebuildLocked(_templateTable, "loaded");
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - _lastRefreshUtc).TotalSeconds < 1d)
        {
            return;
        }

        try
        {
            if (_templateTable.Items.Count != _templates.Count)
            {
                RebuildLocked(_templateTable, "refreshed");
                return;
            }

            _lastRefreshUtc = now;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[ULE] Runtime ammo resolver could not refresh template data: {ex.Message}");
            _lastRefreshUtc = now;
        }
    }

    private static void RebuildLocked(TemplateTable templateTable, string verb)
    {
        try
        {
            _templates = templateTable
                .Items
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key.ToString()) && pair.Value != null)
                .ToDictionary(pair => pair.Key.ToString(), pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            _ammoTemplates = _templates
                .Where(pair => IsAmmoTemplate(pair.Value))
                .Select(pair => pair.Key)
                .OrderBy(tpl => tpl, StringComparer.OrdinalIgnoreCase)
                .ToList();

            CompatibleAmmoByMagazine.Clear();
            _initialized = true;
            _lastRefreshUtc = DateTime.UtcNow;
            Logger.Info($"[ULE] Runtime ammo resolver {verb} {_templates.Count} templates and {_ammoTemplates.Count} ammo types.");
        }
        catch (Exception ex)
        {
            _templates = new Dictionary<string, TemplateItem>(StringComparer.OrdinalIgnoreCase);
            _ammoTemplates = [];
            CompatibleAmmoByMagazine.Clear();
            _initialized = false;
            _lastRefreshUtc = DateTime.UtcNow;
            Logger.Warn($"[ULE] Runtime ammo resolver could not be built: {ex.Message}");
        }
    }

    private static void RemoveChamberAmmoChildrenLocked(List<Item> items)
    {
        items.RemoveAll(item =>
            item != null &&
            IsAmmoLocked(item.Template.ToString()) &&
            IsChamberSlot(item.SlotId));
    }

    private static bool IsChamberSlot(string? slotId)
    {
        return !string.IsNullOrWhiteSpace(slotId) &&
               (slotId.StartsWith("patron_in_weapon", StringComparison.OrdinalIgnoreCase) ||
                slotId.StartsWith("camora", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindInheritedWeaponDefaultAmmoLocked(
        Item magazine,
        IReadOnlyDictionary<string, Item> byId,
        string fallbackAmmo)
    {
        var parentId = magazine.ParentId;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (!string.IsNullOrWhiteSpace(parentId) && visited.Add(parentId))
        {
            if (!byId.TryGetValue(parentId, out var parent))
            {
                break;
            }

            var defaultAmmo = GetDefaultAmmoForWeaponLocked(parent.Template.ToString());
            if (!string.IsNullOrWhiteSpace(defaultAmmo))
            {
                return defaultAmmo;
            }

            parentId = parent.ParentId;
        }

        return fallbackAmmo;
    }

    private static string GetDefaultAmmoForWeaponLocked(string? tpl)
    {
        var template = GetTemplateLocked(tpl);
        var defaultAmmo = template?.Properties?.DefAmmo?.ToString();
        return IsAmmoLocked(defaultAmmo) ? defaultAmmo ?? string.Empty : string.Empty;
    }

    private static string ChooseAmmoForMagazineLocked(TemplateItem? magazineTemplate, string weaponDefaultAmmo)
    {
        if (magazineTemplate == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(weaponDefaultAmmo) &&
            IsAmmoLocked(weaponDefaultAmmo) &&
            (MagazineAllowsAmmoLocked(magazineTemplate, weaponDefaultAmmo) ||
             !MagazineHasAmmoFiltersLocked(magazineTemplate)))
        {
            return weaponDefaultAmmo;
        }

        var compatible = GetCompatibleAmmoLocked(magazineTemplate);
        return compatible.Count == 0
            ? string.Empty
            : compatible[NextInclusive(0, compatible.Count - 1)];
    }

    private static List<string> GetCompatibleAmmoLocked(TemplateItem magazineTemplate)
    {
        var magazineTpl = magazineTemplate.Id.ToString();
        if (string.IsNullOrWhiteSpace(magazineTpl))
        {
            return [];
        }

        if (CompatibleAmmoByMagazine.TryGetValue(magazineTpl, out var cached))
        {
            return cached;
        }

        var compatible = _ammoTemplates
            .Where(ammoTpl => MagazineAllowsAmmoLocked(magazineTemplate, ammoTpl))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tpl => tpl, StringComparer.OrdinalIgnoreCase)
            .ToList();

        CompatibleAmmoByMagazine[magazineTpl] = compatible;
        return compatible;
    }

    private static bool MagazineHasAmmoFiltersLocked(TemplateItem magazineTemplate)
    {
        var cartridges = magazineTemplate.Properties?.Cartridges;
        if (cartridges == null)
        {
            return false;
        }

        foreach (var cartridgeSlot in cartridges)
        {
            var filters = cartridgeSlot?.Properties?.Filters;
            if (filters == null)
            {
                continue;
            }

            foreach (var filter in filters)
            {
                if (filter?.Filter?.Count > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool MagazineAllowsAmmoLocked(TemplateItem? magazineTemplate, string? ammoTpl)
    {
        if (magazineTemplate == null || string.IsNullOrWhiteSpace(ammoTpl))
        {
            return false;
        }

        var cartridges = magazineTemplate.Properties?.Cartridges;
        if (cartridges != null)
        {
            foreach (var cartridgeSlot in cartridges)
            {
                var filters = cartridgeSlot?.Properties?.Filters;
                if (filters == null)
                {
                    continue;
                }

                foreach (var filter in filters)
                {
                    var filterTemplates = filter?.Filter;
                    if (filterTemplates == null)
                    {
                        continue;
                    }

                    foreach (var filterTpl in filterTemplates)
                    {
                        if (TemplateMatchesFilterLocked(ammoTpl, filterTpl.ToString()))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        var magazineCaliber = magazineTemplate.Properties?.AmmoCaliber;
        var ammoCaliber = GetTemplateLocked(ammoTpl)?.Properties?.Caliber;
        return !string.IsNullOrWhiteSpace(magazineCaliber) &&
               string.Equals(magazineCaliber, ammoCaliber, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TemplateMatchesFilterLocked(string? tpl, string? filterTpl)
    {
        if (string.IsNullOrWhiteSpace(tpl) || string.IsNullOrWhiteSpace(filterTpl))
        {
            return false;
        }

        if (string.Equals(tpl, filterTpl, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var template = GetTemplateLocked(tpl);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (template != null)
        {
            var templateId = template.Id.ToString();
            if (string.IsNullOrWhiteSpace(templateId) || !visited.Add(templateId))
            {
                break;
            }

            var parentId = template.Parent.ToString();
            if (string.Equals(parentId, filterTpl, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(parentId))
            {
                break;
            }

            template = GetTemplateLocked(parentId);
        }

        return false;
    }

    private static int GetMagazineCapacityLocked(TemplateItem? template)
    {
        if (template?.Properties?.Cartridges == null)
        {
            return 0;
        }

        return template.Properties.Cartridges
            .Where(slot => slot != null)
            .Select(slot => Convert.ToInt32(Math.Floor(slot.MaxCount.GetValueOrDefault())))
            .DefaultIfEmpty(0)
            .Max();
    }

    private static bool IsMagazineLocked(string? tpl)
    {
        var template = GetTemplateLocked(tpl);
        return template != null &&
               GetMagazineCapacityLocked(template) > 0 &&
               InheritsFromInternalNameLocked(template, MagazineBaseInternalName);
    }

    private static bool IsAmmoLocked(string? tpl)
    {
        var template = GetTemplateLocked(tpl);
        return IsAmmoTemplate(template);
    }

    private static bool IsAmmoTemplate(TemplateItem? template)
    {
        return template?.Properties != null &&
               !string.IsNullOrWhiteSpace(template.Properties.Caliber);
    }

    private static bool InheritsFromInternalNameLocked(TemplateItem rootTemplate, string internalName)
    {
        TemplateItem? template = rootTemplate;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (template != null)
        {
            var templateId = template.Id.ToString();
            if (string.IsNullOrWhiteSpace(templateId) || !visited.Add(templateId))
            {
                break;
            }

            if (string.Equals(template.Name, internalName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var parentId = template.Parent.ToString();
            if (string.IsNullOrWhiteSpace(parentId))
            {
                break;
            }

            template = GetTemplateLocked(parentId);
        }

        return false;
    }

    private static TemplateItem? GetTemplateLocked(string? tpl)
    {
        return !string.IsNullOrWhiteSpace(tpl) && _templates.TryGetValue(tpl, out var template)
            ? template
            : null;
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
}
