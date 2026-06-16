#region SaveManager.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace ULE.SpawnEditor
{
    internal static class SaveManager
    {
        public static MapEdits LoadMapEdits(string mapId)
        {
            var newPath = Path.Combine(DbPaths.MapEditsFolder(mapId), "edits.json");
            var candidatePaths = new[] { newPath }
                .Concat(DbPaths.LegacyMapEditsFolders(mapId).Select(folder => Path.Combine(folder, "edits.json")))
                .Distinct(System.StringComparer.OrdinalIgnoreCase);

            foreach (var path in candidatePaths)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    var raw = File.ReadAllText(path);
                    var edits = JsonConvert.DeserializeObject<MapEdits>(raw) ?? new MapEdits { MapId = mapId };
                    edits.MapId = mapId;
                    if (edits.BySpawnId == null)
                    {
                        edits.BySpawnId = new Dictionary<string, SpawnEdit>();
                    }

                    var upgraded = false;
                    foreach (var edit in edits.BySpawnId.Values)
                    {
                        if (edit?.Items == null)
                        {
                            continue;
                        }

                        foreach (var item in edit.Items)
                        {
                            if (item == null)
                            {
                                continue;
                            }

                            NormalizeItemTree(item);
                            if (!string.IsNullOrWhiteSpace(item.ComposedKey))
                            {
                                continue;
                            }

                            item.ComposedKey = Util.GenerateComposedKey();
                            upgraded = true;
                        }
                    }

                    if (!path.Equals(newPath, System.StringComparison.OrdinalIgnoreCase) || upgraded)
                    {
                        SaveMapEdits(edits);
                    }

                    return edits;
                }
                catch
                {
                    // fall through to the next candidate
                }
            }

            return new MapEdits { MapId = mapId };
        }

        public static void SaveMapEdits(MapEdits edits)
        {
            NormalizeMapEdits(edits);

            var folder = DbPaths.MapEditsFolder(edits.MapId);
            var path = Path.Combine(folder, "edits.json");
            var json = JsonConvert.SerializeObject(edits, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
            File.WriteAllText(path, json);
        }

        private static void NormalizeMapEdits(MapEdits edits)
        {
            if (edits?.BySpawnId == null)
            {
                return;
            }

            foreach (var edit in edits.BySpawnId.Values)
            {
                if (edit?.Items == null)
                {
                    continue;
                }

                foreach (var item in edit.Items)
                {
                    NormalizeItemTree(item);
                }

                LootAmmoAutoFill.FillMagazineAmmo(edit.Items);
            }
        }

        private static void NormalizeItemTree(LootItem item)
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
    }
}
#endregion
