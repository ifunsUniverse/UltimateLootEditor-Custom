using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using SPTarkov.Server.Core.Services;
using UltimateLootEditor.Shared;
using UltimateLootEditor.Util;

namespace UltimateLootEditor.Services;

public static class RuntimeTemplateIndex
{
    private static readonly object Sync = new();
    private static readonly HashSet<string> Templates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string>? _sourceByTpl;
    private static DatabaseService? _databaseService;
    private static DateTime _lastRefreshUtc = DateTime.MinValue;
    private static bool _initialized;

    public static bool HasTemplateData
    {
        get
        {
            lock (Sync)
            {
                return Templates.Count > 0;
            }
        }
    }

    public static void Initialize(DatabaseService databaseService)
    {
        if (databaseService == null)
        {
            return;
        }

        lock (Sync)
        {
            _databaseService = databaseService;
            RebuildLocked(databaseService, "loaded");
        }
    }

    public static bool ContainsTemplate(string tpl)
    {
        if (string.IsNullOrWhiteSpace(tpl))
        {
            return false;
        }

        lock (Sync)
        {
            if (!_initialized || Templates.Count == 0 || Templates.Contains(tpl))
            {
                return true;
            }

            // Some item injectors finish after our OnLoad priority. On a miss, take one
            // fresh look at the final runtime database before treating the edit as broken.
            RefreshIfChangedOrThrottledLocked();
            return !_initialized || Templates.Count == 0 || Templates.Contains(tpl);
        }
    }

    public static string DescribeTemplate(string tpl, string fallbackName = null)
    {
        if (string.IsNullOrWhiteSpace(tpl))
        {
            return "unknown item";
        }

        lock (Sync)
        {
            if (DisplayNames.TryGetValue(tpl, out var displayName) && !string.IsNullOrWhiteSpace(displayName))
            {
                var sourceName = GetSourceNameLocked(tpl);
                return string.IsNullOrWhiteSpace(sourceName)
                    ? displayName
                    : $"[{sourceName}] {displayName}";
            }
        }

        return string.IsNullOrWhiteSpace(fallbackName) ? "unknown item" : fallbackName.Trim();
    }

