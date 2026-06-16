using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace ULE.SpawnEditor
{
    internal static class RuntimeItemSourceCatalog
    {
        private const string Route = "/ule/runtime/item-sources";
        private static readonly object Sync = new object();
        private static Dictionary<string, string> _sources;
        private static bool _loaded;
        private static DateTime _nextRetryUtc = DateTime.MinValue;

        public static string SourceFromTpl(string tpl)
        {
            if (string.IsNullOrWhiteSpace(tpl))
            {
                return string.Empty;
            }

            EnsureLoaded();
            lock (Sync)
            {
                return _sources != null && _sources.TryGetValue(tpl, out var source)
                    ? source
                    : string.Empty;
            }
        }

        public static string PrefixDisplayName(string tpl, string displayName)
        {
            var name = string.IsNullOrWhiteSpace(displayName) ? tpl : displayName.Trim();
            var source = SourceFromTpl(tpl);
            if (string.IsNullOrWhiteSpace(source))
            {
                return name;
            }

            var prefix = $"[{source}] ";
            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? name
                : prefix + name;
        }

        private static void EnsureLoaded()
        {
            lock (Sync)
            {
                if (_loaded)
                {
                    return;
                }

                if (DateTime.UtcNow < _nextRetryUtc)
                {
                    return;
                }

                _sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var json = RequestHandler.GetJson(Route);
                if (string.IsNullOrWhiteSpace(json) ||
                    string.Equals(json.Trim(), "null", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var trimmed = json.TrimStart();
                if (trimmed.StartsWith("{", StringComparison.Ordinal))
                {
                    var obj = JObject.Parse(json);
                    var errorToken = GetPropertyOrDefault(obj, "error") ?? GetPropertyOrDefault(obj, "err");
                    if (errorToken != null && errorToken.Type != JTokenType.Null)
                    {
                        MarkRetry();
                        return;
                    }
                }

                var parsed = JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                             ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                lock (Sync)
                {
                    _sources = new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
                    _loaded = parsed.Count > 0;
                    _nextRetryUtc = _loaded ? DateTime.MinValue : DateTime.UtcNow.AddSeconds(5);
                }
            }
            catch
            {
                // The source prefix is optional. Search still works without this route.
                MarkRetry();
            }
        }

        private static void MarkRetry()
        {
            lock (Sync)
            {
                _loaded = false;
                _nextRetryUtc = DateTime.UtcNow.AddSeconds(5);
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
