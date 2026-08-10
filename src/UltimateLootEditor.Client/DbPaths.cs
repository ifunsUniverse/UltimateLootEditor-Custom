#region DbPaths.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateLootEditor.Shared;

namespace ULE.SpawnEditor
{
    internal static class DbPaths
    {
        public static readonly string[] KnownMapIds = MapCatalog.All.Select(map => map.Id).ToArray();
        public static readonly string[] MapSelectorDisplayValues = MapCatalog.SelectorValues;

        public static bool IsKnownMapId(string mapId)
        {
            return MapCatalog.TryGetById(mapId, out _);
        }

        public static string GetMapDisplayName(string mapId)
        {
            return MapCatalog.TryGetById(mapId, out var descriptor)
                ? descriptor.DisplayName
                : mapId ?? string.Empty;
        }

        public static string ResolveMapIdFromSelector(string selector)
        {
            if (string.IsNullOrWhiteSpace(selector))
            {
                return null;
            }

            if (string.Equals(selector, "off", StringComparison.OrdinalIgnoreCase))
            {
                return "off";
            }

            return MapCatalog.TryResolveSelector(selector, out var descriptor)
                ? descriptor.Id
                : null;
        }

        public static void EnsureAllMapEditFolders()
        {
            foreach (var id in KnownMapIds)
            {
                MapEditsFolder(id);
            }
        }

        public static void CleanupPluginRuntimeArtifacts()
        {
            var pluginRoot = Path.Combine(BepInEx.Paths.PluginPath, ModConstants.ModFolderName);
            Directory.CreateDirectory(pluginRoot);

            DeleteFileIfExists(Path.Combine(pluginRoot, "Ultimate Loot Editor.pdb"));
            DeleteFileIfExists(Path.Combine(pluginRoot, "UltimateLootEditor.Shared.dll"));
            DeleteFileIfExists(Path.Combine(pluginRoot, "UltimateLootEditor.Shared.pdb"));
            DeleteDirectoryIfExists(Path.Combine(pluginRoot, "Cache"));
        }

        public static string MapEditsFolder(string mapId)
        {
            var descriptor = MapCatalog.TryGetById(mapId, out var found)
                ? found
                : new MapDescriptor(mapId, mapId, mapId);

            var dir = Path.Combine(ServerModRoot(), "maps", descriptor.FolderName);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static IEnumerable<string> LegacyMapEditsFolders(string mapId)
        {
            foreach (var folderName in MapCatalog.GetLegacyFolderNames(mapId).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                yield return Path.Combine(ServerModRoot(), "maps", folderName);
            }

            var pluginRoots = new[]
            {
                Path.Combine(BepInEx.Paths.PluginPath, "Ultimate Loot Editor - Spawn Editor", "maps"),
                Path.Combine(BepInEx.Paths.PluginPath, ModConstants.ModFolderName, "maps"),
            };

            foreach (var root in pluginRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var folderName in MapCatalog.GetLegacyFolderNames(mapId).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    yield return Path.Combine(root, folderName);
                }
            }
        }

