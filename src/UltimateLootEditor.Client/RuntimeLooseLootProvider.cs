#region RuntimeLooseLootProvider.cs
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace ULE.SpawnEditor
{
    internal static class RuntimeLooseLootProvider
    {
        private const string RoutePrefix = "/ule/runtime/loose-loot";

        public static bool TryLoadSpawnIndex(
            string mapId,
            BepInEx.Logging.ManualLogSource log,
            out List<SpawnPointData> spawns,
            out string sourceLabel,
            out string error)
        {
            spawns = null;
            sourceLabel = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(mapId))
            {
                error = "Map id is missing.";
                return false;
            }

            var route = $"{RoutePrefix}/{Uri.EscapeDataString(mapId)}";
            try
            {
                var json = RequestHandler.GetJson(route);
                if (string.IsNullOrWhiteSpace(json) || string.Equals(json.Trim(), "null", StringComparison.OrdinalIgnoreCase))
                {
                    error = "Runtime loose loot route returned no data.";
                    return false;
                }

                var trimmed = json.TrimStart();
                if (trimmed.StartsWith("{", StringComparison.Ordinal))
                {
                    var obj = JObject.Parse(json);
                    var errorToken = GetPropertyOrDefault(obj, "error") ?? GetPropertyOrDefault(obj, "err");
                    if (errorToken != null && errorToken.Type != JTokenType.Null)
                    {
                        error = errorToken.ToString();
                        return false;
                    }
                }

                var loaded = LooseLootParser.LoadSpawnIndexFromJson(mapId, json, log, route);
                if (loaded == null || loaded.Count == 0)
                {
                    error = "Runtime loose loot route contained no parseable spawn points.";
                    return false;
                }

                spawns = loaded;
                sourceLabel = route;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
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
    }
}
#endregion
