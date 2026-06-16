using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using UltimateLootEditor.Shared;
using UltimateLootEditor.Util;

namespace UltimateLootEditor.Services;

public static class MapOverrideLoader
{
    private const bool PreferLastFileWins = true;
    private static readonly Regex TailGuid = new(@"\[(?<g>[0-9a-fA-F-]{36})\]\s*$", RegexOptions.Compiled);

    public static void EnsureAllMapFolders()
    {
        var mapsRoot = Path.Combine(GetModRoot(), "maps");
        Directory.CreateDirectory(mapsRoot);

        foreach (var map in MapCatalog.All)
        {
            Directory.CreateDirectory(Path.Combine(mapsRoot, map.FolderName));
        }
    }

    public static void CleanupRuntimeArtifacts()
    {
        var modRoot = GetModRoot();
        Directory.CreateDirectory(modRoot);

        DeleteFileIfExists(Path.Combine(modRoot, "Ultimate Loot Editor.pdb"));
        DeleteFileIfExists(Path.Combine(modRoot, "Ultimate Loot Editor.deps.json"));
        DeleteFileIfExists(Path.Combine(modRoot, "UltimateLootEditor.Shared.dll"));
        DeleteFileIfExists(Path.Combine(modRoot, "UltimateLootEditor.Shared.pdb"));
        DeleteFileIfExists(Path.Combine(modRoot, "Newtonsoft.Json.dll"));
        DeleteFileIfExists(Path.Combine(modRoot, "README.md"));
    }

    public static UleActiveIndex? LoadForLocation(string locationId)
    {
        if (string.IsNullOrWhiteSpace(locationId))
        {
            return null;
        }

        var canonicalLocationId = locationId.Trim().ToLowerInvariant();
        var folders = GetCandidateFolders(canonicalLocationId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var roots = GetCandidateRoots().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var index = new UleActiveIndex { LocationId = canonicalLocationId };
        var filesLoaded = 0;

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var folder in folders)
            {
                var dir = Path.Combine(root, folder);
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                var files = Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

                foreach (var path in files)
                {
                    try
                    {
                        var json = File.ReadAllText(path);
                        var data = JsonSerializer.Deserialize<UleMapFile>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            ReadCommentHandling = JsonCommentHandling.Skip,
                            AllowTrailingCommas = true,
                        });

                        if (data is null)
                        {
                            continue;
                        }

                        Merge(index, data);
                        filesLoaded++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Bad JSON '{Path.GetFileName(path)}' in '{folder}': {ex.Message}");
                    }
                }
            }
        }

        Logger.Info(
            $"Location '{canonicalLocationId}' -> folders '{string.Join(", ", folders)}', files: {filesLoaded}, overrides: full={index.ByFullLabel.Count}, guid={index.ByGuid.Count}"
        );

        return index;
    }

    private static IEnumerable<string> GetCandidateFolders(string mapId)
    {
        if (MapCatalog.TryGetById(mapId, out _))
        {
            foreach (var folderName in MapCatalog.GetLegacyFolderNames(mapId))
            {
                yield return folderName;
            }

            yield break;
        }

        yield return mapId;
    }

    private static IEnumerable<string> GetCandidateRoots()
    {
        yield return Path.Combine(GetModRoot(), "maps");
        yield return Path.Combine(AppContext.BaseDirectory, "maps");
    }

    private static void Merge(UleActiveIndex idx, UleMapFile data)
    {
        foreach (var (key, ov) in data.BySpawnId)
        {
            if (PreferLastFileWins || !idx.ByFullLabel.ContainsKey(key))
            {
                idx.ByFullLabel[key] = ov;
            }

            var guid = ExtractGuid(key);
            if (!string.IsNullOrEmpty(guid) && (PreferLastFileWins || !idx.ByGuid.ContainsKey(guid)))
            {
                idx.ByGuid[guid] = ov;
            }
        }
    }

    private static string ExtractGuid(string label)
        => TailGuid.Match(label) is { Success: true } m ? m.Groups["g"].Value.ToLowerInvariant() : string.Empty;

    private static string GetModRoot()
    {
        var currentDirCandidate = Path.Combine(Environment.CurrentDirectory, "user", "mods", ModConstants.ModFolderName);
        if (Directory.Exists(currentDirCandidate))
        {
            return currentDirCandidate;
        }

        var appBaseCandidate = Path.Combine(AppContext.BaseDirectory, "user", "mods", ModConstants.ModFolderName);
        if (Directory.Exists(appBaseCandidate))
        {
            return appBaseCandidate;
        }

        return currentDirCandidate;
    }

    private static void DeleteFileIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Logger.Warn($"Could not delete stale runtime artifact '{Path.GetFileName(path)}': {ex.Message}");
        }
    }
}
