using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Services;
using UltimateLootEditor.Patching;
using UltimateLootEditor.Services;
using UltimateLootEditor.Util;

namespace UltimateLootEditor.OnLoad;

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader)]
public sealed class UltimateLootEditorServer(DatabaseService databaseService) : IOnLoad
{
    private static bool _isPatched;

    public Task OnLoad()
    {
        if (_isPatched)
        {
            return Task.CompletedTask;
        }

        try
        {
            MapOverrideLoader.CleanupRuntimeArtifacts();
            MapOverrideLoader.EnsureAllMapFolders();
            RuntimeTemplateIndex.Initialize(databaseService);
            RuntimeAmmoResolver.Initialize(databaseService);

            var harmony = new Harmony("com.goatboy.ultimate-loot-editor.server");
            var target = AccessTools.Method(typeof(LocationLootGenerator), nameof(LocationLootGenerator.GenerateDynamicLoot));
            var prefix = AccessTools.Method(typeof(GenerateDynamicLootBridge), "Prefix");
            var createItemTarget = AccessTools.Method(
                typeof(LocationLootGenerator),
                "CreateDynamicLootItem",
                new[]
                {
                    typeof(SptLootItem),
                    typeof(IEnumerable<SptLootItem>),
                    typeof(Dictionary<string, IEnumerable<StaticAmmoDetails>>)
                });
            var createItemPostfix = AccessTools.Method(typeof(GenerateDynamicLootBridge), "PostfixCreateDynamicLootItem");

            if (target == null || prefix == null || createItemTarget == null || createItemPostfix == null)
            {
                throw new MissingMethodException("Unable to locate GenerateDynamicLoot bridge methods");
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            harmony.Patch(createItemTarget, postfix: new HarmonyMethod(createItemPostfix));

            _isPatched = true;
            Logger.Status("[ULE] Runtime loot data bridge loaded successfully.");
        }
        catch (Exception ex)
        {
            Logger.Error("[ULE] Failed to install GenerateDynamicLoot bridge", ex);
            throw;
        }

        return Task.CompletedTask;
    }
}
