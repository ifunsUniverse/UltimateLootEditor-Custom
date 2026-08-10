// File: ModMetadata.cs
// Purpose: Make the SPT server recognize/load ULE as a server mod.
using SPTarkov.Server.Core.Models.Spt.Mod;
using UltimateLootEditor.Shared;
using Version = SemanticVersioning.Version;
using Range = SemanticVersioning.Range;

namespace UltimateLootEditor;

public sealed record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = ModConstants.ServerModGuid;

    public string Name { get; init; } = ModConstants.ModDisplayName;
    public string Author { get; init; } = "GoatBoy";
    public List<string>? Contributors { get; init; } = null;

    // Must be 3-part semver.
    public Version Version { get; init; } = new(ModConstants.Version);

    // Target SPT range; "~4.1.0" = any 4.1.x
    public Range SptVersion { get; init; } = new("~4.1.0");

    public bool HasPrepatcher { get; init; } = false;

    public List<string>? Incompatibilities { get; init; } = new()
    {};

    // Optional dependency map (none required for ULE).
    public Dictionary<string, Range>? ModDependencies { get; init; } = null;

    public string? Url { get; init; } = null;

    // Pick a license string you prefer.
    public string License { get; init; } = "MIT";
}
