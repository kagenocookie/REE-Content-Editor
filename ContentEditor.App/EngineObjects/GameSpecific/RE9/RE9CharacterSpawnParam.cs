using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

[RszComponentClass("app.Cp_B000SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B001SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B002SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B003SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B004SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B005SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B006SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B007SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B030SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B032SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B050SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B051SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B052SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B053SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B054SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B060SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B070SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B200SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B600SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B700SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B800SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_B805SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_C100SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_C200SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_C400SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_C500SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_C510SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_C600SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_C610SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_C700SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_C750SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_C800SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_C801SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_C900SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_C901SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_D000SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_D100SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_D110SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_E400SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_E700SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_E800SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_E900SpawnParam", nameof(GameIdentifier.re9))]
[RszComponentClass("app.Cp_T100SpawnParam", nameof(GameIdentifier.re9))]
public class RE9CharacterSpawnParam(GameObject gameObject, RszInstance data) : BaseMultiMeshComponent(gameObject, data)
{
    private int lastPresetId;
    private int CurrentPresetID {
        get {
            return Data.Get(RszFieldCache.RE9.EnemySpawnParam._CharacterSpawnSettings)?.Get(RszFieldCache.RE9.CharacterSpawnSetting._MontagePresetID) ?? -1;
        }
    }

    // private string? lastWeaponId;
    // private string? CurrentWeaponID {
    //     get {
    //         return Data.Get(RszFieldCache.RE9.EnemySpawnParam._EquipedWeapon);
    //     }
    // }

    private ReadOnlySpan<char> EnemyKind => Data.RszClass.name.AsSpan("app.".Length, 7);

    protected override bool IsMeshUpToDate() => lastPresetId == CurrentPresetID;

    protected override void RefreshMesh()
    {
        UnloadMeshes();
        var kind = EnemyKind;
        var category = kind[3];
        SetEnemyID(kind, CurrentPresetID);
    }

    private void SetEnemyID(ReadOnlySpan<char> kind, int presetId)
    {
        lastPresetId = presetId;
        var baseType = kind[0..5];
        var catalogPath = $"natives/stm/gameassets/character/characterprefab/{baseType}/{kind}/montage/{kind}montagepresetcataloguserdata.user.3";
        if (Scene!.Workspace.ResourceManager.TryResolveGameFile(catalogPath, out var user)) {
            var list = (user.GetFile<UserFile>().Instance?
                .Get(RszFieldCache.RE9.CharacterMontagePresetCatalogUserData._Presets))?.Cast<RszInstance>();
            if (list == null) return;

            var spawnSettings = Data.Get(RszFieldCache.RE9.EnemySpawnParam._CharacterSpawnSettings);
            var random = spawnSettings?.Get(RszFieldCache.RE9.CharacterSpawnSetting._RandomMontagePreset);
            var modelId = spawnSettings?.Get(RszFieldCache.RE9.CharacterSpawnSetting._MontageModelID);

            if (presetId != -1) {
                foreach (var entry in list) {
                    var presetPath = entry.Get(RszFieldCache.RE9.CharacterMontagePresetCatalogUserData.Entry._Preset)?.RSZUserData?.Path;
                    if (string.IsNullOrEmpty(presetPath)) continue;

                    if (!Scene!.Workspace.ResourceManager.TryResolveGameFile(presetPath, out var preset)) {
                        continue;
                    }

                    var presetData = preset.GetFile<UserFile>().Instance;
                    if (presetData?.Get(RszFieldCache.RE9.MontageData._ModelID) == modelId && presetData?.Get(RszFieldCache.RE9.MontageData._ID) == presetId) {
                        ApplyCostumePreset(entry);
                        return;
                    }
                }
            }

            // fallback
            ApplyCostumePreset(list.FirstOrDefault());
        }
    }

    private bool ApplyCostumePreset(RszInstance? costumePresetCatalogEntry)
    {
        // classname: app.CharacterMontagePresetCatalogUserData.Entry
        if (costumePresetCatalogEntry == null) return false;

        var presetPath = costumePresetCatalogEntry.Get(RszFieldCache.RE9.CharacterMontagePresetCatalogUserData.Entry._Preset)?.RSZUserData?.Path;
        if (string.IsNullOrEmpty(presetPath)) return false;

        if (!Scene!.Workspace.ResourceManager.TryResolveGameFile(presetPath, out var user)) {
            return false;
        }

        var costumePresetData = user.GetFile<UserFile>().Instance;
        var parts = costumePresetData?.Get(RszFieldCache.RE9.MontageData._PartsList)
            .Cast<RszInstance>();

        if (parts == null) {
            Logger.Debug("Could not resolve character costume preset " + Data.RszClass.name);
            return false;
        }

        var anyLoaded = false;
        // TODO background load prefabs because ts slow
        foreach (var part in parts) {
            var pfbPath = part.Get(RszFieldCache.RE9.MontageData.Parts._PrefabResource)?.Get(RszFieldCache.Prefab.Path);
            if (string.IsNullOrEmpty(pfbPath)) continue;

            if (Scene.Workspace.ResourceManager.TryResolveGameFile(pfbPath, out var pfbHandle)) {
                var pfb = pfbHandle.GetFile<PfbFile>();
                var meshComp = pfb.Root?.Components.FirstOrDefault(c => c.RszClass.name == "via.render.Mesh");
                var enabledParts = meshComp?.Get(RszFieldCache.Mesh.PartsEnable).Cast<bool>();
                if (meshComp?.Get(RszFieldCache.Mesh.Resource) is string meshPath && !string.IsNullOrEmpty(meshPath)) {
                    var mdf = meshComp?.Get(RszFieldCache.Mesh.Material);
                    var mesh = AddMesh(meshPath, mdf);
                    if (enabledParts != null && mesh != null) {
                        mesh.SetPartsEnabled(enabledParts);
                    }
                    anyLoaded |= mesh != null;
                }
            }
        }

        return anyLoaded;
    }
}

