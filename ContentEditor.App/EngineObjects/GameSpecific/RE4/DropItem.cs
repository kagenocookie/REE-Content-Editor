using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.RE4;

[RszComponentClass("chainsaw.DropItem", nameof(GameIdentifier.re4))]
public class DropItem(GameObject gameObject, RszInstance data) : BaseMultiMeshComponent(gameObject, data)
{
    private int lastObjectId;
    private int CurrentObjectID => Data
        .Get(RszFieldCache.RE4.DropItem._ItemData)
        .Get(RszFieldCache.RE4.DropItemContext_SaveData.ItemID);

    private bool _meshNotFound = false;

    protected override bool IsMeshUpToDate() => lastObjectId == CurrentObjectID;

    protected override void RefreshMesh()
    {
        if (_meshNotFound && IsMeshUpToDate()) return;

        UnloadMeshes();
        SetObjectID(CurrentObjectID);
    }

    private static readonly string[] CatalogPaths = [
        "natives/stm/_chainsaw/environment/catalog/item/dropitemcataloguserdata_main.user.2",
        "natives/stm/_chainsaw/environment/catalog/item/dropitemcataloguserdata_1st.user.2",
        "natives/stm/_mercenaries/environment/catalog/item/dropitemcataloguserdata_mc.user.2",
        "natives/stm/_anotherorder/environment/catalog/item/dropitemcataloguserdata_ao.user.2",
        "natives/stm/_anotherorder/environment/catalog/item/dropitemcataloguserdata_main_ovr.user.2",
        "natives/stm/_anotherorder/environment/catalog/item/dropitemcataloguserdata_1st_ovr.user.2",
    ];

    private void SetObjectID(int hash)
    {
        lastObjectId = hash;
        _meshNotFound = true;
        if (hash == 0) return;

        foreach (var catalogPath in CatalogPaths) {
            if (Scene!.Workspace.ResourceManager.TryResolveGameFile(catalogPath, out var user)) {
                var list = (user.GetFile<UserFile>().Instance?
                    .Get(RszFieldCache.RE4.DropItemCatalogUserData.Items))?.Cast<RszInstance>();
                if (list == null) return;

                var target = list.FirstOrDefault(item => item
                    .Get(RszFieldCache.RE4.DropItemCatalogUserData.CatalogItem.ID) == hash);
                if (target != null) {
                    var pfbPath = target
                        .Get(RszFieldCache.RE4.DropItemCatalogUserData.CatalogItem.DropItemPrefab)
                        .Get(RszFieldCache.Prefab.Path);
                    if (AddMeshesFromPrefab(pfbPath)) {
                        _meshNotFound = false;
                        return;
                    }
                }
            }
        }

        Logger.Debug("Could not find drop item ID", hash);
    }
}

