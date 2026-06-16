using System.Collections.Generic;

namespace UltimateLootEditor.Shared
{
    public sealed class UleMapFile
    {
        public string MapId { get; set; } = string.Empty;
        public Dictionary<string, UleSpawnOverride> BySpawnId { get; set; } =
            new Dictionary<string, UleSpawnOverride>();
    }

    public sealed class UleSpawnOverride
    {
        public double SpawnChance { get; set; } = 1.0;
        public bool? IsAlwaysSpawn { get; set; }
        public List<UleItemEntry> Items { get; set; } = new List<UleItemEntry>();
        public int? MinRolls { get; set; } = 1;
        public int? MaxRolls { get; set; } = 1;
    }

    public class UleItemNode
    {
        public string Tpl { get; set; } = string.Empty;
        public string SlotId { get; set; } = string.Empty;
        public string LocationJson { get; set; }
        public string UpdJson { get; set; }
        public int? StackMin { get; set; }
        public int? StackMax { get; set; }
        public List<UleItemNode> Children { get; set; } = new List<UleItemNode>();
    }

    public sealed class UleItemEntry : UleItemNode
    {
        public string ComposedKey { get; set; }
        public double Weight { get; set; } = 1.0;
        public string PresetId { get; set; }
        public string PresetName { get; set; }
    }
}