        public static string ResolveLocalesEnPath()
        {
            string root = BepInEx.Paths.GameRootPath;

            var preferred = Path.Combine(root, "SPT_Runtime", "SPT_Data", "database", "locales", "global", "en.json");
            if (File.Exists(preferred))
            {
                return preferred;
            }

            string[] candidates =
            {
                Path.Combine(root, "SPT_Runtime", "SPT_Data", "database", "locales", "global", "en.json"),
                Path.Combine(root, "SPT", "SPT_Data", "database", "locales", "global", "en.json"),
                Path.Combine(root, "SPT_Data", "database", "locales", "global", "en.json"),
                Path.Combine(root, "SPT_Data", "Server", "database", "locales", "global", "en.json"),
                Path.Combine(root, "Aki_Data", "database", "locales", "global", "en.json"),
                Path.Combine(root, "Aki_Data", "Server", "database", "locales", "global", "en.json"),
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        public static string ResolveItemsPath()
        {
            string root = BepInEx.Paths.GameRootPath;

            string[] candidates =
            {
                Path.Combine(root, "SPT_Runtime", "SPT_Data", "database", "templates", "items.json"),
                Path.Combine(root, "SPT", "SPT_Data", "database", "templates", "items.json"),
                Path.Combine(root, "SPT_Data", "database", "templates", "items.json"),
                Path.Combine(root, "SPT_Data", "Server", "database", "templates", "items.json"),
                Path.Combine(root, "Aki_Data", "database", "templates", "items.json"),
                Path.Combine(root, "Aki_Data", "Server", "database", "templates", "items.json"),
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        public static string ResolveGlobalsPath()
        {
            string root = BepInEx.Paths.GameRootPath;

            string[] candidates =
            {
                Path.Combine(root, "SPT_Runtime", "SPT_Data", "database", "globals.json"),
                Path.Combine(root, "SPT", "SPT_Data", "database", "globals.json"),
                Path.Combine(root, "SPT_Data", "database", "globals.json"),
                Path.Combine(root, "SPT_Data", "Server", "database", "globals.json"),
                Path.Combine(root, "Aki_Data", "database", "globals.json"),
                Path.Combine(root, "Aki_Data", "Server", "database", "globals.json"),
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        public static string NormalizeMapId(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            var id = candidate.Trim().ToLowerInvariant();
            switch (id)
            {
                case "customs":
                case "custom":
                    return "bigmap";
                case "reserve":
                case "reservbase":
                    return "rezervbase";
                case "streets":
                    return "tarkovstreets";
                case "groundzero":
                case "gz":
                    return "sandbox";
                default:
                    return IsKnownMapId(id) ? id : null;
            }
        }

        public static string ResolveLooseLootPath(string mapId, BepInEx.Logging.ManualLogSource log)
        {
            string root = BepInEx.Paths.GameRootPath;
            if (string.IsNullOrWhiteSpace(mapId))
            {
                return null;
            }

            string id = mapId.ToLowerInvariant();
            if (id == "customs" || id == "custom")
            {
                id = "bigmap";
            }
            else if (id == "reserve")
            {
                id = "rezervbase";
            }
            else if (id == "streets")
            {
                id = "tarkovstreets";
            }
            else if (id == "gz" || id == "groundzero")
            {
                id = "sandbox";
            }

            var folderCandidates = new List<string>();
            if (id == "sandbox")
            {
                folderCandidates.Add("sandbox");
                folderCandidates.Add("sandbox_high");
            }
            else if (id == "sandbox_high")
            {
                folderCandidates.Add("sandbox_high");
                folderCandidates.Add("sandbox");
            }
            else
            {
                folderCandidates.Add(id);
            }

            foreach (var folder in folderCandidates)
            {
                var preferred = Path.Combine(root, "SPT_Runtime", "SPT_Data", "database", "locations", folder, "looseLoot.json");
                if (File.Exists(preferred))
                {
                    return preferred;
                }

                var legacyPreferred = Path.Combine(root, "SPT", "SPT_Data", "database", "locations", folder, "looseLoot.json");
                if (File.Exists(legacyPreferred))
                {
                    return legacyPreferred;
                }
            }

            string[] bases =
            {
                Path.Combine("SPT_Runtime", "SPT_Data"),
                "SPT_Data",
                Path.Combine("SPT_Data", "Server"),
                "Aki_Data",
                Path.Combine("Aki_Data", "Server"),
            };

            foreach (var folder in folderCandidates)
            {
                foreach (var basePath in bases)
                {
                    var candidate = Path.Combine(root, basePath, "database", "locations", folder, "looseLoot.json");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            log?.LogWarning($"[ULE] No looseLoot.json found for '{mapId}'. Checked known locations.");
            return null;
        }

        private static string ServerModRoot()
        {
            var dir = Path.Combine(BepInEx.Paths.GameRootPath, "SPT_Runtime", "user", "mods", ModConstants.ModFolderName);
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void DeleteDirectoryIfExists(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }
}
#endregion
