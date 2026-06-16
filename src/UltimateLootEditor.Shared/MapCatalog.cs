using System;
using System.Collections.Generic;
using System.Linq;

namespace UltimateLootEditor.Shared
{
    public sealed class MapDescriptor
    {
        public MapDescriptor(string id, string displayName, string folderName)
        {
            Id = id;
            DisplayName = displayName;
            FolderName = folderName;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string FolderName { get; }
    }

    public static class MapCatalog
    {
        public static readonly IReadOnlyList<MapDescriptor> All = new[]
        {
            new MapDescriptor("bigmap", "Customs", "Customs"),
            new MapDescriptor("factory4_day", "Factory (Day)", "FactoryDay"),
            new MapDescriptor("factory4_night", "Factory (Night)", "FactoryNight"),
            new MapDescriptor("interchange", "Interchange", "Interchange"),
            new MapDescriptor("laboratory", "Labs", "Labs"),
            new MapDescriptor("labyrinth", "Labyrinth", "Labyrinth"),
            new MapDescriptor("lighthouse", "Lighthouse", "Lighthouse"),
            new MapDescriptor("rezervbase", "Reserve", "Reserve"),
            new MapDescriptor("sandbox", "Ground Zero (1-20)", "GroundZero"),
            new MapDescriptor("sandbox_high", "Ground Zero (21+)", "GroundZero21"),
            new MapDescriptor("shoreline", "Shoreline", "Shoreline"),
            new MapDescriptor("tarkovstreets", "Streets of Tarkov", "Streets"),
            new MapDescriptor("woods", "Woods", "Woods"),
        };

        public static string[] SelectorValues { get; } =
            (new[] { "off" }).Concat(All.Select(map => map.DisplayName)).ToArray();

        private static readonly Dictionary<string, MapDescriptor> ById =
            All.ToDictionary(map => map.Id, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, MapDescriptor> ByDisplayName =
            All.ToDictionary(map => map.DisplayName, StringComparer.OrdinalIgnoreCase);

        public static bool TryGetById(string mapId, out MapDescriptor descriptor)
        {
            descriptor = null;
            if (string.IsNullOrWhiteSpace(mapId))
            {
                return false;
            }

            return ById.TryGetValue(mapId, out descriptor);
        }

        public static bool TryResolveSelector(string selector, out MapDescriptor descriptor)
        {
            descriptor = null;
            if (string.IsNullOrWhiteSpace(selector))
            {
                return false;
            }

            if (ById.TryGetValue(selector, out descriptor))
            {
                return true;
            }

            return ByDisplayName.TryGetValue(selector, out descriptor);
        }

        public static IEnumerable<string> GetLegacyFolderNames(string mapId)
        {
            if (!TryGetById(mapId, out var descriptor))
            {
                yield break;
            }

            yield return descriptor.FolderName;
            yield return descriptor.DisplayName;
            yield return descriptor.Id;
        }
    }
}
