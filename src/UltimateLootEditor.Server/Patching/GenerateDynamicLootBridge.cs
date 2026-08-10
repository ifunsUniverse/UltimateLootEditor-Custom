// File: Patching/GenerateDynamicLootBridge.cs
// ULE bridge — LoL-accurate, spawnpoint-aware, MongoId-aware.
// - Primary: build Template.Items (SptLootItem with ComposedKey:string + Template:MongoId)
//            and Spawnpoint.ItemDistribution (ComposedKey + Weight)
// - Fallback: collapse Template.Items to 1 item (your TPL), then either align ComposedKey to an
//             existing spawnpoint dist key or create a single-entry distribution on the spawnpoint.
// - Writes numeric/bool via backing fields when needed; forces spawn when chance >= 1

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using SPTarkov.Server.Core.Generators.Loot;             // LocationLootGenerator, ContainerItem
using SPTarkov.Server.Core.Models.Eft.Common;           // Spawnpoint
using SPTarkov.Server.Core.Models.Eft.Common.Tables;    // LooseLoot
using UltimateLootEditor.Shared;
using UltimateLootEditor.Util;                          // LabelGuidExtractor, Logger
using UltimateLootEditor.Services;                      // MapOverrideLoader

namespace UltimateLootEditor.Patching
{
    [HarmonyPatch]
    public static class GenerateDynamicLootBridge
    {
        private static readonly Random ForcedSpawnRootRandom = new();
        private static readonly object ForcedSpawnRootRandomLock = new();

        [HarmonyPrefix]
        [HarmonyPatch(typeof(LocationLootGenerator), nameof(LocationLootGenerator.GenerateDynamicLoot))]
        public static bool Prefix(
            LooseLoot dynamicLootDist,
            string locationName)
        {
            var locId = (locationName ?? string.Empty).Trim().ToLowerInvariant();
            var idx = MapOverrideLoader.LoadForLocation(locId);
            if (idx is null || (idx.ByFullLabel.Count == 0 && idx.ByGuid.Count == 0))
            {
                return true;
            }

            try
            {
                // Index live spawnpoints by label & guid
                var dynByLabel = new Dictionary<string, Spawnpoint>(StringComparer.Ordinal);
                var dynByGuid = new Dictionary<string, Spawnpoint>(StringComparer.Ordinal);
                void Index(IEnumerable<Spawnpoint> set)
                {
                    if (set == null) return;
                    foreach (var sp in set)
                    {
                        var label = sp.Template?.Id ?? string.Empty;
                        var locationId = sp.LocationId ?? string.Empty;
                        if (label.Length == 0 && locationId.Length == 0) continue;

                        if (label.Length > 0)
                        {
                            dynByLabel[label] = sp;
                            var gid = LabelGuidExtractor.ExtractGuid(label);
                            if (!string.IsNullOrEmpty(gid)) dynByGuid[gid] = sp;
                        }

                        if (locationId.Length > 0)
                        {
                            dynByLabel[locationId] = sp;
                            var gid = LabelGuidExtractor.ExtractGuid(locationId);
                            if (!string.IsNullOrEmpty(gid)) dynByGuid[gid] = sp;
                        }
                    }
                }
                Index(dynamicLootDist.Spawnpoints);
                Index(dynamicLootDist.SpawnpointsForced);

                var created = EnsureCreatedSpawnpoints(dynamicLootDist, idx, dynByLabel, dynByGuid);
                var forcedSpawnpoints = new HashSet<Spawnpoint>(dynamicLootDist.SpawnpointsForced ?? Enumerable.Empty<Spawnpoint>());
                int touched = 0, wrote = 0, replaced = 0, notFound = 0, noDist = 0, skippedMissing = 0;
                var processedSpawnIds = new HashSet<string>(StringComparer.Ordinal);

                foreach (var pair in idx.ByFullLabel)
                {
                    var label = pair.Key;
                    var ov = pair.Value;

                    if (!dynByLabel.TryGetValue(label, out var sp))
                    {
                        // try by guid
                        var gid = LabelGuidExtractor.ExtractGuid(label);
                        if (!string.IsNullOrEmpty(gid)) dynByGuid.TryGetValue(gid, out sp);
                    }

                    if (sp == null) { notFound++; continue; }
                    if (!processedSpawnIds.Add(sp.Template?.Id ?? label)) { continue; }
                    touched++;

                    var template = sp.Template;
                    if (template == null) { replaced++; continue; }

                    var diag = new List<string>();
                    if (!TryWriteItems(template, ov, sp, locId, label, diag, out var entries))
                    {
                        if (diag.Contains("all edited root items missing"))
                        {
                            skippedMissing++;
                            continue;
                        }

                        if (!TryWriteFirstItem(sp, template, ov, out var fbMsg))
                        {
                            Logger.Warn($"[ULE] {label}: first-item fallback failed: {fbMsg}");
                            continue;
                        }
                        noDist++;
                    }
                    else
                    {
                        wrote++;
                    }

                    ApplySpawnChance(sp, template, ov);
                    ApplySpawnTransform(sp, template, ov);
                    PrepareForcedOrAlwaysSpawnWeightedRoot(sp, template, forcedSpawnpoints.Contains(sp));
                }

                // Also check any overrides keyed by GUID only
                foreach (var pair in idx.ByGuid)
                {
                    var gid = pair.Key;
                    var ov = pair.Value;
                    if (!dynByGuid.TryGetValue(gid, out var sp)) { notFound++; continue; }
                    if (!processedSpawnIds.Add(sp.Template?.Id ?? gid)) { continue; }
                    touched++;

                    var template = sp.Template;
                    if (template == null) { replaced++; continue; }

                    var diag = new List<string>();
                    var label = sp.Template?.Id ?? gid;
                    if (!TryWriteItems(template, ov, sp, locId, label, diag, out var entries))
                    {
                        if (diag.Contains("all edited root items missing"))
                        {
                            skippedMissing++;
                            continue;
                        }

                        if (!TryWriteFirstItem(sp, template, ov, out var fbMsg))
                        {
                            Logger.Warn($"[ULE][guid:{gid}] first-item fallback failed: {fbMsg}");
                            continue;
                        }
                        noDist++;
                    }
                    else
                    {
                        wrote++;
                    }

                    ApplySpawnChance(sp, template, ov);
                    ApplySpawnTransform(sp, template, ov);
                    PrepareForcedOrAlwaysSpawnWeightedRoot(sp, template, forcedSpawnpoints.Contains(sp));
                }

                Logger.Info($"[ULE] map={locId} touched={touched} wrote={wrote} created={created} notFound={notFound} replaced={replaced} noDist={noDist} skippedMissing={skippedMissing}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[ULE] Override apply failed for '{locId}', falling back to vanilla loot generation", ex);
                return true;
            }
        }

        private static int EnsureCreatedSpawnpoints(
            LooseLoot dynamicLootDist,
            UleActiveIndex idx,
            IDictionary<string, Spawnpoint> dynByLabel,
            IDictionary<string, Spawnpoint> dynByGuid)
        {
            if (dynamicLootDist == null || idx?.ByFullLabel == null)
            {
                return 0;
            }

            var created = 0;
            foreach (var pair in idx.ByFullLabel)
            {
                var label = pair.Key;
                var ov = pair.Value;
                if (string.IsNullOrWhiteSpace(label) || ov?.IsCreated != true)
                {
                    continue;
                }

                if (dynByLabel.ContainsKey(label))
                {
                    continue;
                }

                if (ov.Position == null)
                {
                    Logger.Warn($"[ULE] Created spawn '{label}' has no saved position and was skipped.");
                    continue;
                }

                var position = ToServerVector(ov.Position);
                var rotation = ov.Rotation != null
                    ? ToServerVector(ov.Rotation)
                    : new SPTarkov.Server.Core.Models.Eft.Common.Vector3(0f, 0f, 0f);

                var spawn = new Spawnpoint
                {
                    LocationId = label,
                    Probability = Clamp01(ov.SpawnChance),
                    ItemDistribution = new List<LooseLootItemDistribution>(),
                    Template = new SpawnpointTemplate
                    {
                        Id = label,
                        IsContainer = false,
                        UseGravity = ov.UseGravity ?? true,
                        RandomRotation = false,
                        Position = position,
                        Rotation = rotation,
                        IsAlwaysSpawn = ov.IsAlwaysSpawn ?? ov.SpawnChance >= 0.999d,
                        IsGroupPosition = false,
                        GroupPositions = new List<GroupPosition>(),
                        Root = string.Empty,
                        Items = new List<SptLootItem>()
                    }
                };

                if (!AddToSpawnpointList(dynamicLootDist, "Spawnpoints", spawn))
                {
                    Logger.Warn($"[ULE] Created spawn '{label}' could not be added to the loose loot table.");
                    continue;
                }

                dynByLabel[label] = spawn;
                var guid = LabelGuidExtractor.ExtractGuid(label);
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    dynByGuid[guid] = spawn;
                }

                created++;
            }

