using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.RE2;

[RszComponentClass("app.ropeway.item.ItemPositions", nameof(GameIdentifier.re2), nameof(GameIdentifier.re2rt))]
[RszComponentClass("offline.item.ItemPositions", nameof(GameIdentifier.re3), nameof(GameIdentifier.re3rt))]
public class ItemPositions(GameObject gameObject, RszInstance data) : BaseSingleMeshComponent(gameObject, data)
{
    private int lastItemId;
    private int lastWeaponId;

    private int CurrentItemID
    {
        get {
            var itemId = RszFieldCache.RE2.ItemPositions.InitializeItemId.Get(Data);
            if (itemId == 0) {
                return RszFieldCache.RE2.ItemPositions.InitializeBulletId.Get(Data);
            }
            return itemId;
        }
    }

    private int CurrentWeaponID => RszFieldCache.RE2.ItemPositions.InitializeWeaponId.Get(Data);

    protected override void RefreshMesh()
    {
        var itemId = CurrentItemID;
        if (itemId != 0) {
            SetItemID(itemId);
            return;
        }

        var weaponId = CurrentWeaponID;
        if (weaponId != 0) {
            SetWeaponID(weaponId);
        }
    }

    private void SetItemID(int itemId)
    {
        var desc = Scene!.Workspace.Env.TypeCache.GetEnumDescriptor(RszFieldCache.RE2.ItemPositions.InitializeItemId.GetField(Data.RszClass)!.original_type);
        var label = desc.GetLabel(itemId);
        var pfbPath = $"ObjectRoot/SetModel/sm7x_Item/common/item/tentative/{label}.pfb";
        LoadMeshFromPrefab(pfbPath);
        lastItemId = itemId;
        lastWeaponId = CurrentWeaponID;
    }

    private void SetWeaponID(int weaponId)
    {
        var desc = Scene!.Workspace.Env.TypeCache.GetEnumDescriptor(RszFieldCache.RE2.ItemPositions.InitializeWeaponId.GetField(Data.RszClass)!.original_type);
        var label = desc.GetLabel(weaponId);
        var pfbPath = $"ObjectRoot/Prefab/UI/Weapon/{label}.pfb";
        LoadMeshFromPrefab(pfbPath);
        lastWeaponId = weaponId;
        lastItemId = CurrentItemID;
    }

    protected override bool IsMeshUpToDate()
    {
        return lastItemId == CurrentItemID && lastWeaponId == CurrentWeaponID;
    }
}
