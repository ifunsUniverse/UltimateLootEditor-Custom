// File: ModMetadata.cs
// Purpose: Make the SPT 4.0 server recognize/load ULE as a server mod.
using SPTarkov.Server.Core.Models.Spt.Mod;
using UltimateLootEditor.Shared;
using Version = SemanticVersioning.Version;
using Range = SemanticVersioning.Range;

namespace UltimateLootEditor;

public sealed record ModMetadata : AbstractModMetadata
{
    // Use a stable, unique GUID (reverse-DNS recommended). Change "yourname".
    public override string ModGuid { get; init; } = ModConstants.ServerModGuid;

    public override string Name { get; init; } = ModConstants.ModDisplayName;
    public override string Author { get; init; } = "GoatBoy";
    public override List<string>? Contributors { get; init; } = null;

    // Must be 3-part semver.
    public override Version Version { get; init; } = new(ModConstants.Version);

    // Target SPT range; "~4.0" = any 4.0.x
    public override Range SptVersion { get; init; } = new("~4.0.0");

    public override List<string>? Incompatibilities { get; init; } = new()
    {};

    // Optional dependency map (none required for ULE).
    public override Dictionary<string, Range>? ModDependencies { get; init; } = null;

    public override string? Url { get; init; } = null;
    public override bool? IsBundleMod { get; init; } = false;

    // Pick a license string you prefer.
    public override string License { get; init; } = "MIT";
}

