#region LooseLootParser.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ULE.SpawnEditor
{
    internal static class LooseLootParser
    {
        public static List<SpawnPointData> LoadSpawnIndex(string mapId, string path, BepInEx.Logging.ManualLogSource log)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException($"looseLoot.json was not found for map '{mapId}'.", path);
            }

            var spawns = BuildSpawnIndexFromSource(path, log, includeItems: true);
            var itemCount = spawns.Sum(spawn => spawn?.Items?.Count ?? 0);
            log?.LogInfo($"[ULE] Loaded raid-scoped loose loot data for '{mapId}' with {spawns.Count} spawns and {itemCount} root item entries in memory.");
            return spawns;
        }

        public static List<SpawnPointData> LoadOrBuildSpawnIndex(string mapId, string path, BepInEx.Logging.ManualLogSource log)
        {
            return LoadSpawnIndex(mapId, path, log);
        }

        public static List<SpawnPointData> LoadSpawnIndexFromJson(string mapId, string json, BepInEx.Logging.ManualLogSource log, string sourceLabel)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<SpawnPointData>();
            }

            var spawns = BuildSpawnIndexFromEntries(EnumerateSpawnEntriesFromJson(json), log, includeItems: true);
            var itemCount = spawns.Sum(spawn => spawn?.Items?.Count ?? 0);
            log?.LogInfo($"[ULE] Loaded raid-scoped loose loot data for '{mapId}' from {sourceLabel ?? "runtime JSON"} with {spawns.Count} spawns and {itemCount} root item entries in memory.");
            return spawns;
        }

        public static bool TryLoadSpawnDetails(string path, string spawnId, out SpawnPointData spawn, out string error)
        {
            spawn = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(spawnId))
            {
                error = "Spawn id is missing.";
                return false;
            }

            if (!TryLoadSpawnDetails(path, new[] { spawnId }, out var spawns, out error))
            {
                if (string.IsNullOrWhiteSpace(error))
                {
                    error = $"Spawn '{spawnId}' was not found in looseLoot.json.";
                }

                return false;
            }

            spawn = spawns.FirstOrDefault(candidate => string.Equals(candidate?.Id, spawnId, StringComparison.Ordinal));
            if (spawn != null)
            {
                return true;
            }

            error = $"Spawn '{spawnId}' was not found in looseLoot.json.";
            return false;
        }

        public static bool TryLoadSpawnDetails(string path, IEnumerable<string> spawnIds, out List<SpawnPointData> spawns, out string error)
        {
            spawns = new List<SpawnPointData>();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                error = "looseLoot.json was not found.";
                return false;
            }

            var remaining = new HashSet<string>(
                (spawnIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.Ordinal);

            if (remaining.Count == 0)
            {
                error = "No spawn ids were supplied.";
                return false;
            }

            try
            {
                var syntheticIndex = 0;
                foreach (var entry in EnumerateSpawnEntries(path))
                {
                    var currentIndex = syntheticIndex++;
                    if (!TryParseSpawnSummary(entry, currentIndex, out var summary) ||
                        summary == null ||
                        string.IsNullOrWhiteSpace(summary.Id) ||
                        !remaining.Contains(summary.Id))
                    {
                        continue;
                    }

                    if (!TryParseDetailedSpawn(entry, currentIndex, out var parsed) || parsed == null)
                    {
                        continue;
                    }

                    spawns.Add(parsed);
                    remaining.Remove(summary.Id);
                    if (remaining.Count == 0)
                    {
                        return true;
                    }
                }

                if (spawns.Count > 0)
                {
                    error = remaining.Count > 0
                        ? $"Some requested spawns were not found in looseLoot.json: {string.Join(", ", remaining)}"
                        : string.Empty;
                    return true;
                }

                error = "Requested spawns were not found in looseLoot.json.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static List<SpawnPointData> BuildSpawnIndexFromSource(string path, BepInEx.Logging.ManualLogSource log, bool includeItems)
        {
            return BuildSpawnIndexFromEntries(EnumerateSpawnEntries(path), log, includeItems);
        }

        private static List<SpawnPointData> BuildSpawnIndexFromEntries(IEnumerable<JObject> entries, BepInEx.Logging.ManualLogSource log, bool includeItems)
        {
            var list = new List<SpawnPointData>();

            try
            {
                var syntheticIndex = 0;
                foreach (var entry in entries ?? Enumerable.Empty<JObject>())
                {
                    var currentIndex = syntheticIndex++;
                    SpawnPointData spawn;
                    var parsed = includeItems
                        ? TryParseDetailedSpawn(entry, currentIndex, out spawn)
                        : TryParseSpawnSummary(entry, currentIndex, out spawn);

                    if (parsed)
                    {
                        list.Add(spawn);
                    }
                }
            }
            catch (Exception ex)
            {
                log?.LogError($"[ULE] Failed to load looseLoot.json into memory: {ex}");
            }

            return list;
        }

        private static IEnumerable<JObject> EnumerateSpawnEntriesFromJson(string json)
        {
            var root = JToken.Parse(json);
            foreach (var entry in EnumerateSpawnEntries(root))
            {
                yield return entry;
            }
        }

        private static IEnumerable<JObject> EnumerateSpawnEntries(JToken root)
        {
            if (root == null || root.Type == JTokenType.Null || root.Type == JTokenType.Undefined)
            {
                yield break;
            }

            if (root is JArray array)
            {
                foreach (var entry in array.OfType<JObject>())
                {
                    yield return entry;
                }

                yield break;
            }

            if (!(root is JObject obj))
            {
                yield break;
            }

            foreach (var property in obj.Properties())
            {
                if (!string.Equals(property.Name, "spawnpoints", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(property.Name, "spawnpointsForced", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!(property.Value is JArray spawnArray))
                {
                    continue;
                }

                foreach (var entry in spawnArray.OfType<JObject>())
                {
                    yield return entry;
                }
            }

            var data = GetPropertyOrDefault(obj, "data");
            if (data == null)
            {
                yield break;
            }

            foreach (var entry in EnumerateSpawnEntries(data))
            {
                yield return entry;
            }
        }

        private static IEnumerable<JObject> EnumerateSpawnEntries(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var textReader = new StreamReader(stream))
            using (var reader = new JsonTextReader(textReader) { DateParseHandling = DateParseHandling.None })
            {
                while (reader.Read())
                {
                    if (reader.TokenType != JsonToken.PropertyName)
                    {
                        continue;
                    }

                    var propertyName = reader.Value as string;
                    if (!string.Equals(propertyName, "spawnpoints", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(propertyName, "spawnpointsForced", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!reader.Read() || reader.TokenType != JsonToken.StartArray)
                    {
                        continue;
                    }

                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonToken.EndArray)
                        {
                            break;
                        }

                        if (reader.TokenType != JsonToken.StartObject)
                        {
                            continue;
                        }

                        yield return JObject.Load(reader);
                    }
                }
            }
        }

        private static bool TryParseSpawnSummary(JObject entry, int syntheticIndex, out SpawnPointData spawn)
        {
            spawn = null;

            if (TryParseTemplateSpawn(entry, syntheticIndex, includeItems: false, out spawn))
            {
                return true;
            }

            if (TryParseClassicSpawn(entry, syntheticIndex, includeItems: false, out spawn))
            {
                return true;
            }

            return false;
        }

        private static bool TryParseDetailedSpawn(JObject entry, int syntheticIndex, out SpawnPointData spawn)
        {
            spawn = null;

            if (TryParseTemplateSpawn(entry, syntheticIndex, includeItems: true, out spawn))
            {
                return true;
            }

            if (TryParseClassicSpawn(entry, syntheticIndex, includeItems: true, out spawn))
            {
                return true;
            }

            return false;
        }

        private static bool TryParseTemplateSpawn(JObject entry, int syntheticIndex, bool includeItems, out SpawnPointData spawn)
        {
            spawn = null;

            var templ = GetObjectProperty(entry, "template");
            if (templ == null)
            {
                return false;
            }

            var pos = ReadVec3(GetPropertyOrDefault(templ, "Position")) ?? ParseVec3FromString(GetStringProperty(entry, "locationId"));
            if (!pos.HasValue)
            {
                return false;
            }

            var id = GetStringProperty(templ, "Id") ??
                     GetStringProperty(entry, "locationId") ??
                     GetStringProperty(entry, "name") ??
                     GetStringProperty(entry, "id");
            var items = includeItems ? ReadTemplateItems(templ, entry) : new List<LootItem>();
            var templateItems = GetArrayProperty(templ, "Items");
            var alwaysSpawn = ReadBool(GetPropertyOrDefault(templ, "IsAlwaysSpawn")) ??
                              ReadBool(GetPropertyOrDefault(entry, "IsAlwaysSpawn"));

            spawn = new SpawnPointData
            {
                Id = string.IsNullOrWhiteSpace(id) ? MakeSyntheticId(pos.Value, syntheticIndex) : id,
                DetailKey = string.IsNullOrWhiteSpace(id) ? MakeSyntheticId(pos.Value, syntheticIndex) : id,
                Position = pos.Value,
                SpawnChance = Mathf.Clamp01(ReadFloat(GetPropertyOrDefault(entry, "probability")) ?? 1f),
                HasAlwaysSpawnFlag = alwaysSpawn.HasValue,
                IsAlwaysSpawn = alwaysSpawn ?? false,
                ItemCountSummary = includeItems ? items.Count : templateItems?.Count ?? 0,
                DetailsLoaded = includeItems,
                DataVersion = includeItems ? 1 : 0,
                Items = items
            };

            return true;
        }

        private static bool TryParseClassicSpawn(JObject entry, int syntheticIndex, bool includeItems, out SpawnPointData spawn)
        {
            spawn = null;

            var pos = ReadVec3(GetPropertyOrDefault(entry, "position"));
            if (!pos.HasValue)
            {
                return false;
            }

            var id = GetStringProperty(entry, "name") ?? GetStringProperty(entry, "id");
            var arr = GetArrayProperty(entry, "items");
            var items = includeItems ? ReadClassicItems(arr) : new List<LootItem>();
            var spawnId = string.IsNullOrWhiteSpace(id) ? MakeSyntheticId(pos.Value, syntheticIndex) : id;
            var alwaysSpawn = ReadBool(GetPropertyOrDefault(entry, "IsAlwaysSpawn"));

            spawn = new SpawnPointData
            {
                Id = spawnId,
                DetailKey = spawnId,
                Position = pos.Value,
                SpawnChance = Mathf.Clamp01(ReadFloat(GetPropertyOrDefault(entry, "probability")) ?? 1f),
                HasAlwaysSpawnFlag = alwaysSpawn.HasValue,
                IsAlwaysSpawn = alwaysSpawn ?? false,
                ItemCountSummary = includeItems ? items.Count : arr?.Count ?? 0,
                DetailsLoaded = includeItems,
                DataVersion = includeItems ? 1 : 0,
                Items = items
            };

            return true;
        }

        private static List<LootItem> ReadTemplateItems(JObject templ, JObject entry)
        {
            var weightByKey = new Dictionary<string, float>(StringComparer.Ordinal);
            var distArr = GetArrayProperty(entry, "itemDistribution");
            if (distArr != null)
            {
                foreach (var d in distArr.OfType<JObject>())
                {
                    var key = ReadComposedKey(GetPropertyOrDefault(d, "composedKey")) ??
                              GetStringProperty(d, "key");

                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    weightByKey[key] = ReadFloat(GetPropertyOrDefault(d, "relativeProbability")) ?? 1f;
                }
            }

            var roots = BuildTemplateItemTree(GetArrayProperty(templ, "Items"));
            var items = new List<LootItem>(roots.Count);
            foreach (var root in roots)
            {
                if (root == null || string.IsNullOrWhiteSpace(root.Tpl))
                {
                    continue;
                }

                var weight = 1f;
                if (!string.IsNullOrWhiteSpace(root.ComposedKey) && weightByKey.TryGetValue(root.ComposedKey, out var found))
                {
                    weight = found;
                }

                items.Add(new LootItem
                {
                    Tpl = root.Tpl,
                    ComposedKey = root.ComposedKey,
                    Weight = weight,
                    SlotId = root.SlotId,
                    LocationJson = root.LocationJson,
                    UpdJson = root.UpdJson,
                    StackMin = root.StackCount,
                    StackMax = root.StackCount,
                    Children = root.Children.Select(ConvertTemplateChild).ToList()
                });
            }

            return items;
        }

        private static List<LootItem> ReadClassicItems(JArray arr)
        {
            var items = new List<LootItem>();
            if (arr == null)
            {
                return items;
            }

            foreach (var it in arr.OfType<JObject>())
            {
                var tpl = GetStringProperty(it, "tpl") ??
                          GetStringProperty(it, "_tpl") ??
                          GetStringProperty(it, "itemTpl") ??
                          GetStringProperty(it, "template") ??
                          GetStringProperty(it, "templateId") ??
                          GetStringProperty(it, "id");
                if (string.IsNullOrWhiteSpace(tpl))
                {
                    continue;
                }

                var upd = GetPropertyOrDefault(it, "upd");
                items.Add(new LootItem
                {
                    Tpl = tpl,
                    ComposedKey = ReadComposedKey(GetPropertyOrDefault(it, "composedKey")) ?? Util.GenerateComposedKey(),
                    Weight = ReadFloat(GetPropertyOrDefault(it, "weight")) ?? ReadFloat(GetPropertyOrDefault(it, "probability")) ?? 1f,
                    SlotId = GetStringProperty(it, "slotId") ?? string.Empty,
                    LocationJson = GetRawJsonOrNull(GetPropertyOrDefault(it, "location")),
                    UpdJson = GetRawJsonOrNull(upd),
                    StackMin = ReadStackCountFromUpd(upd),
                    StackMax = ReadStackCountFromUpd(upd)
                });
            }

            return items;
        }

        private static List<TemplateItemRecord> BuildTemplateItemTree(JArray templateItems)
        {
            var roots = new List<TemplateItemRecord>();
            if (templateItems == null)
            {
                return roots;
            }

            var ordered = new List<TemplateItemRecord>();
            var byId = new Dictionary<string, TemplateItemRecord>(StringComparer.Ordinal);
            var syntheticId = 0;

            foreach (var item in templateItems.OfType<JObject>())
            {
                var record = ParseTemplateItemRecord(item, syntheticId++);
                if (record == null)
                {
                    continue;
                }

                ordered.Add(record);
                if (!string.IsNullOrWhiteSpace(record.Id) && !byId.ContainsKey(record.Id))
                {
                    byId[record.Id] = record;
                }
            }

            foreach (var record in ordered)
            {
                if (!string.IsNullOrWhiteSpace(record.ParentId) && byId.TryGetValue(record.ParentId, out var parent))
                {
                    parent.Children.Add(record);
                    continue;
                }

                roots.Add(record);
            }

            return roots;
        }

        private static TemplateItemRecord ParseTemplateItemRecord(JObject item, int syntheticId)
        {
            var tpl = GetStringProperty(item, "_tpl") ??
                      GetStringProperty(item, "tpl") ??
                      GetStringProperty(item, "template") ??
                      GetStringProperty(item, "templateId");
            if (string.IsNullOrWhiteSpace(tpl))
            {
                return null;
            }

            var upd = GetPropertyOrDefault(item, "upd");
            return new TemplateItemRecord
            {
                Id = GetStringProperty(item, "_id") ??
                     GetStringProperty(item, "id") ??
                     $"template_item_{syntheticId.ToString(CultureInfo.InvariantCulture)}",
                ParentId = GetStringProperty(item, "parentId") ?? GetStringProperty(item, "ParentId") ?? string.Empty,
                Tpl = tpl,
                SlotId = GetStringProperty(item, "slotId") ?? GetStringProperty(item, "SlotId") ?? string.Empty,
                ComposedKey = ReadComposedKey(GetPropertyOrDefault(item, "composedKey")) ?? string.Empty,
                LocationJson = GetRawJsonOrNull(GetPropertyOrDefault(item, "location")),
                UpdJson = GetRawJsonOrNull(upd),
                StackCount = ReadStackCountFromUpd(upd)
            };
        }

        private static LootItemNode ConvertTemplateChild(TemplateItemRecord record)
        {
            return new LootItemNode
            {
                Tpl = record.Tpl,
                SlotId = record.SlotId,
                LocationJson = record.LocationJson,
                UpdJson = record.UpdJson,
                StackMin = record.StackCount,
                StackMax = record.StackCount,
                Children = record.Children.Select(ConvertTemplateChild).ToList()
            };
        }

        private static Vector3? ReadVec3(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return null;
            }

            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                var x = ReadFloat(GetPropertyOrDefault(obj, "x")) ?? ReadFloat(GetPropertyOrDefault(obj, "X"));
                var y = ReadFloat(GetPropertyOrDefault(obj, "y")) ?? ReadFloat(GetPropertyOrDefault(obj, "Y"));
                var z = ReadFloat(GetPropertyOrDefault(obj, "z")) ?? ReadFloat(GetPropertyOrDefault(obj, "Z"));
                if (x.HasValue && y.HasValue && z.HasValue)
                {
                    return new Vector3(x.Value, y.Value, z.Value);
                }
            }

            if (token.Type == JTokenType.String)
            {
                return ParseVec3FromString(token.Value<string>());
            }

            return null;
        }

        private static Vector3? ParseVec3FromString(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return null;
            }

            var trimmed = s.Trim();
            if (trimmed.StartsWith("(", StringComparison.Ordinal) && trimmed.EndsWith(")", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2);
            }

            var parts = trimmed.IndexOf(',') >= 0
                ? trimmed.Split(',')
                : Regex.Split(trimmed, @"\s+").Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
            if (parts.Length != 3)
            {
                return null;
            }

            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                return null;
            }

            return new Vector3(x, y, z);
        }

        private static string MakeSyntheticId(Vector3 v, int index)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "synthetic_spawn_{0}_{1:0.###}_{2:0.###}_{3:0.###}",
                index,
                v.x,
                v.y,
                v.z);
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

        private static JObject GetObjectProperty(JObject obj, string name)
        {
            return GetPropertyOrDefault(obj, name) as JObject;
        }

        private static JArray GetArrayProperty(JObject obj, string name)
        {
            return GetPropertyOrDefault(obj, name) as JArray;
        }

        private static string GetStringProperty(JObject obj, string name)
        {
            return ReadString(GetPropertyOrDefault(obj, name));
        }

        private static string ReadString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }

            if (token.Type == JTokenType.Integer ||
                token.Type == JTokenType.Float ||
                token.Type == JTokenType.Boolean)
            {
                return token.ToString(Formatting.None);
            }

            return null;
        }

        private static float? ReadFloat(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return null;
            }

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                return token.Value<float>();
            }

            if (token.Type == JTokenType.String &&
                float.TryParse(token.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private static bool? ReadBool(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return null;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }

            if (token.Type == JTokenType.Integer)
            {
                return token.Value<int>() != 0;
            }

            if (token.Type == JTokenType.String &&
                bool.TryParse(token.Value<string>(), out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private static string ReadComposedKey(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                return token.Value<string>();
            }

            if (token.Type == JTokenType.Object)
            {
                var key = GetStringProperty((JObject)token, "key");
                if (!string.IsNullOrWhiteSpace(key))
                {
                    return key;
                }
            }

            return token.ToString(Formatting.None);
        }

        private static string GetRawJsonOrNull(JToken token)
        {
            return token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined
                ? null
                : token.ToString(Formatting.None);
        }

        private static int? ReadStackCountFromUpd(JToken upd)
        {
            var obj = upd as JObject;
            if (obj == null)
            {
                return null;
            }

            var stackCount = ReadFloat(GetPropertyOrDefault(obj, "StackObjectsCount"));
            if (!stackCount.HasValue)
            {
                return null;
            }

            var rounded = (int)Math.Round(stackCount.Value);
            return rounded > 0 ? rounded : (int?)null;
        }

        private sealed class TemplateItemRecord
        {
            public string Id;
            public string ParentId;
            public string Tpl;
            public string SlotId;
            public string ComposedKey;
            public string LocationJson;
            public string UpdJson;
            public int? StackCount;
            public readonly List<TemplateItemRecord> Children = new List<TemplateItemRecord>();
        }
    }
}
#endregion
