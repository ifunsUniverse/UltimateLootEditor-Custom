using UltimateLootEditor.Shared;

namespace UltimateLootEditor.Services;

public sealed class UleActiveIndex
{
    public string LocationId { get; init; } = string.Empty;
    public Dictionary<string, UleSpawnOverride> ByFullLabel { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, UleSpawnOverride> ByGuid { get; } = new(StringComparer.OrdinalIgnoreCase);
}
