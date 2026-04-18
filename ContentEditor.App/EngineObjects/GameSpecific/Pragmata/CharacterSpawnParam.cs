using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.Pragmata;

[RszComponentClass("app.Ch06000SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch06001SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch07000SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch07100SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch07200SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch09000SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch09001SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch10100SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch14100SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch14200SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch14300SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch14310SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch14400SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch14500SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch14600SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16000SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16001SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16003SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16005SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16010SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16021SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16022SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16030SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16050SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16060SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16091SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16092SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16095SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16100SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16200SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch17000SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch17001SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch17020SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch17021SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch17100SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch17200SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch17202SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch17400SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch18000SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch21000SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch22000SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.PartnerSpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.PlayerSpawnParam", nameof(GameIdentifier.pragmata))]
public class CharacterSpawnParam(GameObject gameObject, RszInstance data) : BaseSingleMeshComponent(gameObject, data)
{
    private uint lastObjectId;
    private uint CurrentObjectID => Data.Get(RszFieldCache.Pragmata.EnemySpawnParam.ObjectID).Get(RszFieldCache.Pragmata.ObjectDefine.Hash);

    protected override bool IsMeshUpToDate() => lastObjectId == CurrentObjectID;

    protected override void RefreshMesh()
    {
        UnloadMesh();
        SetEnemyID(CurrentObjectID);
    }

    private static readonly string[] CatalogPaths = [
        "natives/stm/singletonuserdata/catalog/characterbodycatalog_1st_maincontents.user.3",
        "natives/stm/singletonuserdata/catalog/characterbodycatalog_2nd_maincontents.user.3"
    ];

    private void SetEnemyID(uint hash)
    {
        lastObjectId = hash;
        if (hash == 0) return;

        foreach (var catalogPath in CatalogPaths) {
            if (Scene!.Workspace.ResourceManager.TryResolveGameFile(catalogPath, out var user)) {
                var list = (user.GetFile<UserFile>().Instance?
                    .Get(RszFieldCache.Pragmata.CharacterBodyCatalogUserData._DataTable))?.Cast<RszInstance>();
                if (list == null) return;

                var target = list.FirstOrDefault(item => item
                    .Get(RszFieldCache.Pragmata.CharacterBodyCatalogUserData.Data._CharacterKind)
                    .Get(RszFieldCache.Pragmata.ObjectDefine.Hash) == hash);
                if (target != null) {
                    var pfbPath = target
                        .Get(RszFieldCache.Pragmata.CharacterBodyCatalogUserData.Data._BodyPrefab)
                        .Get(RszFieldCache.Prefab.Path);
                    LoadMeshFromPrefab(pfbPath);
                    return;
                }
            }
        }

        Logger.Debug("Could not find enemy ID", hash);
    }
}

