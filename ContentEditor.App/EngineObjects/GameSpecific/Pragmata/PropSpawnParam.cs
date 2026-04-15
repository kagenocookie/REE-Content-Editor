using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.Pragmata;

[RszComponentClass("app.PropSpawnParam", nameof(GameIdentifier.pragmata))]
public class PropSpawnParam(GameObject gameObject, RszInstance data) : BaseSingleMeshComponent(gameObject, data)
{
    private uint lastObjectId;
    private uint CurrentObjectID => Data.Get(RszFieldCache.Pragmata.PropSpawnParam._KindHash);

    protected override bool IsMeshUpToDate() => lastObjectId == CurrentObjectID;

    protected override void RefreshMesh()
    {
        UnloadMesh();
        if (lastObjectId != CurrentObjectID) {
            SetObjectID(CurrentObjectID);
        }
    }

    private static readonly string[] CatalogPaths = [
        "natives/stm/singletonuserdata/catalog/trial/propcatalog_trial_maincontents.user.3",
        "natives/stm/singletonuserdata/catalog/trial/propcatalog_manual_trial_maincontents.user.3"
    ];

    private void SetObjectID(uint hash)
    {
        lastObjectId = hash;

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
                    LoadMeshFromPrefab(pfbPath);
                    return;
                }
            }
        }

        // Logger.Debug("Could not find prop ID", hash);
    }
}

