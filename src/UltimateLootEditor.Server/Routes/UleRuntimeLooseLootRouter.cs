using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using UltimateLootEditor.Services;
using UltimateLootEditor.Shared;

namespace UltimateLootEditor.Routes;

[Injectable]
public sealed class UleRuntimeLooseLootRouter(
    JsonUtil jsonUtil,
    DatabaseService databaseService,
    ISptLogger<UleRuntimeLooseLootRouter> logger) : DynamicRouter(jsonUtil, [
    new RouteAction<EmptyRequestData>(
        "/ule/runtime/loose-loot",
        (url, _, _, _) =>
        {
            try
            {
                var requestedLocation = ExtractLocationId(url);
                var locationId = NormalizeLocationId(requestedLocation);
                if (string.IsNullOrWhiteSpace(locationId))
                {
                    return ValueTask.FromResult(jsonUtil.Serialize(new RuntimeLooseLootError("Missing location id.")) ?? "{\"error\":\"Missing location id.\"}");
                }

                var locations = databaseService.GetLocations();
                var mappedLocationId = locations.GetMappedKey(locationId);
                if (!string.IsNullOrWhiteSpace(mappedLocationId))
                {
                    locationId = mappedLocationId;
                }

                var dictionary = locations.GetDictionary();
                if (!dictionary.TryGetValue(locationId, out var location) || location?.LooseLoot == null)
                {
                    return ValueTask.FromResult(jsonUtil.Serialize(new RuntimeLooseLootError($"Location '{locationId}' has no loose loot.")) ?? "{\"error\":\"Location has no loose loot.\"}");
                }

                var looseLoot = location.LooseLoot.Value;
                return ValueTask.FromResult(jsonUtil.Serialize(looseLoot) ?? "{}");
            }
            catch (Exception ex)
            {
                logger.Warning($"[ULE] Failed to return runtime loose loot for '{url}': {ex.Message}");
                return ValueTask.FromResult(jsonUtil.Serialize(new RuntimeLooseLootError(ex.Message)) ?? "{\"error\":\"Runtime loose loot route failed.\"}");
            }
        }),
    new RouteAction<EmptyRequestData>(
        "/ule/runtime/item-sources",
        (_, _, _, _) =>
        {
            try
            {
                var sources = RuntimeTemplateIndex.GetRuntimeItemSources(databaseService);
                return ValueTask.FromResult(jsonUtil.Serialize(sources) ?? "{}");
            }
            catch (Exception ex)
            {
                logger.Warning($"[ULE] Failed to return runtime item source map: {ex.Message}");
                return ValueTask.FromResult(jsonUtil.Serialize(new RuntimeLooseLootError(ex.Message)) ?? "{\"error\":\"Runtime item source route failed.\"}");
            }
        }),
    new RouteAction<EmptyRequestData>(
        "/ule/runtime/item-presets",
        (_, _, _, _) =>
        {
            try
            {
                var globals = databaseService.GetGlobals();
                var presets = globals?.GetType()
                    .GetProperty("ItemPresets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(globals);

                return ValueTask.FromResult(jsonUtil.Serialize(presets) ?? "{}");
            }
            catch (Exception ex)
            {
                logger.Warning($"[ULE] Failed to return runtime item presets: {ex.Message}");
                return ValueTask.FromResult(jsonUtil.Serialize(new RuntimeLooseLootError(ex.Message)) ?? "{\"error\":\"Runtime item preset route failed.\"}");
            }
        })
])
{
    private sealed record RuntimeLooseLootError(string Error);

    private static string ExtractLocationId(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var trimmed = url.Trim();
        var index = trimmed.LastIndexOf('/');
        var segment = index >= 0 && index < trimmed.Length - 1
            ? trimmed[(index + 1)..]
            : trimmed;

        return Uri.UnescapeDataString(segment).Trim();
    }

    private static string NormalizeLocationId(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
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
        }

        return MapCatalog.TryGetById(id, out var descriptor)
            ? descriptor.Id
            : id;
    }
}