            return created;
        }

        private static SPTarkov.Server.Core.Models.Eft.Common.Vector3 ToServerVector(UleVector3 value)
        {
            return new SPTarkov.Server.Core.Models.Eft.Common.Vector3(
                Convert.ToSingle(value?.X ?? 0d),
                Convert.ToSingle(value?.Y ?? 0d),
                Convert.ToSingle(value?.Z ?? 0d));
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0d;
            }

            return Math.Max(0d, Math.Min(1d, value));
        }

        public static void PostfixCreateDynamicLootItem(
            SptLootItem chosenItem,
            IEnumerable<SptLootItem> lootItems,
            Dictionary<string, IEnumerable<StaticAmmoDetails>> staticAmmoDist,
            ref ContainerItem __result)
        {
            if (chosenItem == null ||
                string.IsNullOrWhiteSpace(chosenItem.ComposedKey) ||
                !chosenItem.ComposedKey.StartsWith("ULE:", StringComparison.OrdinalIgnoreCase) ||
                __result?.Items == null)
            {
                return;
            }

            try
            {
                var items = __result.Items
                    .Where(item => item != null)
                    .ToList();

                if (items.Count == 0)
                {
                    return;
                }

                RuntimeAmmoResolver.RepairGeneratedAmmo(items, chosenItem.Template.ToString());
                __result.Items = items;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ULE] Failed to repair generated magazine ammo for {chosenItem.Template}: {ex.Message}");
            }
        }

        // === Primary path: write Items + spawn distribution ===================
        private static bool TryWriteItems(
            object template,
            UleSpawnOverride ov,
            Spawnpoint sp,
            string locationId,
            string spawnLabel,
            List<string> diag,
            out int entries)
        {
            entries = 0;

            if (!TryGetEnumerableMember(template, new[] { "Items", "items" }, out var itemsMember, out var elemType))
            { diag.Add("no Items list"); return false; }

            var listObj = GetMemberValue(template, itemsMember) as IEnumerable;
            if (listObj == null)
            { diag.Add("Items is null"); return false; }

            var newElems = new List<object>();
            var dists = new List<(string ck, float w)>();
            int idxCounter = 0;
            string rootId = string.Empty;

            var attemptedRootItems = 0;
            var missingRootItems = 0;
            foreach (var entry in ov?.Items ?? Enumerable.Empty<UleItemEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Tpl))
                {
                    continue;
                }

                attemptedRootItems++;
                if (IsMissingTemplate(entry.Tpl))
                {
                    missingRootItems++;
                    LogMissingItem(locationId, spawnLabel, entry.Tpl, DescribeItem(entry));
                    continue;
                }

                var ordinal = idxCounter++;
                if (!TryAppendLootItemTree(entry, elemType, ordinal, newElems, dists, locationId, spawnLabel, ref rootId, out var reason))
                {
                    diag.Add(reason);
                    DumpElementMembersOnce(elemType, entry.Tpl);
                    continue;
                }
            }

            if (newElems.Count == 0)
            {
                diag.Add(attemptedRootItems > 0 && missingRootItems == attemptedRootItems
                    ? "all edited root items missing"
                    : "no item elements created");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(rootId) && !TrySetStringOrBacking(template, "Root", rootId))
            {
                diag.Add("cannot assign template Root");
            }

            // Replace Items
            if (!TryAssignItemsBack(template, itemsMember, elemType, newElems))
            { diag.Add("cannot assign Items back"); return false; }

            // Create/replace SpawnPoint.ItemDistribution referencing those composed keys
            if (!TryWriteSpawnDistribution(sp, dists, out var why))
            { diag.Add($"spawn dist write failed: {why}"); return false; }

            entries = newElems.Count;
            return true;
        }

        private static bool TryAppendLootItemTree(
            UleItemEntry entry,
            Type elemType,
            int ordinal,
            List<object> newElems,
            List<(string ck, float w)> dists,
            string locationId,
            string spawnLabel,
            ref string firstRootId,
            out string reason)
        {
            reason = string.Empty;

            var rootElem = Activator.CreateInstance(elemType);
            if (rootElem == null)
            {
                reason = "elem ctor failed";
                return false;
            }

            var stackCount = ReadPreferredStackCount(entry);
            if (!TryInitializeLootItem(
                    rootElem,
                    entry.Tpl,
                    entry.ComposedKey,
                    ordinal,
                    stackCount,
                    parentId: null,
                    slotId: entry.SlotId,
                    locationJson: entry.LocationJson,
                    updJson: entry.UpdJson,
                    assignComposedKey: true,
                    out var composedKey,
                    out var itemId,
                    out reason))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(firstRootId))
            {
                firstRootId = itemId;
            }

            if (!TrySetStringOrBacking(rootElem, "ComposedKey", composedKey))
            {
                TrySetComposedKey(rootElem, composedKey);
            }

            newElems.Add(rootElem);
            dists.Add((composedKey, entry.Weight > 0d ? Convert.ToSingle(entry.Weight) : 1f));

            var childOrdinal = ordinal + 1;
            return TryAppendChildLootItems(entry.Children, elemType, itemId, newElems, locationId, spawnLabel, ref childOrdinal, out reason);
        }

        private static bool TryAppendChildLootItems(
            IEnumerable<UleItemNode> children,
            Type elemType,
            string parentId,
            List<object> newElems,
            string locationId,
            string spawnLabel,
            ref int ordinal,
            out string reason)
        {
            reason = string.Empty;
            if (children == null)
            {
                return true;
            }

            foreach (var child in children)
            {
                if (child == null || string.IsNullOrWhiteSpace(child.Tpl))
                {
                    continue;
                }

                if (IsMissingTemplate(child.Tpl))
                {
                    LogMissingItem(locationId, spawnLabel, child.Tpl, DescribeItem(child));
                    continue;
                }

                var elem = Activator.CreateInstance(elemType);
                if (elem == null)
                {
                    reason = "child elem ctor failed";
                    return false;
                }

                var childOrdinal = ordinal++;
                if (!TryInitializeLootItem(
                        elem,
                        child.Tpl,
                        requestedComposedKey: null,
                        childOrdinal,
                        ReadPreferredStackCount(child),
                        parentId,
                        child.SlotId,
                        child.LocationJson,
                        child.UpdJson,
                        assignComposedKey: false,
                        out _,
                        out var childItemId,
                        out reason))
                {
                    return false;
                }

                newElems.Add(elem);

                if (!TryAppendChildLootItems(child.Children, elemType, childItemId, newElems, locationId, spawnLabel, ref ordinal, out reason))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryWriteSpawnDistribution(Spawnpoint sp, List<(string ck, float w)> dists, out string why)
        {
            // Try writing the distribution on the spawnpoint first
            if (TryWriteSpawnDistributionOnHolder(sp, dists))
            { why = string.Empty; return true; }

            // Some shapes hold the distribution on the template instead
            var t = sp?.Template;
            if (t != null && TryWriteSpawnDistributionOnHolder(t, dists))
            { why = "wrote distribution on template (no spawnpoint list)"; return true; }

            why = "no spawnpoint or template distribution list";
            return false;
        }

        private static bool TryWriteSpawnDistributionOnHolder(object holder, List<(string ck, float w)> dists)
        {
            if (holder == null) return false;

            if (!TryFindDistributionMember(holder, out var member, out var listType, out var elemType))
                return false;

            var list = GetMemberValue(holder, member) as IList;
            if (list == null)
            {
                list = CreateListInstance(listType, elemType) as IList;
                if (list == null) return false;
                TrySetMemberValue(holder, member, list);
            }

            list.Clear();
            foreach (var (ck, w) in dists)
            {
                var elem = Activator.CreateInstance(elemType);
                if (elem == null) continue;

                // ComposedKey on the distribution element
                if (!TrySetStringOrBacking(elem, "ComposedKey", ck))
                {
                    // Some schemas have nested { ComposedKey { Key:string } }
                    if (!TrySetComposedKey(elem, ck)) { }
                }

                // Weight / RelativeProbability
                TrySetWeight(elem, (float)w);

                list.Add(elem);
            }

            return list.Count > 0;
        }

        private static void PrepareForcedOrAlwaysSpawnWeightedRoot(Spawnpoint sp, object template, bool isForcedSpawn)
        {
            if (sp?.Template == null || template == null)
            {
                return;
            }

            if (!isForcedSpawn && sp.Template.IsAlwaysSpawn != true)
            {
                return;
            }

            if (!TryReadDistributionWeights(sp, template, out var weightedKeys) || weightedKeys.Count <= 1)
            {
                return;
            }

            var chosenComposedKey = ChooseWeightedComposedKey(weightedKeys);
            if (string.IsNullOrWhiteSpace(chosenComposedKey))
            {
                return;
            }

            if (!TryGetEnumerableMember(template, new[] { "Items", "items" }, out var itemsMember, out var elemType))
            {
                return;
            }

            var items = (GetMemberValue(template, itemsMember) as IEnumerable)?
                .Cast<object>()
                .Where(item => item != null)
                .ToList();
            if (items == null || items.Count <= 1)
            {
                return;
            }

            var chosenRoot = items.FirstOrDefault(item =>
                string.Equals(ReadCkFromElem(item), chosenComposedKey, StringComparison.Ordinal));
            if (chosenRoot == null)
            {
                return;
            }

            var reordered = new List<object> { chosenRoot };
            reordered.AddRange(items.Where(item => !ReferenceEquals(item, chosenRoot)));
            if (!TryAssignItemsBack(template, itemsMember, elemType, reordered))
            {
                return;
            }

            var rootId = ReadMemberAsString(chosenRoot, "Id");
            if (!string.IsNullOrWhiteSpace(rootId) &&
                !TrySetMongoIdOrString(template, "Root", rootId))
            {
                TrySetStringOrBacking(template, "Root", rootId);
            }
        }

        private static bool TryReadDistributionWeights(object sp, object template, out List<(string ck, double weight)> weightedKeys)
        {
            weightedKeys = new List<(string ck, double weight)>();
            if (!TryReadDistributionWeightsFromHolder(sp, weightedKeys) && template != null)
            {
                TryReadDistributionWeightsFromHolder(template, weightedKeys);
            }

            weightedKeys = weightedKeys
                .Where(item => !string.IsNullOrWhiteSpace(item.ck) && item.weight > 0d)
                .ToList();
            return weightedKeys.Count > 0;
        }

        private static bool TryReadDistributionWeightsFromHolder(object holder, List<(string ck, double weight)> weightedKeys)
        {
            if (holder == null || weightedKeys == null)
            {
                return false;
            }

            if (!TryFindDistributionMember(holder, out var member, out _, out _))
            {
                return false;
            }

            var list = GetMemberValue(holder, member) as IEnumerable;
            if (list == null)
            {
                return false;
            }

            foreach (var elem in list)
            {
                var ck = ReadCkFromElem(elem);
                if (string.IsNullOrWhiteSpace(ck))
                {
                    continue;
                }

                weightedKeys.Add((ck, Math.Max(0d, ReadWeightFromDistribution(elem))));
            }

            return weightedKeys.Count > 0;
        }

        private static string ChooseWeightedComposedKey(List<(string ck, double weight)> weightedKeys)
        {
            if (weightedKeys == null || weightedKeys.Count == 0)
            {
                return string.Empty;
            }

            var total = weightedKeys.Sum(item => Math.Max(0d, item.weight));
            if (total <= 0d)
            {
                return weightedKeys[0].ck;
            }

            double roll;
            lock (ForcedSpawnRootRandomLock)
            {
                roll = ForcedSpawnRootRandom.NextDouble() * total;
            }

            var accumulated = 0d;
            foreach (var (ck, weight) in weightedKeys)
            {
                accumulated += Math.Max(0d, weight);
                if (roll <= accumulated)
                {
                    return ck;
                }
            }

            return weightedKeys[weightedKeys.Count - 1].ck;
        }

        private static double ReadWeightFromDistribution(object elem)
        {
            var names = new[] { "RelativeProbability", "Probability", "RelativeWeight", "Weight", "Chance" };
            foreach (var name in names)
            {
                if (TryReadNumericMember(elem, name, out var value))
                {
                    return value;
                }
            }

            return 1d;
        }

        // ==== Fallback: single-element override (spawnpoint-aware) ==============
        private static bool TryWriteFirstItem(Spawnpoint sp, object template, UleSpawnOverride ov, out string msg)
        {
            msg = string.Empty;

            if (!TryGetEnumerableMember(template, new[] { "Items", "items" }, out var itemsMember, out var elemType))
            { msg = "no Items enumerable"; return false; }

            var itemsObj = GetMemberValue(template, itemsMember);
            if (itemsObj == null) { msg = "Items is null"; return false; }

            var firstItem = FirstItemFromOverride(ov);
            if (firstItem == null || string.IsNullOrWhiteSpace(firstItem.Tpl)) { msg = "no JSON items"; return false; }
            if (IsMissingTemplate(firstItem.Tpl)) { msg = "first item template is missing"; return false; }

            // Materialize and ensure at least one element exists
            var material = new List<object>();
            foreach (var e in (IEnumerable)itemsObj) material.Add(e);
            object targetElem;
            if (material.Count == 0) { targetElem = Activator.CreateInstance(elemType)!; material.Add(targetElem); }
            else { targetElem = material[0]!; }

            // Try to use an existing CK from the spawnpoint's distribution
            string resolvedCk;
            if (TryGetSpawnDistributionKeys(sp, out var keys) && keys.Count > 0)
            {
                resolvedCk = keys[0];
            }
            else
            {
                // No dist on spawnpoint: give this item its own CK and create a single-entry dist on the spawnpoint
                resolvedCk = !string.IsNullOrWhiteSpace(firstItem.ComposedKey)
                    ? firstItem.ComposedKey
                    : $"ULE:{Guid.NewGuid():N}:0";
            }

            string rootId;
            string reason;
            if (!TryInitializeLootItem(
                    targetElem,
                    firstItem.Tpl,
                    resolvedCk,
                    0,
                    FirstStackCountFromOverride(ov),
                    parentId: null,
                    slotId: firstItem.SlotId,
                    locationJson: firstItem.LocationJson,
                    updJson: firstItem.UpdJson,
                    assignComposedKey: true,
                    out resolvedCk,
                    out rootId,
                    out reason))
            {
                msg = reason;
                DumpElementMembersOnce(elemType, firstItem.Tpl);
                return false;
            }

            // Give the element a weight/probability of 1 so it is self-sufficient if no distribution exists
            var weightNames = new[] { "RelativeProbability", "Probability", "RelativeWeight", "Weight", "Chance" };
            SetNumericIfExists(targetElem, weightNames, 1f);

            // Collapse to only the mutated element
            if (!TryAssignItemsBack(template, itemsMember, elemType, new List<object> { targetElem }))
            { msg = "cannot assign updated Items back to template"; return false; }

            if (!TrySetStringOrBacking(template, "Root", rootId))
            {
                msg = "cannot assign template Root in fallback";
                return false;
            }

            if (TryGetSpawnDistributionKeys(sp, out keys) && keys.Count > 0)
            {
                msg = $"fallback wrote tpl='{firstItem.Tpl}' and aligned CK='{resolvedCk}' (collapsed Items to 1 + kept spawn dist)";
            }
            else
            {
                if (!TryEnsureSingleEntrySpawnDistribution(sp, resolvedCk, 1f, out string why))
                {
                    msg = $"fallback wrote tpl='{firstItem.Tpl}' but could not create spawn dist: {why}";
                    if (why == "no spawnpoint distribution holder")
                    {
                        var label = sp?.Template?.Id ?? "unknown";
                        DumpSpawnpointMembersOnce(sp, label);
                    }
                }
                else
                {
                    msg = $"fallback wrote tpl='{firstItem.Tpl}' and created single-entry spawn dist CK='{resolvedCk}' (collapsed Items to 1)";
                }
            }

            ApplySpawnChance(sp, template, ov);
            ApplySpawnTransform(sp, template, ov);
            return true;
        }

        private static void ApplySpawnChance(Spawnpoint sp, object template, UleSpawnOverride ov)
        {
            try
            {
                var chance = 0f;
                try
                {
                    chance = Convert.ToSingle(ov?.SpawnChance ?? 0d);
                }
                catch { }

                chance = Math.Max(0f, Math.Min(1f, chance));
                var alwaysSpawn = ov?.IsAlwaysSpawn ?? chance >= 0.999f;
                var names = new[] { "RelativeProbability", "Probability", "RelativeWeight", "Weight", "Chance" };
                if (sp != null)
                {
                    SetNumericIfExists(sp, names, chance);
                    TrySetNullableBoolOrBacking(sp, "IsAlwaysSpawn", alwaysSpawn);
                }

                if (template != null)
                {
                    SetNumericIfExists(template, names, chance);
                    TrySetNullableBoolOrBacking(template, "IsAlwaysSpawn", alwaysSpawn);
                }
            }
            catch { }
        }

        private static void ApplySpawnTransform(Spawnpoint sp, object template, UleSpawnOverride ov)
        {
            if (ov == null)
            {
                return;
            }

            try
            {
                if (ov.Position != null)
                {
                    TrySetVectorMember(sp, "Position", ov.Position);
                    TrySetVectorMember(template, "Position", ov.Position);
                }

                if (ov.Rotation != null)
                {
                    TrySetVectorMember(sp, "Rotation", ov.Rotation);
                    TrySetVectorMember(template, "Rotation", ov.Rotation);
                }

                if (ov.UseGravity.HasValue)
                {
                    TrySetNullableBoolOrBacking(sp, "UseGravity", ov.UseGravity.Value);
                    TrySetNullableBoolOrBacking(template, "UseGravity", ov.UseGravity.Value);
                }
            }
            catch { }
        }

        private static bool TrySetVectorMember(object target, string name, UleVector3 value)
        {
            if (target == null || string.IsNullOrWhiteSpace(name) || value == null)
            {
                return false;
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            var property = target.GetType().GetProperty(name, flags);
            if (property?.CanWrite == true && TryConvertVector(property.PropertyType, value, out var propertyValue))
            {
                try
                {
                    property.SetValue(target, propertyValue);
                    return true;
                }
                catch { }
            }

            var field = target.GetType().GetField(name, flags);
            if (field != null && !field.IsInitOnly && TryConvertVector(field.FieldType, value, out var fieldValue))
            {
                try
                {
                    field.SetValue(target, fieldValue);
                    return true;
                }
                catch { }
            }

            var backingField = target.GetType().GetField($"<{name}>k__BackingField", flags);
            if (backingField != null && !backingField.IsInitOnly && TryConvertVector(backingField.FieldType, value, out var backingValue))
            {
                try
                {
                    backingField.SetValue(target, backingValue);
                    return true;
                }
                catch { }
            }

            return false;
        }

        private static bool TryConvertVector(Type targetType, UleVector3 value, out object converted)
        {
            converted = null;
            if (targetType == null || value == null)
            {
                return false;
            }

            var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            var x = Convert.ToSingle(value.X);
            var y = Convert.ToSingle(value.Y);
            var z = Convert.ToSingle(value.Z);
            var serverVector = new SPTarkov.Server.Core.Models.Eft.Common.Vector3(x, y, z);

            if (effectiveType.IsInstanceOfType(serverVector))
            {
                converted = serverVector;
                return true;
            }

            foreach (var parameterType in new[] { typeof(float), typeof(double) })
            {
                var ctor = effectiveType.GetConstructor(new[] { parameterType, parameterType, parameterType });
                if (ctor == null)
                {
                    continue;
                }

                try
                {
                    converted = ctor.Invoke(new[]
                    {
                        Convert.ChangeType(value.X, parameterType),
                        Convert.ChangeType(value.Y, parameterType),
                        Convert.ChangeType(value.Z, parameterType)
                    });
                    return true;
                }
                catch { }
            }

            try
            {
                converted = Activator.CreateInstance(effectiveType);
                if (converted == null)
                {
                    return false;
                }

                SetNumericIfExists(converted, new[] { "X", "x" }, x);
                SetNumericIfExists(converted, new[] { "Y", "y" }, y);
                SetNumericIfExists(converted, new[] { "Z", "z" }, z);
                return true;
            }
            catch
            {
                converted = null;
                return false;
            }
        }

        private static bool IsMissingTemplate(string tpl)
        {
            return RuntimeTemplateIndex.HasTemplateData && !RuntimeTemplateIndex.ContainsTemplate(tpl);
        }

        private static string DescribeItem(UleItemNode item)
        {
            if (item == null)
            {
                return "unknown item";
            }

            var fallback = item is UleItemEntry entry ? entry.PresetName : null;
            return RuntimeTemplateIndex.DescribeTemplate(item.Tpl, fallback);
        }

        private static void LogMissingItem(string locationId, string spawnLabel, string tpl, string lastKnownItem)
        {
            var mapName = MapCatalog.TryGetById(locationId, out var descriptor)
                ? descriptor.DisplayName
                : locationId;

            Logger.Warn(
                $"[ULE] Missing item while applying {mapName} loot spawn changes:{Environment.NewLine}" +
                $"Spawn: {spawnLabel}{Environment.NewLine}" +
                $"Template: {tpl}{Environment.NewLine}" +
                $"Last known item: {(string.IsNullOrWhiteSpace(lastKnownItem) ? "unknown item" : lastKnownItem)}{Environment.NewLine}" +
                "Skipped this item due to missing mod.");
        }

        // === Spawnpoint distribution helpers ===================================
        private static bool TryFindDistributionMember(object holder, out MemberInfo member, out Type listType, out Type elemType)
        {
            member = null; listType = null; elemType = null;

            // 1) Known names first
            if (TryGetListMember(holder, new[] { "ItemDistribution", "itemDistribution", "ItemsDistribution", "itemsDistribution", "Distribution", "distribution" }, out member, out listType, out elemType))
                return true;

            // 2) Fallback: search for any IList whose element has ComposedKey and a weight-ish field
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var m in holder.GetType().GetMembers(flags))
            {
                Type t;
                if (m is PropertyInfo pi) t = pi.PropertyType;
                else if (m is FieldInfo fi) t = fi.FieldType;
                else continue;

                if (!typeof(IList).IsAssignableFrom(t) || !t.IsGenericType) continue;
                var eType = t.GetGenericArguments()[0];

                if (ElemLooksLikeDistribution(eType))
                {
                    member = m; listType = t; elemType = eType;
                    return true;
                }
            }

            return false;
        }

        private static bool ElemLooksLikeDistribution(Type elemType)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            bool hasCk = elemType.GetProperty("ComposedKey", flags) != null
                         || elemType.GetField("ComposedKey", flags) != null
                         || elemType.GetProperty("Key", flags) != null
                         || elemType.GetField("Key", flags) != null;

            var weightNames = new[] { "RelativeProbability", "Probability", "RelativeWeight", "Weight", "Chance" };
            bool hasWeight = weightNames.Any(n => elemType.GetProperty(n, flags) != null || elemType.GetField(n, flags) != null);

            return hasCk && hasWeight;
        }

        private static bool TryGetSpawnDistributionKeys(Spawnpoint sp, out List<string> keys)
        {
            keys = new List<string>();
            if (!TryFindDistributionMember(sp, out var member, out _, out _)) return false;
            var list = GetMemberValue(sp, member) as IEnumerable;
            if (list == null) return false;

            foreach (var e in list)
            {
                var ck = ReadCkFromElem(e);
                if (!string.IsNullOrWhiteSpace(ck)) keys.Add(ck);
            }
            return keys.Count > 0;
        }

        private static bool TryEnsureSingleEntrySpawnDistribution(Spawnpoint sp, string ck, float weight, out string why)
        {
            why = string.Empty;
            if (!TryFindDistributionMember(sp, out var member, out var listType, out var elemType))
            { why = "no spawnpoint distribution holder"; return false; }

            var list = GetMemberValue(sp, member) as IList;
            if (list == null)
            {
                list = CreateListInstance(listType, elemType) as IList;
                if (list == null) { why = "cannot instantiate distribution list"; return false; }
                if (!TrySetMemberValue(sp, member, list)) { why = "cannot assign distribution list"; return false; }
            }

            list.Clear();
            var elem = Activator.CreateInstance(elemType);
            if (elem == null) { why = "cannot create distribution element"; return false; }

            if (!TrySetStringOrBacking(elem, "ComposedKey", ck))
            {
                if (!TrySetComposedKey(elem, ck)) { why = "cannot set ComposedKey on dist elem"; return false; }
            }
            TrySetWeight(elem, weight);
            list.Add(elem);
            return true;
        }

        private static void AddToSpawnpointsForced(object dynamicLootDist, Spawnpoint sp)
        {
            if (dynamicLootDist == null || sp == null) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

            // Get property/field
            var p = dynamicLootDist.GetType().GetProperty("SpawnpointsForced", flags);
            var f = dynamicLootDist.GetType().GetField("SpawnpointsForced", flags);

            object listObj = null;
            try { if (p != null && p.CanRead) listObj = p.GetValue(dynamicLootDist); } catch { }
            if (listObj == null)
            {
                try { if (f != null) listObj = f.GetValue(dynamicLootDist); } catch { }
            }

            // Ensure a writable IList
            IList list = listObj as IList;
            if (list == null)
            {
                try
                {
                    var listType = typeof(List<>).MakeGenericType(sp.GetType());
                    list = Activator.CreateInstance(listType) as IList;
                    if (p != null && p.CanWrite) p.SetValue(dynamicLootDist, list);
                    else if (f != null && !f.IsInitOnly) f.SetValue(dynamicLootDist, list);
                }
                catch { }
            }
            if (list == null) return;

            // Avoid duplicates (by reference)
            foreach (var x in list) if (ReferenceEquals(x, sp)) return;
            list.Add(sp);
        }

        private static bool AddToSpawnpointList(object dynamicLootDist, string memberName, Spawnpoint sp)
        {
            if (dynamicLootDist == null || sp == null || string.IsNullOrWhiteSpace(memberName))
            {
                return false;
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            var member = (MemberInfo)dynamicLootDist.GetType().GetProperty(memberName, flags)
                         ?? dynamicLootDist.GetType().GetField(memberName, flags);
            if (member == null)
            {
                return false;
            }

            var existing = GetMemberValue(dynamicLootDist, member) as IEnumerable;
            var list = new List<Spawnpoint>();
            if (existing != null)
            {
                foreach (var item in existing)
                {
                    if (item is Spawnpoint existingSpawn)
                    {
                        var existingLabel = existingSpawn.Template?.Id ?? existingSpawn.LocationId ?? string.Empty;
                        var newLabel = sp.Template?.Id ?? sp.LocationId ?? string.Empty;
                        if (ReferenceEquals(existingSpawn, sp) ||
                            string.Equals(existingLabel, newLabel, StringComparison.Ordinal))
                        {
                            return true;
                        }

                        list.Add(existingSpawn);
                    }
                }
            }

            list.Add(sp);
            return TrySetMemberValue(dynamicLootDist, member, list);
        }

        // Read CK from any element shape
        private static string ReadCkFromElem(object elem)
        {
            var s = ReadStringMember(elem, "ComposedKey");
            if (!string.IsNullOrWhiteSpace(s)) return s;
            var k = ReadStringMember(elem, "Key");
            if (!string.IsNullOrWhiteSpace(k)) return k;

            // nested ComposedKey { Key }
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            var ckObj = elem.GetType().GetProperty("ComposedKey", flags)?.GetValue(elem)
                       ?? elem.GetType().GetField("ComposedKey", flags)?.GetValue(elem);
            if (ckObj != null)
            {
                var inner = ckObj.GetType().GetProperty("Key", flags)?.GetValue(ckObj) as string
                         ?? ckObj.GetType().GetField("Key", flags)?.GetValue(ckObj) as string;
                if (!string.IsNullOrWhiteSpace(inner)) return inner;
            }
            return string.Empty;
        }

        // === Reflection helpers ===============================================
        private static bool TryGetEnumerableMember(object obj, IEnumerable<string> names, out MemberInfo member, out Type elemType)
        {
            member = null; elemType = null;
            if (TryGetListMember(obj, names, out var m, out var listType, out var eType))
            { member = m; elemType = eType; return true; }
            return false;
        }

        private static bool TryGetListMember(object obj, IEnumerable<string> names, out MemberInfo member, out Type listType, out Type elemType)
        {
            member = null; listType = null; elemType = null;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

            foreach (var n in names)
            {
                var p = obj.GetType().GetProperty(n, flags);
                if (p != null && typeof(IEnumerable).IsAssignableFrom(p.PropertyType))
                {
                    if (p.PropertyType.IsGenericType)
                    {
                        member = p; listType = p.PropertyType; elemType = p.PropertyType.GetGenericArguments()[0];
                        return true;
                    }
                }
                var f = obj.GetType().GetField(n, flags);
                if (f != null && typeof(IEnumerable).IsAssignableFrom(f.FieldType))
                {
                    if (f.FieldType.IsGenericType)
                    {
                        member = f; listType = f.FieldType; elemType = f.FieldType.GetGenericArguments()[0];
                        return true;
                    }
                }
            }

            return false;
        }

        private static object CreateListInstance(Type listType, Type elemType)
        {
            try
            {
                if (!listType.IsInterface && Activator.CreateInstance(listType) is object inst) return inst;
            }
            catch { }
            try
            {
                var concrete = typeof(List<>).MakeGenericType(elemType);
                return Activator.CreateInstance(concrete);
            }
            catch { }
            return null;
        }

        private static object GetMemberValue(object obj, MemberInfo m)
        {
            try { if (m is PropertyInfo pi) return pi.GetValue(obj); if (m is FieldInfo fi) return fi.GetValue(obj); }
            catch { }
            return null;
        }

        private static bool TryAssignItemsBack(object template, MemberInfo itemsMember, Type elemType, List<object> elems)
        {
            try
            {
                if (itemsMember is PropertyInfo pi)
                {
                    if (pi.PropertyType.IsAssignableFrom(elems.GetType())) { pi.SetValue(template, elems); return true; }
                    var concrete = typeof(List<>).MakeGenericType(elemType);
                    var inst = Activator.CreateInstance(concrete) as IList;
                    foreach (var e in elems) inst.Add(e);
                    pi.SetValue(template, inst);
                    return true;
                }
                else if (itemsMember is FieldInfo fi)
                {
                    if (fi.FieldType.IsAssignableFrom(elems.GetType())) { fi.SetValue(template, elems); return true; }
                    var concrete = typeof(List<>).MakeGenericType(elemType);
                    var inst = Activator.CreateInstance(concrete) as IList;
                    foreach (var e in elems) inst.Add(e);
                    fi.SetValue(template, inst);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static string ReadStringMember(object obj, string name)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            try
            {
                var p = obj.GetType().GetProperty(name, flags);
                if (p != null && p.CanRead && p.PropertyType == typeof(string))
                {
                    var v = p.GetValue(obj) as string; if (!string.IsNullOrWhiteSpace(v)) return v;
                }
                var f = obj.GetType().GetField(name, flags);
                if (f != null && f.FieldType == typeof(string))
                {
                    var v = f.GetValue(obj) as string; if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            catch { }
            return string.Empty;
        }

        private static string ReadMemberAsString(object obj, string name)
        {
            if (obj == null || string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            foreach (var memberName in new[] { name, $"<{name}>k__BackingField" })
            {
                try
                {
                    var p = obj.GetType().GetProperty(memberName, flags);
                    if (p != null && p.CanRead)
                    {
                        var value = p.GetValue(obj);
                        var text = value?.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }

                    var f = obj.GetType().GetField(memberName, flags);
                    if (f != null)
                    {
                        var value = f.GetValue(obj);
                        var text = value?.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }
                }
                catch { }
            }

            return string.Empty;
        }

        private static bool TryReadNumericMember(object obj, string name, out double value)
        {
            value = 0d;
            if (obj == null || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            foreach (var memberName in new[] { name, $"<{name}>k__BackingField" })
            {
                try
                {
                    var p = obj.GetType().GetProperty(memberName, flags);
                    if (p != null && p.CanRead && TryConvertToDouble(p.GetValue(obj), out value))
                    {
                        return true;
                    }

                    var f = obj.GetType().GetField(memberName, flags);
                    if (f != null && TryConvertToDouble(f.GetValue(obj), out value))
                    {
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private static bool TryConvertToDouble(object raw, out double value)
        {
            value = 0d;
            if (raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToDouble(raw);
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetStringOrBacking(object target, string name, string value)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

            var p = target.GetType().GetProperty(name, flags);
            if (p != null)
            {
                try
                {
                    if (p.CanWrite && p.PropertyType == typeof(string)) { p.SetValue(target, value); return true; }
                    if (p.CanWrite && !p.PropertyType.IsValueType) { p.SetValue(target, value); return true; }
                }
                catch { }
            }

            var f = target.GetType().GetField(name, flags);
            if (f != null)
            {
                try
                {
                    if (!f.IsInitOnly && f.FieldType == typeof(string)) { f.SetValue(target, value); return true; }
                    if (!f.IsInitOnly && !f.FieldType.IsValueType) { f.SetValue(target, value); return true; }
                }
                catch { }
            }

            var bf = target.GetType().GetField($"<{name}>k__BackingField", flags);
            if (bf != null)
            {
                try
                {
                    if (bf.FieldType == typeof(string)) { bf.SetValue(target, value); return true; }
                    if (!bf.FieldType.IsValueType) { bf.SetValue(target, value); return true; }
                }
                catch { }
            }
            return false;
        }

        private static bool TrySetComposedKey(object elem, string ck)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

            // Has a nested object with Key field/property
            var ckObj = elem.GetType().GetProperty("ComposedKey", flags)?.GetValue(elem)
                       ?? elem.GetType().GetField("ComposedKey", flags)?.GetValue(elem);
            if (ckObj == null)
            {
                try
                {
                    var t = elem.GetType().GetProperty("ComposedKey", flags)?.PropertyType
                          ?? elem.GetType().GetField("ComposedKey", flags)?.FieldType;
                    if (t != null)
                    {
                        ckObj = Activator.CreateInstance(t);
                        if (ckObj != null)
                        {
                            if (elem.GetType().GetProperty("ComposedKey", flags) is PropertyInfo pi) pi.SetValue(elem, ckObj);
                            else if (elem.GetType().GetField("ComposedKey", flags) is FieldInfo fi) fi.SetValue(elem, ckObj);
                        }
                    }
                }
                catch { }
            }

            if (ckObj != null)
            {
                var set = false;
                try
                {
                    var p = ckObj.GetType().GetProperty("Key", flags);
                    if (p != null && p.CanWrite && p.PropertyType == typeof(string)) { p.SetValue(ckObj, ck); set = true; }
                    var f = ckObj.GetType().GetField("Key", flags);
                    if (!set && f != null && f.FieldType == typeof(string)) { f.SetValue(ckObj, ck); set = true; }
                    var bf = ckObj.GetType().GetField("<Key>k__BackingField", flags);
                    if (!set && bf != null && bf.FieldType == typeof(string)) { bf.SetValue(ckObj, ck); set = true; }
                }
                catch { }
                return set;
            }
            return false;
        }

        private static bool TryInitializeLootItem(
            object elem,
            string tpl,
            string requestedComposedKey,
            int ordinal,
            int? stackCount,
            string parentId,
            string slotId,
            string locationJson,
            string updJson,
            bool assignComposedKey,
            out string composedKey,
            out string itemId,
            out string reason)
        {
            composedKey = assignComposedKey
                ? (string.IsNullOrWhiteSpace(requestedComposedKey)
                    ? $"ULE:{Guid.NewGuid():N}:{ordinal}"
                    : requestedComposedKey)
                : string.Empty;
            itemId = GenerateMongoIdString();
            reason = string.Empty;

            if (!TrySetMongoIdOrString(elem, "Template", tpl) &&
                !TrySetMongoIdOrString(elem, "Tpl", tpl) &&
                !TrySetMongoIdOrString(elem, "TemplateId", tpl))
            {
                string boundPath;
                if (!TryBindTemplateDeep(elem, tpl, out boundPath))
                {
                    reason = "cannot bind Template/Tpl/TemplateId";
                    return false;
                }
            }

            if (assignComposedKey &&
                !TrySetStringOrBacking(elem, "ComposedKey", composedKey) &&
                !TrySetComposedKey(elem, composedKey))
            {
                reason = "cannot assign ComposedKey";
                return false;
            }

            if (!TrySetMongoIdOrString(elem, "Id", itemId))
            {
                reason = "cannot assign loot item Id";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(parentId) &&
                !TrySetMongoIdOrString(elem, "ParentId", parentId) &&
                !TrySetStringOrBacking(elem, "ParentId", parentId))
            {
                reason = "cannot assign ParentId";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(slotId) &&
                !TrySetStringOrBacking(elem, "SlotId", slotId))
            {
                reason = "cannot assign SlotId";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(locationJson) &&
                !TrySetJsonMember(elem, "Location", locationJson))
            {
                reason = "cannot assign Location";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(updJson))
            {
                if (!TrySetJsonMember(elem, "Upd", updJson))
                {
                    if (stackCount.HasValue)
                    {
                        TryWriteLootItemUpd(elem, stackCount.Value);
                    }
                }
            }
            else
            {
                TryWriteLootItemUpd(elem, stackCount.GetValueOrDefault(1));
            }

            return true;
        }

        private static void TryWriteLootItemUpd(object elem, int stackCount)
        {
            if (elem == null)
            {
                return;
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            MemberInfo updMember = null;
            Type updType = null;
            object upd = null;

            var prop = elem.GetType().GetProperty("Upd", flags);
            if (prop != null)
            {
                updMember = prop;
                updType = prop.PropertyType;
                try { upd = prop.GetValue(elem); } catch { }
            }
            else
            {
                var field = elem.GetType().GetField("Upd", flags);
                if (field != null)
                {
                    updMember = field;
                    updType = field.FieldType;
                    try { upd = field.GetValue(elem); } catch { }
                }
            }

            if (updType == null)
            {
                return;
            }

            if (upd == null)
            {
                try { upd = Activator.CreateInstance(updType); } catch { }
                if (upd == null)
                {
                    return;
                }

                if (!TrySetMemberValue(elem, updMember, upd))
                {
                    return;
                }
            }

            SetNumericIfExists(upd, new[] { "StackObjectsCount" }, Math.Max(1, stackCount));
        }

        private static int? ReadPreferredStackCount(object entry)
        {
            if (entry == null)
            {
                return null;
            }

            var stackMax = ReadNullableInt(entry, "StackMax");
            var stackMin = ReadNullableInt(entry, "StackMin");
            var value = stackMax ?? stackMin;
            if (!value.HasValue || value.Value <= 0)
            {
                var updJson = ReadNullableString(entry, "UpdJson");
                return ReadStackCountFromJson(updJson);
            }

            return value.Value;
        }

        private static int? FirstStackCountFromOverride(UleSpawnOverride ov)
        {
            foreach (var it in ov?.Items ?? Enumerable.Empty<UleItemEntry>())
            {
                var stackCount = ReadPreferredStackCount(it);
                if (stackCount.HasValue)
                {
                    return stackCount.Value;
                }
            }

            return null;
        }

        private static UleItemEntry FirstItemFromOverride(UleSpawnOverride ov)
        {
            return ov?.Items?.FirstOrDefault(item => item != null && !string.IsNullOrWhiteSpace(item.Tpl));
        }

        private static int? ReadNullableInt(object target, string name)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

            try
            {
                var p = target.GetType().GetProperty(name, flags);
                if (p != null && p.CanRead)
                {
                    var value = p.GetValue(target);
                    if (value != null)
                    {
                        return Convert.ToInt32(value);
                    }
                }

                var f = target.GetType().GetField(name, flags);
                if (f != null)
                {
                    var value = f.GetValue(target);
                    if (value != null)
                    {
                        return Convert.ToInt32(value);
                    }
                }
            }
            catch { }

            return null;
        }

        private static string ReadNullableString(object target, string name)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

            try
            {
                var p = target.GetType().GetProperty(name, flags);
                if (p != null && p.CanRead)
                {
                    return p.GetValue(target) as string;
                }

                var f = target.GetType().GetField(name, flags);
                if (f != null)
                {
                    return f.GetValue(target) as string;
                }
            }
            catch { }

            return null;
        }

        private static int? ReadStackCountFromJson(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (!doc.RootElement.TryGetProperty("StackObjectsCount", out var stackToken))
                {
                    return null;
                }

                if (stackToken.ValueKind == JsonValueKind.Number && stackToken.TryGetInt32(out var value))
                {
                    return value > 0 ? value : null;
                }

                if (stackToken.ValueKind == JsonValueKind.String && int.TryParse(stackToken.GetString(), out value))
                {
                    return value > 0 ? value : null;
                }
            }
            catch { }

            return null;
        }

        private static string GenerateMongoIdString()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 24);
        }

        private static object ConvertToMongoIdIfPossible(Type targetType, string value)
        {
            if (targetType == null)
            {
                return null;
            }

            var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (effectiveType == typeof(string))
            {
                return value;
            }

            try
            {
                var parse = effectiveType.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "Parse"
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType == typeof(string));
                if (parse != null) return parse.Invoke(null, new object[] { value });

                var impl = effectiveType.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => (m.Name == "op_Implicit" || m.Name == "op_Explicit")
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType == typeof(string)
                        && m.ReturnType == effectiveType);
                if (impl != null) return impl.Invoke(null, new object[] { value });

                var ctor = effectiveType.GetConstructor(new[] { typeof(string) });
                if (ctor != null) return ctor.Invoke(new object[] { value });
            }
            catch { }

            return null;
        }

        private static bool TrySetMongoIdOrString(object target, string name, string value)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

            var p = target.GetType().GetProperty(name, flags);
            if (p != null)
            {
                try
                {
                    if (p.CanWrite && p.PropertyType == typeof(string)) { p.SetValue(target, value); return true; }
                    if (p.CanWrite)
                    {
                        var converted = ConvertToMongoIdIfPossible(p.PropertyType, value);
                        if (converted != null) { p.SetValue(target, converted); return true; }
                    }
                }
                catch { }
            }

            var f = target.GetType().GetField(name, flags);
            if (f != null)
            {
                try
                {
                    if (!f.IsInitOnly && f.FieldType == typeof(string)) { f.SetValue(target, value); return true; }
                    if (!f.IsInitOnly)
                    {
                        var converted = ConvertToMongoIdIfPossible(f.FieldType, value);
                        if (converted != null) { f.SetValue(target, converted); return true; }
                    }
                }
                catch { }
            }

            var bf = target.GetType().GetField($"<{name}>k__BackingField", flags);
            if (bf != null)
            {
                try
                {
                    if (bf.FieldType == typeof(string)) { bf.SetValue(target, value); return true; }
                    if (!bf.IsInitOnly)
                    {
                        var converted = ConvertToMongoIdIfPossible(bf.FieldType, value);
                        if (converted != null) { bf.SetValue(target, converted); return true; }
                    }
                }
                catch { }
            }
            return false;
        }

        private static bool TrySetMemberValue(object obj, MemberInfo m, object v)
        {
            try { if (m is PropertyInfo pi) { pi.SetValue(obj, v); return true; } if (m is FieldInfo fi) { fi.SetValue(obj, v); return true; } }
            catch { }
            return false;
        }

        private static readonly JsonSerializerOptions JsonMemberOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private static bool TrySetJsonMember(object target, string name, string rawJson)
        {
            if (target == null || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rawJson))
            {
                return false;
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

            var p = target.GetType().GetProperty(name, flags);
            if (p != null && p.CanWrite)
            {
                try
                {
                    if (TryDeserializeJson(rawJson, p.PropertyType, out var value))
                    {
                        p.SetValue(target, value);
                        return true;
                    }
                }
                catch { }
            }

            var f = target.GetType().GetField(name, flags);
            if (f != null && !f.IsInitOnly)
            {
                try
                {
                    if (TryDeserializeJson(rawJson, f.FieldType, out var value))
                    {
                        f.SetValue(target, value);
                        return true;
                    }
                }
                catch { }
            }

            var bf = target.GetType().GetField($"<{name}>k__BackingField", flags);
            if (bf != null && !bf.IsInitOnly)
            {
                try
                {
                    if (TryDeserializeJson(rawJson, bf.FieldType, out var value))
                    {
                        bf.SetValue(target, value);
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private static bool TryDeserializeJson(string rawJson, Type targetType, out object value)
        {
            value = null;
            if (targetType == null || string.IsNullOrWhiteSpace(rawJson))
            {
                return false;
            }

            try
            {
                if ((Nullable.GetUnderlyingType(targetType) ?? targetType) == typeof(string))
                {
                    using var doc = JsonDocument.Parse(rawJson);
                    value = doc.RootElement.ValueKind == JsonValueKind.String
                        ? doc.RootElement.GetString()
                        : rawJson;
                    return true;
                }

                value = JsonSerializer.Deserialize(rawJson, targetType, JsonMemberOptions);
                return value != null || !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        private static void TrySetWeight(object distElem, float w)
        {
            var names = new[] { "RelativeProbability", "Probability", "RelativeWeight", "Weight", "Chance" };
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            foreach (var n in names)
            {
                var p = distElem.GetType().GetProperty(n, flags);
                if (p?.CanWrite == true)
                {
                    try
                    {
                        var converted = ConvertToNumericIfPossible(p.PropertyType, w);
                        if (converted != null) { p.SetValue(distElem, converted); return; }
                    }
                    catch { }
                }
                var f = distElem.GetType().GetField(n, flags);
                if (f != null)
                {
                    try
                    {
                        var converted = ConvertToNumericIfPossible(f.FieldType, w);
                        if (converted != null) { f.SetValue(distElem, converted); return; }
                    }
                    catch { }
                }
            }
        }

        private static void SetNumericIfExists(object target, IEnumerable<string> names, float value)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

            foreach (var n in names)
            {
                // Property
                var p = target.GetType().GetProperty(n, flags);
                if (p != null)
                {
                    try
                    {
                        var converted = ConvertToNumericIfPossible(p.PropertyType, value >= 1f && (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) == typeof(int) ? 1f : value);
                        if (converted != null) { p.SetValue(target, converted); continue; }
                    }
                    catch { }
                }

                // Field
                var f = target.GetType().GetField(n, flags);
                if (f != null)
                {
                    try
                    {
                        var converted = ConvertToNumericIfPossible(f.FieldType, value >= 1f && (Nullable.GetUnderlyingType(f.FieldType) ?? f.FieldType) == typeof(int) ? 1f : value);
                        if (converted != null) { f.SetValue(target, converted); continue; }
                    }
                    catch { }
                }

                // Backing field
                var bf = target.GetType().GetField($"<{n}>k__BackingField", flags);
                if (bf != null)
                {
                    try
                    {
                        var converted = ConvertToNumericIfPossible(bf.FieldType, value >= 1f && (Nullable.GetUnderlyingType(bf.FieldType) ?? bf.FieldType) == typeof(int) ? 1f : value);
                        if (converted != null) { bf.SetValue(target, converted); continue; }
                    }
                    catch { }
                }
            }
        }

        private static object ConvertToNumericIfPossible(Type targetType, float value)
        {
            if (targetType == null)
            {
                return null;
            }

            var nullableType = Nullable.GetUnderlyingType(targetType);
            var effectiveType = nullableType ?? targetType;

            object numericValue = null;
            if (effectiveType == typeof(float))
            {
                numericValue = value;
            }
            else if (effectiveType == typeof(double))
            {
                numericValue = (double)value;
            }
            else if (effectiveType == typeof(int))
            {
                numericValue = (int)Math.Round(value);
            }
            else if (effectiveType == typeof(long))
            {
                numericValue = (long)Math.Round(value);
            }

            if (numericValue == null)
            {
                return null;
            }

            if (nullableType != null)
            {
                try
                {
                    return Activator.CreateInstance(targetType, numericValue);
                }
                catch
                {
                    return null;
                }
            }

            return numericValue;
        }

        private static readonly HashSet<string> dumpedSpawnHolders = new();
        private static void DumpSpawnpointMembersOnce(object sp, string label)
        {
            if (!Logger.Debug || sp == null)
            {
                return;
            }

            var key = sp.GetType().FullName ?? sp.GetType().Name;
            if (!dumpedSpawnHolders.Add(key)) return;
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var props = string.Join(", ", sp.GetType().GetProperties(flags).Select(p => p.Name + ":" + p.PropertyType.Name));
                var fields = string.Join(", ", sp.GetType().GetFields(flags).Select(f => f.Name + ":" + f.FieldType.Name));
                Logger.Info($"[ULE][SPAWN MEMBERS] '{label}' type={sp.GetType().FullName} props: {props}");
                Logger.Info($"[ULE][SPAWN MEMBERS] '{label}' type={sp.GetType().FullName} fields: {fields}");
            }
            catch { }
        }

        private static readonly HashSet<string> dumpedElemTypes = new();
        private static void DumpElementMembersOnce(Type elemType, string tpl)
        {
            if (!Logger.Debug || elemType == null)
            {
                return;
            }

            var key = elemType.FullName ?? elemType.Name;
            if (!dumpedElemTypes.Add(key)) return;
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var props = string.Join(", ", elemType.GetProperties(flags).Select(p => p.Name + ":" + p.PropertyType.Name));
                var fields = string.Join(", ", elemType.GetFields(flags).Select(f => f.Name + ":" + f.FieldType.Name));
                Logger.Info($"[ULE][ELEM] elemType={elemType.FullName} tpl={tpl} props: {props}");
                Logger.Info($"[ULE][ELEM] elemType={elemType.FullName} tpl={tpl} fields: {fields}");
            }
            catch { }
        }

        private static IEnumerable<string> ReadMemberNames(Type t)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            foreach (var p in t.GetProperties(flags)) yield return p.Name;
            foreach (var f in t.GetFields(flags)) yield return f.Name;
        }

        private static bool TryBindTemplateDeep(object elem, string tpl, out string boundPath)
        {
            return TryBindTemplateDeep(elem, tpl, out boundPath, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
        }

        private static bool TryBindTemplateDeep(object elem, string tpl, out string boundPath, HashSet<object> visited, int depth)
        {
            boundPath = string.Empty;
            if (elem == null || depth > 4)
            {
                return false;
            }

            var elemType = elem.GetType();
            if (!elemType.IsValueType && !visited.Add(elem))
            {
                return false;
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

            foreach (var p in elemType.GetProperties(flags))
            {
                if (p.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (p.PropertyType == typeof(string) && p.CanWrite && p.Name.Equals("Tpl", StringComparison.OrdinalIgnoreCase))
                { p.SetValue(elem, tpl); boundPath = p.Name; return true; }
                if (p.PropertyType == typeof(string) && p.CanWrite && p.Name.Contains("Template", StringComparison.OrdinalIgnoreCase))
                { p.SetValue(elem, tpl); boundPath = p.Name; return true; }
                if (!p.CanRead || !ShouldRecurseInto(p.PropertyType))
                {
                    continue;
                }

                object child;
                try { child = p.GetValue(elem); }
                catch { continue; }

                if (child != null && TryBindTemplateDeep(child, tpl, out boundPath, visited, depth + 1))
                { boundPath = $"{p.Name}.{boundPath}"; return true; }
            }

            foreach (var f in elemType.GetFields(flags))
            {
                if (!f.IsInitOnly && f.FieldType == typeof(string) && f.Name.Equals("Tpl", StringComparison.OrdinalIgnoreCase))
                { f.SetValue(elem, tpl); boundPath = f.Name; return true; }
                if (!f.IsInitOnly && f.FieldType == typeof(string) && f.Name.Contains("Template", StringComparison.OrdinalIgnoreCase))
                { f.SetValue(elem, tpl); boundPath = f.Name; return true; }
                if (!ShouldRecurseInto(f.FieldType))
                {
                    continue;
                }

                object child;
                try { child = f.GetValue(elem); }
                catch { continue; }

                if (child != null && TryBindTemplateDeep(child, tpl, out boundPath, visited, depth + 1))
                { boundPath = $"{f.Name}.{boundPath}"; return true; }
            }

            return false;
        }

        private static bool ShouldRecurseInto(Type type)
        {
            if (type == typeof(string) || type.IsPrimitive || type.IsEnum)
            {
                return false;
            }

            if (type == typeof(decimal) || type == typeof(DateTime) || type == typeof(Guid))
            {
                return false;
            }

            if (typeof(IEnumerable).IsAssignableFrom(type))
            {
                return false;
            }

            var ns = type.Namespace ?? string.Empty;
            if (ns.StartsWith("System", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private static bool TrySetNullableBoolOrBacking(object target, string name, bool value)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

            var p = target.GetType().GetProperty(name, flags);
            if (p != null)
            {
                try
                {
                    if (p.CanWrite && (p.PropertyType == typeof(bool) || p.PropertyType == typeof(bool?)))
                    { p.SetValue(target, value); return true; }
                }
                catch { }
            }

            var f = target.GetType().GetField(name, flags);
            if (f != null)
            {
                try
                {
                    if (!f.IsInitOnly && (f.FieldType == typeof(bool) || f.FieldType == typeof(bool?)))
                    { f.SetValue(target, value); return true; }
                }
                catch { }
            }

            var bf = target.GetType().GetField($"<{name}>k__BackingField", flags);
            if (bf != null)
            {
                try
                {
                    if (bf.FieldType == typeof(bool) || bf.FieldType == typeof(bool?))
                    { bf.SetValue(target, value); return true; }
                }
                catch { }
            }
            return false;
        }

    }
}
