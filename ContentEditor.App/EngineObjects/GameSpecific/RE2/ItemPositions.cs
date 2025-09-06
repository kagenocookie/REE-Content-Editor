using System.Buffers;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.UVar;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

[RszComponentClass("app.ropeway.item.ItemPositions", nameof(GameIdentifier.re2))]
public class ItemPositions(GameObject gameObject, RszInstance data) : BaseSingleMeshComponent(gameObject, data), IFixedClassnameComponent
{
    static string IFixedClassnameComponent.Classname => "app.ropeway.item.ItemPositions";
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

    private void LoadMeshFromPrefab(string prefab)
    {
        if (Scene!.Workspace.ResourceManager.TryResolveFile(prefab, out var handle)) {
            var pfb = handle.GetFile<PfbFile>();
            var meshComp = pfb.IterAllGameObjects(true)
                .SelectMany(go => go.Components.Where(comp => comp.RszClass.name == MeshComponent.Classname))
                .FirstOrDefault();
            if (meshComp == null) return;

            var mesh = RszFieldCache.Mesh.Resource.Get(meshComp);
            if (string.IsNullOrEmpty(mesh)) return;

            var mat = RszFieldCache.Mesh.Material.Get(meshComp);
            SetMesh(mesh, mat);
        }
    }
}
