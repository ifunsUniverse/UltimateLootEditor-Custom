using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators.Loot;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using UltimateLootEditor.Shared;
using UltimateLootEditor.Patching;
using UltimateLootEditor.Services;
using UltimateLootEditor.Util;

namespace UltimateLootEditor.OnLoad;

[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class UltimateLootEditorServer(TemplateTable templateTable, LocaleTable localeTable) : IOnLoad
{
    private static bool _isPatched;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (_isPatched)
        {
            return Task.CompletedTask;
        }

        try
        {
            MapOverrideLoader.CleanupRuntimeArtifacts();
            MapOverrideLoader.EnsureAllMapFolders();
            RuntimeTemplateIndex.Initialize(templateTable, localeTable);
            RuntimeAmmoResolver.Initialize(templateTable);

            var harmony = new Harmony(ModConstants.ServerModGuid);
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
