using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.Pragmata;

[RszComponentClass("app.Ch16000SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16001SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16003SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch16030SpawnParam", nameof(GameIdentifier.pragmata))]
[RszComponentClass("app.Ch17000SpawnParam", nameof(GameIdentifier.pragmata))]
public class CharacterSpawnParam(GameObject gameObject, RszInstance data) : BaseSingleMeshComponent(gameObject, data)
{
    private uint lastObjectId;
    private uint CurrentObjectID => Data.Get(RszFieldCache.Pragmata.EnemySpawnParam.ObjectID).Get(RszFieldCache.Pragmata.ObjectDefine.Hash);

    protected override bool IsMeshUpToDate() => lastObjectId == CurrentObjectID;

    protected override void RefreshMesh()
    {
        UnloadMesh();
        if (lastObjectId != CurrentObjectID) {
            SetEnemyID(CurrentObjectID);
        }
    }

    private void SetEnemyID(uint hash)
    {
        lastObjectId = hash;

        var catalogPath = "natives/stm/singletonuserdata/catalog/trial/characterbodycatalog_trial_maincontents.user.3";
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

        // Logger.Debug("Could not find enemy ID", hash);
    }
}

