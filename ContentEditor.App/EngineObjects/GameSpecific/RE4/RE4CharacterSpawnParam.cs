using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.Il2cpp;
using Silk.NET.Maths;

namespace ContentEditor.App;

[RszComponentClass("chainsaw.Ch1c0SpawnParamCommon", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1c0z0SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1c0z0SpawnParam_AO", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1c0z0SpawnParam_AO_G", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1c0z0SpawnParamMercenaries", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1c0z1SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1c0z1SpawnParamMercenaries", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1c0z2SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1c0z2SpawnParamMercenaries", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1c0z2SpawnParam_AO", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1c8z0SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1c8z0SpawnParamMercenaries", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1d0z0SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1d0z0SpawnParamMercenaries", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1d2z0SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1d3z0SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1d3z0SpawnParamMercenaries", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1d4z0SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1d6z0SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1e0z0SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1e0z0SpawnParamMercenaries", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1f0z0SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1f1z0SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1f2z0SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1f6z0SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1f7z0SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch1f8z0SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch8g3z0SpawnParam", nameof(GameIdentifier.re4))]
[RszComponentClass("chainsaw.Ch8gaz0SpawnParam", nameof(GameIdentifier.re4))]
public class RE4CharacterSpawnParam(GameObject gameObject, RszInstance data) : BaseMultiMeshComponent(gameObject, data)
{
    private uint lastEnemyId = uint.MaxValue;
    private uint CurrentEnemyID => Data.Get(RszFieldCache.RE4.Ch1xxxSpawnParam._MontageID);

    protected override bool IsMeshUpToDate() => lastEnemyId == CurrentEnemyID;

    protected override void RefreshMesh()
    {
        UnloadMeshes();
        var enemyId = CurrentEnemyID;
        if (enemyId > 0 && enemyId < uint.MaxValue) {
            var kindIdEnum = (EnumDescriptor<int>)Scene!.Workspace.Env.TypeCache.GetEnumDescriptor("chainsaw.CharacterKindID");
            var kindSpan = Data.RszClass.name.AsSpan()[("chainsaw.".Length)..(Data.RszClass.name.IndexOf("SpawnParam"))];
            var kindStr = string.Concat(kindSpan.Slice(0, 3), "_", kindSpan.Slice(3)).ToLowerInvariant();
            if (!kindStr.Contains('z')) {
                var zoneId = 0; // TODO determine zone properly (based on loc id?)
                kindStr = kindStr + "z" + zoneId;
            }

            var kindId = kindIdEnum.LabelToValues.GetValueOrDefault(kindStr, -1);
            if (kindId != -1) {
                SetEnemyID(kindId, enemyId);
                return;
            }

            Logger.Debug("Failed to find RE4 character kind ID from classname " + Data.RszClass.name);
        }

        lastEnemyId = uint.MaxValue;
    }

    private void SetEnemyID(int kindId, uint enemyId)
    {
        lastEnemyId = enemyId;
        if (Scene!.Workspace.ResourceManager.TryResolveGameFile("natives/stm/_chainsaw/appsystem/catalog/character/costumepresetcatalog_1st.user.2", out var user)) {
            var list = (user.GetFile<UserFile>().Instance?.Values[0] as List<object>)?.Cast<RszInstance>();
            var data = list?.FirstOrDefault(d => d.Get(RszFieldCache.RE4.CostumePresetCatalogUserData.Data._KindID) == kindId);
            if (ApplyCostumePreset(enemyId, data)) {
                return;
            }
        }

        if (Scene!.Workspace.ResourceManager.TryResolveGameFile("natives/stm/_chainsaw/appsystem/catalog/character/costumepresetcatalog_2nd.user.2", out user)) {
            var list = (user.GetFile<UserFile>().Instance?.Values[0] as List<object>)?.Cast<RszInstance>();
            var data = list?.FirstOrDefault(d => d.Get(RszFieldCache.RE4.CostumePresetCatalogUserData.Data._KindID) == kindId);
            if (ApplyCostumePreset(enemyId, data)) {
                return;
            }
        }
    }

    private bool ApplyCostumePreset(uint characterId, RszInstance? costumePresetCatalog)
    {
        if (costumePresetCatalog == null) return false;

        var costumeUserdata = RszFieldCache.RE4.CostumePresetCatalogUserData.Data._CostumePresetUserData.Get(costumePresetCatalog);
        if (costumeUserdata?.RSZUserData is not RSZUserDataInfo constumeData || constumeData.Path == null) {
            return false;
        }

        if (!Scene!.Workspace.ResourceManager.TryResolveGameFile(constumeData.Path, out var user)) {
            return false;
        }

        var costumePresetData = user.GetFile<UserFile>().Instance;

        var costumePreset = costumePresetData?.Get(RszFieldCache.RE4.CostumePresetUserData._DataTable)
            .Cast<RszInstance>()
            .FirstOrDefault(preset => preset.Get(RszFieldCache.RE4.CostumePresetUserData.Data._ID) == characterId);

        if (costumePreset == null) {
            Logger.Debug("Could not resolve character costume preset " + characterId);
            return false;
        }

        var anyLoaded = false;
        var prefabs = costumePreset.Get(RszFieldCache.RE4.CostumePresetUserData.Data._PrefabTable).OfType<RszInstance>();
        foreach (var pfbData in prefabs) {
            var pfbRef = pfbData.Get(RszFieldCache.RE4.CostumePresetUserData.Data.PrefabData._Prefab);
            var pfbPath = pfbRef.Get(RszFieldCache.Prefab.Path);
            if (Scene.Workspace.ResourceManager.TryResolveGameFile(pfbPath, out var pfbHandle)) {
                var pfb = pfbHandle.GetFile<PfbFile>();
                var meshComp = pfb.Root?.Components.FirstOrDefault(c => c.RszClass.name == "via.render.Mesh");
                var partsOff = prefabs.FirstOrDefault()?.Get(RszFieldCache.RE4.CostumePresetUserData.Data.PrefabData._InitialPartsOffList).Cast<int>();
                if (meshComp?.Get(RszFieldCache.Mesh.Resource) is string meshPath && !string.IsNullOrEmpty(meshPath)) {
                    var mdf = meshComp?.Get(RszFieldCache.Mesh.Material);
                    var mesh = AddMesh(meshPath, mdf);
                    if (partsOff != null && mesh != null) {
                        mesh.SetAllPartsEnabled(true);
                        foreach (var offPart in partsOff) {
                            mesh.SetMeshPartEnabled(offPart, false);
                        }
                    }
                    anyLoaded |= mesh != null;
                }
            }
        }

        return anyLoaded;
    }

    private Matrix4X4<float> GetSpawnPosition()
    {
        var spawnArea = GameObject.Parent?.GetComponent("chainsaw.CharacterArea");
        if (spawnArea == null) return GameObject.Transform.WorldTransform;

        // TODO RE4 handle custom spawn areas properly (and see if there's other similar components)
        return GameObject.Transform.WorldTransform;
    }

    internal override unsafe void Render(RenderContext context)
    {
        base.Render(context);
        // var render = AppConfig.Instance.RenderMeshes.Get();
        // if (!render) {
        //     return;
        // }
        // if (mesh == null || !IsMeshUpToDate()) {
        //     RefreshMesh();
        // }
        // if (mesh != null) {
        //     context.RenderSimple(mesh, GetSpawnPosition());
        // }
    }
}