    public static IReadOnlyDictionary<string, string> GetRuntimeItemSources(DatabaseService databaseService)
    {
        lock (Sync)
        {
            if (databaseService != null)
            {
                _databaseService = databaseService;
            }

            RefreshIfChangedOrThrottledLocked(forceCountCheck: true);
            _sourceByTpl ??= BuildRuntimeItemSourceMap();
            return new Dictionary<string, string>(_sourceByTpl, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string GetSourceNameLocked(string tpl)
    {
        RefreshIfChangedOrThrottledLocked(forceCountCheck: true);
        _sourceByTpl ??= BuildRuntimeItemSourceMap();
        return _sourceByTpl.TryGetValue(tpl, out var sourceName) ? sourceName : string.Empty;
    }

    private static void RefreshIfChangedOrThrottledLocked(bool forceCountCheck = false)
    {
        if (_databaseService == null)
        {
            return;
        }

        if (!_initialized || Templates.Count == 0)
        {
            RebuildLocked(_databaseService, "loaded");
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            var shouldCount = forceCountCheck || (now - _lastRefreshUtc).TotalSeconds >= 1d;
            if (!shouldCount)
            {
                return;
            }

            var runtimeCount = CountTemplateEntries(_databaseService.GetItems());
            if (runtimeCount != Templates.Count)
            {
                RebuildLocked(_databaseService, "refreshed");
                return;
            }

            _lastRefreshUtc = now;
        }
        catch
        {
            if ((DateTime.UtcNow - _lastRefreshUtc).TotalSeconds >= 1d)
            {
                RebuildLocked(_databaseService, "refreshed");
            }
        }
    }

    private static void RebuildLocked(DatabaseService databaseService, string verb)
    {
        Templates.Clear();
        DisplayNames.Clear();
        _sourceByTpl = null;

        try
        {
            var items = databaseService.GetItems();
            var localeNames = BuildLocaleNameLookup(databaseService);
            foreach (var (tpl, item) in EnumerateTemplateEntries(items))
            {
                if (string.IsNullOrWhiteSpace(tpl))
                {
                    continue;
                }

                Templates.Add(tpl);
                var name = ResolveItemName(localeNames, tpl, item);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    DisplayNames[tpl] = name;
                }
            }

            _initialized = true;
            _lastRefreshUtc = DateTime.UtcNow;
            Logger.Info($"[ULE] Runtime template safety index {verb} {Templates.Count} item templates.");
        }
        catch (Exception ex)
        {
            _initialized = false;
            _lastRefreshUtc = DateTime.UtcNow;
            Logger.Warn($"[ULE] Runtime template safety index could not be built: {ex.Message}");
        }
    }

    private static int CountTemplateEntries(object items)
    {
        var count = 0;
        foreach (var (tpl, _) in EnumerateTemplateEntries(items))
        {
            if (!string.IsNullOrWhiteSpace(tpl))
            {
                count++;
            }
        }

        return count;
    }

    private static Dictionary<string, string> BuildRuntimeItemSourceMap()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var baseTemplates = LoadBaseTemplateIds();
        var modsRoot = GetUserModsRoot();
        if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
        {
            return result;
        }

        foreach (var modDir in Directory.EnumerateDirectories(modsRoot))
        {
            var folderName = Path.GetFileName(modDir);
            if (string.Equals(folderName, ModConstants.ModFolderName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourceName = ResolveModDisplayName(modDir);
            foreach (var file in EnumerateCandidateItemFiles(modDir))
            {
                IEnumerable<string> templateIds;
                try
                {
                    templateIds = ReadTopLevelTemplateKeys(file).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var tpl in templateIds)
                {
                    if (!Templates.Contains(tpl) || baseTemplates.Contains(tpl))
                    {
                        continue;
                    }

                    result.TryAdd(tpl, sourceName);
                }
            }
        }

        Logger.Info($"[ULE] Runtime mod-source index matched {result.Count} modded item templates.");
        return result;
    }

    private static IEnumerable<(string Tpl, object Item)> EnumerateTemplateEntries(object items)
    {
        if (items == null)
        {
            yield break;
        }

        if (items is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                var tpl = entry.Key?.ToString();
                if (!string.IsNullOrWhiteSpace(tpl))
                {
                    yield return (tpl, entry.Value);
                }
            }

            yield break;
        }

        if (items is IEnumerable enumerable)
        {
            foreach (var entry in enumerable)
            {
                var entryType = entry?.GetType();
                var key = entryType?.GetProperty("Key", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry)?.ToString();
                var value = entryType?.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    yield return (key, value);
                }
            }
        }
    }

    private static string ResolveItemName(IReadOnlyDictionary<string, string> localeNames, string tpl, object item)
    {
        if (localeNames != null &&
            localeNames.TryGetValue(tpl, out var localized) &&
            !string.IsNullOrWhiteSpace(localized))
        {
            return localized;
        }

        foreach (var name in new[] { "Name", "ShortName", "_name" })
        {
            var value = ReadStringMember(item, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static Dictionary<string, string> BuildLocaleNameLookup(DatabaseService databaseService)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var locales = databaseService.GetLocales();
            if (locales == null)
            {
                return result;
            }

            var dictionary = InvokeInstanceMethod(locales, "GetDictionary");
            if (dictionary is IDictionary outer)
            {
                foreach (DictionaryEntry localeEntry in outer)
                {
                    if (localeEntry.Value is IDictionary localeDict)
                    {
                        foreach (DictionaryEntry entry in localeDict)
                        {
                            var key = entry.Key?.ToString();
                            if (string.IsNullOrWhiteSpace(key) ||
                                !key.EndsWith(" Name", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            var tpl = key.Substring(0, key.Length - " Name".Length);
                            if (!LooksLikeTemplateId(tpl))
                            {
                                continue;
                            }

                            var name = entry.Value?.ToString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                result.TryAdd(tpl, name);
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return result;
    }

    private static object InvokeInstanceMethod(object target, string methodName)
    {
        return target?.GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.Invoke(target, Array.Empty<object>());
    }

    private static string ReadStringMember(object target, string name)
    {
        if (target == null)
        {
            return string.Empty;
        }

        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        try
        {
            var prop = target.GetType().GetProperty(name, flags);
            if (prop?.CanRead == true)
            {
                return prop.GetValue(target)?.ToString() ?? string.Empty;
            }

            var field = target.GetType().GetField(name, flags);
            if (field != null)
            {
                return field.GetValue(target)?.ToString() ?? string.Empty;
            }
        }
        catch { }

        return string.Empty;
    }

    private static HashSet<string> LoadBaseTemplateIds()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(AppContext.BaseDirectory, "SPT_Data", "database", "templates", "items.json");
        if (!File.Exists(path))
        {
            return result;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (LooksLikeTemplateId(property.Name))
                {
                    result.Add(property.Name);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[ULE] Could not read vanilla item template index: {ex.Message}");
        }

        return result;
    }

    private static IEnumerable<string> EnumerateCandidateItemFiles(string modDir)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(modDir, "*.json", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            if (IsLikelyCustomItemFile(modDir, file))
            {
                yield return file;
            }
        }
    }

    private static bool IsLikelyCustomItemFile(string modDir, string file)
    {
        var relative = Path.GetRelativePath(modDir, file).Replace('\\', '/');
        var name = Path.GetFileName(file);

        return relative.IndexOf("CustomItems/", StringComparison.OrdinalIgnoreCase) >= 0
               || relative.IndexOf("CustomItem/", StringComparison.OrdinalIgnoreCase) >= 0
               || string.Equals(relative, "db/items.json", StringComparison.OrdinalIgnoreCase)
               || string.Equals(relative, "db/templates/items.json", StringComparison.OrdinalIgnoreCase)
               || string.Equals(relative, "database/templates/items.json", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, "customItems.json", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ReadTopLevelTemplateKeys(string file)
    {
        using var stream = File.OpenRead(file);
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (LooksLikeTemplateId(property.Name))
            {
                yield return property.Name;
            }
        }
    }

    private static bool LooksLikeTemplateId(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.Length == 24
               && value.All(Uri.IsHexDigit);
    }

    private static string ResolveModDisplayName(string modDir)
    {
        foreach (var fileName in new[] { "package.json", "mod.json" })
        {
            var path = Path.Combine(modDir, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var property in new[] { "displayName", "name", "modName" })
                {
                    if (doc.RootElement.TryGetProperty(property, out var value) &&
                        value.ValueKind == JsonValueKind.String)
                    {
                        var name = value.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            return PrettifyModName(name);
                        }
                    }
                }
            }
            catch { }
        }

        return PrettifyModName(Path.GetFileName(modDir));
    }

    private static string PrettifyModName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Modded";
        }

        var result = value.Trim();
        result = Regex.Replace(result, @"\s*-\s*", " - ");
        result = result.Replace('_', ' ');
        result = Regex.Replace(result, @"(?<=[a-z])(?=[A-Z])", " ");
        result = Regex.Replace(result, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(result) ? "Modded" : result;
    }

    private static string GetUserModsRoot()
    {
        var currentDirCandidate = Path.Combine(Environment.CurrentDirectory, "user", "mods");
        if (Directory.Exists(currentDirCandidate))
        {
            return currentDirCandidate;
        }

        var appBaseCandidate = Path.Combine(AppContext.BaseDirectory, "user", "mods");
        return Directory.Exists(appBaseCandidate) ? appBaseCandidate : string.Empty;
    }
}
