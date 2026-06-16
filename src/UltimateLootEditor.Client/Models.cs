#region Models.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ULE.SpawnEditor
{
    internal class SpawnPointData
    {
        public string Id;
        public string DetailKey;
        public Vector3 Position;
        public float SpawnChance = 1f; // [0..1]
        public bool HasAlwaysSpawnFlag;
        public bool IsAlwaysSpawn;
        public int ItemCountSummary;
        public bool DetailsLoaded;
        public int DataVersion;
        public List<LootItem> Items = new List<LootItem>();

        public SpawnPointData Clone()
        {
            return new SpawnPointData
            {
                Id = Id,
                DetailKey = DetailKey,
                Position = Position,
                SpawnChance = SpawnChance,
                HasAlwaysSpawnFlag = HasAlwaysSpawnFlag,
                IsAlwaysSpawn = IsAlwaysSpawn,
                ItemCountSummary = ItemCountSummary,
                DetailsLoaded = DetailsLoaded,
                DataVersion = DataVersion,
                Items = Items?.Select(item => item?.Clone())
                    .Where(item => item != null)
                    .ToList() ?? new List<LootItem>()
            };
        }

        public void CopyFrom(SpawnPointData other)
        {
            if (other == null)
            {
                return;
            }

            Id = other.Id;
            DetailKey = other.DetailKey;
            Position = other.Position;
            SpawnChance = other.SpawnChance;
            HasAlwaysSpawnFlag = other.HasAlwaysSpawnFlag;
            IsAlwaysSpawn = other.IsAlwaysSpawn;
            ItemCountSummary = other.ItemCountSummary;
            DetailsLoaded = other.DetailsLoaded;
            DataVersion = other.DataVersion;
            Items = other.Items?.Select(item => item?.Clone())
                .Where(item => item != null)
                .ToList() ?? new List<LootItem>();
        }
    }

    internal class LootItemNode
    {
        public string Tpl;
        public string SlotId;
        public string LocationJson;
        public string UpdJson;
        public int? StackMin;
        public int? StackMax;
        public List<LootItemNode> Children = new List<LootItemNode>();

        public virtual LootItemNode CloneNode()
        {
            return new LootItemNode
            {
                Tpl = Tpl,
                SlotId = SlotId,
                LocationJson = LocationJson,
                UpdJson = UpdJson,
                StackMin = StackMin,
                StackMax = StackMax,
                Children = Children?.Select(child => child?.CloneNode())
                    .Where(child => child != null)
                    .ToList() ?? new List<LootItemNode>()
            };
        }
    }

    internal class LootItem : LootItemNode
    {
        public string ComposedKey;
        public float Weight = 1f;
        public string PresetId;
        public string PresetName;

        public LootItem Clone()
        {
            return new LootItem
            {
                Tpl = Tpl,
                ComposedKey = ComposedKey,
                Weight = Weight,
                SlotId = SlotId,
                LocationJson = LocationJson,
                UpdJson = UpdJson,
                StackMin = StackMin,
                StackMax = StackMax,
                PresetId = PresetId,
                PresetName = PresetName,
                Children = Children?.Select(child => child?.CloneNode())
                    .Where(child => child != null)
                    .ToList() ?? new List<LootItemNode>()
            };
        }

        public override LootItemNode CloneNode()
        {
            return Clone();
        }
    }

    // Saved edits per spawn
    internal class SpawnEdit
    {
        public float? SpawnChance;           // null = unchanged
        public bool? IsAlwaysSpawn;          // null = unchanged/legacy chance-derived behavior
        public List<LootItem> Items;         // if null = unchanged; if empty = clears
    }

    internal class MapEdits
    {
        public string MapId;
        public Dictionary<string, SpawnEdit> BySpawnId = new Dictionary<string, SpawnEdit>();
    }

    internal class SearchCandidate
    {
        public string Tpl;
        public string DisplayName;
        public bool IsPreset;
        public LootItem TemplateItem;

        public string StableKey =>
            IsPreset && !string.IsNullOrWhiteSpace(TemplateItem?.PresetId)
                ? $"preset::{TemplateItem.PresetId}"
                : Tpl ?? string.Empty;
    }
}
#endregion
