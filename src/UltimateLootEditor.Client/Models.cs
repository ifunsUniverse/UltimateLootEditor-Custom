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
        public string Name;
        public Vector3 Position;
        public Vector3 Rotation;
        public float SpawnChance = 1f; // [0..1]
        public bool HasAlwaysSpawnFlag;
        public bool IsAlwaysSpawn;
        public bool UseGravity = true;
        public int ItemCountSummary;
        public bool DetailsLoaded;
        public int DataVersion;
        public bool IsUserCreated;
        public List<LootItem> Items = new List<LootItem>();

        public SpawnPointData Clone()
        {
            return new SpawnPointData
            {
                Id = Id,
                DetailKey = DetailKey,
                Name = Name,
                Position = Position,
                Rotation = Rotation,
                SpawnChance = SpawnChance,
                HasAlwaysSpawnFlag = HasAlwaysSpawnFlag,
                IsAlwaysSpawn = IsAlwaysSpawn,
                UseGravity = UseGravity,
                ItemCountSummary = ItemCountSummary,
                DetailsLoaded = DetailsLoaded,
                DataVersion = DataVersion,
                IsUserCreated = IsUserCreated,
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
            Name = other.Name;
            Position = other.Position;
            Rotation = other.Rotation;
            SpawnChance = other.SpawnChance;
            HasAlwaysSpawnFlag = other.HasAlwaysSpawnFlag;
            IsAlwaysSpawn = other.IsAlwaysSpawn;
            UseGravity = other.UseGravity;
            ItemCountSummary = other.ItemCountSummary;
            DetailsLoaded = other.DetailsLoaded;
            DataVersion = other.DataVersion;
            IsUserCreated = other.IsUserCreated;
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
        public bool? UseGravity;             // null = unchanged/legacy default
        public bool? IsCreated;
        public string Name;
        public SavedVector3 Position;
        public SavedVector3 Rotation;
        public List<LootItem> Items;         // if null = unchanged; if empty = clears
    }

    internal class MapEdits
    {
        public string MapId;
        public Dictionary<string, SpawnEdit> BySpawnId = new Dictionary<string, SpawnEdit>();
    }

    internal class SavedVector3
    {
        public double X;
        public double Y;
        public double Z;

        public static SavedVector3 FromUnity(Vector3 value)
        {
            return new SavedVector3
            {
                X = value.x,
                Y = value.y,
                Z = value.z
            };
        }

        public Vector3 ToUnity()
        {
            return new Vector3((float)X, (float)Y, (float)Z);
        }
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
