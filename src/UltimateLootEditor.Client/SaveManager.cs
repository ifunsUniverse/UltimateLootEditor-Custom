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
            var snapshot = CloneMapEdits(edits);
            NormalizeMapEdits(snapshot);

            var folder = DbPaths.MapEditsFolder(snapshot.MapId);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "edits.json");
            var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
            File.WriteAllText(path, json);
        }

        public static MapEdits ShallowSnapshotMapEdits(MapEdits edits)
        {
            if (edits == null)
            {
                return new MapEdits();
            }

            return new MapEdits
            {
                MapId = edits.MapId,
                BySpawnId = edits.BySpawnId?
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                    .ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        System.StringComparer.Ordinal)
                    ?? new Dictionary<string, SpawnEdit>()
            };
        }

        public static MapEdits CloneMapEdits(MapEdits edits)
        {
            if (edits == null)
            {
                return new MapEdits();
            }

            return new MapEdits
            {
                MapId = edits.MapId,
                BySpawnId = edits.BySpawnId?
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                    .ToDictionary(
                        pair => pair.Key,
                        pair => CloneSpawnEdit(pair.Value),
                        System.StringComparer.Ordinal)
                    ?? new Dictionary<string, SpawnEdit>()
            };
        }

        private static SpawnEdit CloneSpawnEdit(SpawnEdit edit)
        {
            if (edit == null)
            {
                return null;
            }

            return new SpawnEdit
            {
                SpawnChance = edit.SpawnChance,
                IsAlwaysSpawn = edit.IsAlwaysSpawn,
                UseGravity = edit.UseGravity,
                IsCreated = edit.IsCreated,
                Name = edit.Name,
                Position = CloneVector(edit.Position),
                Rotation = CloneVector(edit.Rotation),
                Items = edit.Items?
                    .Select(item => item?.Clone())
                    .Where(item => item != null)
                    .ToList()
            };
        }

        private static SavedVector3 CloneVector(SavedVector3 value)
        {
            if (value == null)
            {
                return null;
            }

            return new SavedVector3
            {
                X = value.X,
                Y = value.Y,
                Z = value.Z
            };
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
