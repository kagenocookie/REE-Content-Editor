using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.Pragmata;

[RszComponentClass("app.PropSpawnParam", nameof(GameIdentifier.pragmata))]
public class PropSpawnParam(GameObject gameObject, RszInstance data) : BaseSingleMeshComponent(gameObject, data)
{
    private uint lastObjectId;
    private uint CurrentObjectID => Data.Get(RszFieldCache.Pragmata.PropSpawnParam._KindHash);

    private bool _meshNotFound = false;

    protected override bool IsMeshUpToDate() => lastObjectId == CurrentObjectID;

    protected override void RefreshMesh()
    {
        if (_meshNotFound && mesh == null) return;

        UnloadMesh();
        SetObjectID(CurrentObjectID);
    }

    private static readonly string[] CatalogPaths = [
        "natives/stm/singletonuserdata/catalog/propcatalog_1st_maincontents.user.3",
        "natives/stm/singletonuserdata/catalog/propcatalog_2nd_maincontents.user.3",
        "natives/stm/singletonuserdata/catalog/propcatalog_manual_1st_maincontents.user.3",
        "natives/stm/singletonuserdata/catalog/propcatalog_manual_2nd_maincontents.user.3"
    ];

    private void SetObjectID(uint hash)
    {
        lastObjectId = hash;
        _meshNotFound = true;
        if (hash == 0) return;

        foreach (var catalogPath in CatalogPaths) {
            if (Scene!.Workspace.ResourceManager.TryResolveGameFile(catalogPath, out var user)) {
                var list = (user.GetFile<UserFile>().Instance?
                    .Get(RszFieldCache.Pragmata.PropCatalogUserData._CatalogData))?.Cast<RszInstance>();
                if (list == null) return;

                var target = list.FirstOrDefault(item => item
                    .Get(RszFieldCache.Pragmata.PropCatalogUserData.PropPrefabData._PropIDHash) == hash);
                if (target != null) {
                    var pfbPath = target
                        .Get(RszFieldCache.Pragmata.PropCatalogUserData.PropPrefabData._Prefab)
                        .Get(RszFieldCache.Prefab.Path);
                    _meshNotFound = !LoadMeshFromPrefab(pfbPath);
                    return;
                }
            }
        }

        Logger.Debug("Could not find prop ID", hash);
    }
}

