using System.Text.Json;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.DD2;

[RszComponentClass("app.PartSwapper", nameof(GameIdentifier.dd2))]
public class PartSwapper(GameObject gameObject, RszInstance data) : BaseMultiMeshComponent(gameObject, data), IFixedClassnameComponent
{
    private string? _lastPartSwapperData;
    private ContentWorkspace Workspace => Scene!.Workspace;
    static string IFixedClassnameComponent.Classname => "app.PartSwapper";

    private const uint Species_Human = 2501532887;
    private const uint Species_Beast = 3666037007;
    private const uint Gender_Male = 2776536455;
    private const uint Gender_Female = 1910070090;

    internal override void OnActivate()
    {
        base.OnActivate();
        IsStatic = false;
    }

    protected override bool IsMeshUpToDate()
    {
        if (Scene == null) return true;

        var currentJson = JsonSerializer.Serialize(Data.Get<RszInstance>(RszFieldCache.DD2.PartSwapper._Meta), Scene.Workspace.Env.JsonOptions);
        return currentJson == _lastPartSwapperData;
    }

    public void ResetCache(bool forceRefreshMesh)
    {
        _lastPartSwapperData = null;
        if (forceRefreshMesh) {
            RefreshMesh();
        }
    }

    protected override void RefreshMesh()
    {
        if (Scene == null) return;

        UnloadMeshes();

        var meta = Data.Get<RszInstance>(RszFieldCache.DD2.PartSwapper._Meta);

        var Species = (uint)meta.GetFieldValue("_Species")!;
        var Gender = (uint)meta.GetFieldValue("_Gender")!;
        var HeadStyle = (uint)meta.GetFieldValue("_HeadStyle")!;
        var SkinStyle = (uint)meta.GetFieldValue("_SkinStyle")!;
        var MuscleStyle = (uint)meta.GetFieldValue("_MuscleStyle")!;
        var HairStyle = (uint)meta.GetFieldValue("_HairStyle")!;
        var BeardStyle = (uint)meta.GetFieldValue("_BeardStyle")!;
        var BodyHairStyle = (uint)meta.GetFieldValue("_BodyHairStyle")!;
        var FurStyle = (uint)meta.GetFieldValue("_FurStyle")!;
        var FurPattern = (uint)meta.GetFieldValue("_FurPattern")!;
        var FurFaceStyle = (uint)meta.GetFieldValue("_FurFaceStyle")!;
        var FurForeheadStyle = (uint)meta.GetFieldValue("_FurForeheadStyle")!;
        var FurEyeStyle = (uint)meta.GetFieldValue("_FurEyeStyle")!;
        var FurCheekStyle = (uint)meta.GetFieldValue("_FurCheekStyle")!;
        var FurNoseStyle = (uint)meta.GetFieldValue("_FurNoseStyle")!;
        var EyeStyle = (uint)meta.GetFieldValue("_EyeStyle")!;
        var EyeLeftStyle = (uint)meta.GetFieldValue("_EyeLeftStyle")!;
        var EyeRightStyle = (uint)meta.GetFieldValue("_EyeRightStyle")!;
        var EyebrowStyle = (uint)meta.GetFieldValue("_EyebrowStyle")!;
        var EyelashStyle = (uint)meta.GetFieldValue("_EyelashStyle")!;
        var EyelinerStyle = (uint)meta.GetFieldValue("_EyelinerStyle")!;
        var EyeshadowStyle = (uint)meta.GetFieldValue("_EyeshadowStyle")!;
        var CheekStyle = (uint)meta.GetFieldValue("_CheekStyle")!;
        var LipStyle = (uint)meta.GetFieldValue("_LipStyle")!;
        var FrecklesStyle = (uint)meta.GetFieldValue("_FrecklesStyle")!;
        var NoseStyle = (uint)meta.GetFieldValue("_NoseStyle")!;
        var TopsStyle = (uint)meta.GetFieldValue("_TopsStyle")!;
        var PantsStyle = (uint)meta.GetFieldValue("_PantsStyle")!;
        var HelmStyle = (uint)meta.GetFieldValue("_HelmStyle")!;
        var MantleStyle = (uint)meta.GetFieldValue("_MantleStyle")!;
        var BackpackStyle = (uint)meta.GetFieldValue("_BackpackStyle")!;
        var FacewearStyle = (uint)meta.GetFieldValue("_FacewearStyle")!;
        var UnderwearStyle = (uint)meta.GetFieldValue("_UnderwearStyle")!;

        _lastPartSwapperData = JsonSerializer.Serialize(Data.Get<RszInstance>(RszFieldCache.DD2.PartSwapper._Meta), Scene.Workspace.Env.JsonOptions);

        UpdateBodyMesh(Gender, Species, SkinStyle);
        UpdateHeadMesh(Gender, Species, HeadStyle, SkinStyle);
        UpdateHairMesh(HairStyle, Species);

        UpdateTops(Gender, TopsStyle);
        UpdatePants(Gender, PantsStyle);
        UpdateHelm(Gender, HelmStyle, Species);
        UpdateMantle(Gender, MantleStyle);
        UpdateFacewear(Gender, FacewearStyle, Species);
        UpdateBackpack(Gender, BackpackStyle);
    }

    private void UpdateBodyMesh(uint gender, uint species, uint skinStyle)
    {
        // 3468739823 = body_000_m, 3066141223 = body_000_f
        var bodyMeshId = gender == Gender_Male ? 3468739823 : 3066141223;
        var bodyMesh = GetMeshOrNull("BodyMesh", bodyMeshId);

        if (bodyMesh == null) return;

        var skinId = (uint?)GetEntitySwapItem_GenderSpecies("BodySkinStyle__data", skinStyle, gender, species)?.GetFieldValue("_SkinID") ?? 0u;
        AddMesh(bodyMesh, GetSkinOrNull("BodySkin", skinId));
    }

    private void UpdateHeadMesh(uint gender, uint species, uint headStyle, uint skinStyle)
    {
        var meshId = (uint?)GetEntitySwapItem_GenderSpecies("HeadMeshStyle__data", headStyle, gender, species)?.GetFieldValue("_MeshID") ?? 0u;
        var skinId = (uint?)GetEntitySwapItem_GenderSpecies("HeadSkinStyle__data", skinStyle, gender, species)?.GetFieldValue("_SkinID") ?? 0u;

        var mesh = GetMeshOrNull("HeadMesh", meshId);
        if (mesh == null) return;

        AddMesh(mesh, GetSkinOrNull("HeadSkin", skinId));
    }

    private void UpdateHairMesh(uint hairStyle, uint species)
    {
        var swap = GetEntitySwapItem("HairStyle__data", hairStyle);
        if (swap == null) return;

        var meshId = (uint)swap.GetFieldValue("_MeshID")!;
        var skinId = (uint)swap.GetFieldValue("_SkinID")!;

        var mesh = GetMeshOrNull("HairMesh", meshId);
        if (mesh == null) return;

        var skin = species == Species_Beast
            ? GetSkinOrNull("HairSkin", skinId, "skinBeast") ?? GetSkinOrNull("HairSkin", skinId)
            : GetSkinOrNull("HairSkin", skinId);

        AddMesh(mesh, skin);
    }

    private void UpdateTops(uint gender, uint styleHash)
    {
        var swap = GetEntitySwapItem_Gender("TopsStyle__data", styleHash, gender);
        if (swap == null) {
            return;
        }

        AddMeshSkin(swap, "_BdMeshID", "_BdSkinID", "TopsBdMesh", "mesh", "TopsBdSkin", "_BdPartsEnable");
        AddMeshSkin(swap, "_BdSubMeshID", "_BdSubSkinID", "TopsBdMesh", "mesh", "TopsBdSkin", "_BdSubPartsEnable");
        AddMeshSkin(swap, "_WbMeshID", "_WbSkinID", "TopsWbMesh", "mesh", "TopsWbSkin", "_WbPartsEnable");
        AddMeshSkin(swap, "_WbSubMeshID", "_WbSubSkinID", "TopsWbMesh", "mesh", "TopsWbSkin", "_WbSubPartsEnable");
        AddMeshSkin(swap, "_AmMeshID", "_AmSkinID", "TopsAmMesh", "mesh", "TopsAmSkin", "_AmPartsEnable");
        AddMeshSkin(swap, "_AmSubMeshID", "_AmSubSkinID", "TopsAmMesh", "mesh", "TopsAmSkin", "_AmSubPartsEnable");
        AddMeshSkin(swap, "_BtMeshID", "_BtSkinID", "TopsBtMesh", "mesh", "TopsBtSkin", "_BtPartsEnable");
        AddMeshSkin(swap, "_BtSubMeshID", "_BtSubSkinID", "TopsBtMesh", "mesh", "TopsBtSkin", "_BtSubPartsEnable");
    }

    private void UpdatePants(uint gender, uint styleHash)
    {
        var swap = GetEntitySwapItem_Gender("PantsStyle__data", styleHash, gender);
        if (swap == null) {
            return;
        }

        AddMeshSkin(swap, "_LgMeshID", "_LgSkinID", "PantsLgMesh", "mesh", "PantsLgSkin", "_LgPartsEnable");
        AddMeshSkin(swap, "_LgSubMeshID", "_LgSubSkinID", "PantsLgMesh", "mesh", "PantsLgSkin", "_LgSubPartsEnable");
        AddMeshSkin(swap, "_WlMeshID", "_WlSkinID", "PantsWlMesh", "mesh", "PantsWlSkin", "_WlPartsEnable");
        AddMeshSkin(swap, "_WlSubMeshID", "_WlSubSkinID", "PantsWlMesh", "mesh", "PantsWlSkin", "_WlSubPartsEnable");
    }

    private void UpdateHelm(uint gender, uint styleHash, uint species)
    {
        var swap = GetEntitySwapItem_Gender("HelmStyle__data", styleHash, gender);
        if (swap == null) {
            return;
        }

        var speciesMesh = species == Species_Human ? "humanMesh" : "beastMesh";
        AddMeshSkin(swap, "_MeshID", "_SkinID", "HelmMesh", speciesMesh, "HelmSkin", "_PartsEnable");
        AddMeshSkin(swap, "_SubMeshID", "_SubSkinID", "HelmMesh", speciesMesh, "HelmSkin", "_SubPartsEnable");
    }

    private void UpdateMantle(uint gender, uint styleHash)
    {
        var swap = GetEntitySwapItem_Gender("MantleStyle__data", styleHash, gender);
        if (swap == null) {
            return;
        }

        AddMeshSkin(swap, "_MeshID", "_SkinID", "MantleMesh", "mesh", "MantleSkin", "_PartsEnable");
    }

    private void UpdateFacewear(uint gender, uint styleHash, uint species)
    {
        var swap = GetEntitySwapItem_Gender("FacewearStyle__data", styleHash, gender);
        if (swap == null) {
            return;
        }

        var speciesMesh = species == Species_Human ? "humanMesh" : "beastMesh";
        AddMeshSkin(swap, "_MeshID", "_SkinID", "FacewearMesh", speciesMesh, "FacewearSkin", "_PartsEnable");
    }

    private void UpdateBackpack(uint gender, uint styleHash)
    {
        var swap = GetEntitySwapItem_Gender("BackpackStyle__data", styleHash, gender);
        if (swap == null) {
            return;
        }

        AddMeshSkin(swap, "_MeshID", "_SkinID", "BackpackMesh", "mesh", "BackpackSkin", "_PartsEnable");
    }

    private void AddMeshSkin(
        RszInstance swapData,
        string meshField, string skinField,
        string meshEntity, string entityMeshField,
        string skinEntity,
        string? enablePartsField = null)
    {
        var meshId = (uint)swapData.GetFieldValue(meshField)!;
        var skinId = (uint)swapData.GetFieldValue(skinField)!;
        if (meshId == 0 || skinId == 0) return;

        // var mesh = Workspace.ResourceManager.GetActiveEntityInstance(meshEntity, meshId)?.Get<RSZObjectResource>(entityMeshField)?.Instance?.GetFieldValue("_Mesh") as string;
        var mesh = GetMeshOrNull(meshEntity, meshId, entityMeshField);
        var skin = GetSkinOrNull(skinEntity, skinId);
        if (!string.IsNullOrEmpty(mesh)) {
            var loadedMesh = AddMesh(mesh, skin, ShaderFlags.EnableStreamingTex);
            if (!string.IsNullOrEmpty(enablePartsField) && loadedMesh != null) {
                // TODO enable parts
                var partBits = Convert.ToUInt64(swapData.GetFieldValue(enablePartsField));
                for (int i = 0; i < 64; i++) {
                    if ((partBits & (1ul << i)) == 0) {
                        loadedMesh.SetMeshPartEnabled(i, false);
                    }
                }
            }
        }
    }

    private RszInstance? GetEntitySwapItem(string swapDataResourceType, uint styleHash)
    {
        if (styleHash == 0) return null;

        var data = Workspace.ResourceManager.GetActiveResourceInstance(swapDataResourceType, styleHash);
        if (data == null) return null;

        if (data is RSZObjectResource rsz) {
            return rsz.Instance;
        }

        return null;
    }

    private RszInstance? GetEntitySwapItem_Gender(string swapDataResourceType, uint styleHash, uint gender)
    {
        if (styleHash == 0) return null;

        var genderName = gender == Gender_Male ? "male" : "female";
        var data = Workspace.ResourceManager.GetActiveResourceInstance(swapDataResourceType, styleHash);
        if (data == null) return null;

        if (data is RSZObjectResource rsz) {
            return rsz.Instance;
        } else if (data is GroupedResource group) {
            return (group.Get(genderName) as RSZObjectResource)?.Instance;
        }

        return null;
    }

    private RszInstance? GetEntitySwapItem_Species(string swapDataResourceType, uint styleHash, uint species)
    {
        if (styleHash == 0) return null;

        var genderName = species == Species_Human ? "human" : "beast";
        var data = Workspace.ResourceManager.GetActiveResourceInstance(swapDataResourceType, styleHash);
        if (data == null) return null;

        if (data is RSZObjectResource rsz) {
            return rsz.Instance;
        } else if (data is GroupedResource group) {
            return (group.Get(genderName) as RSZObjectResource)?.Instance;
        }

        return null;
    }

    private RszInstance? GetEntitySwapItem_GenderSpecies(string swapDataResourceType, uint styleHash, uint gender, uint species)
    {
        if (styleHash == 0) return null;

        var subtype = (gender, species) switch {
            (Gender_Male, Species_Human) => "humanMale",
            (Gender_Female, Species_Human) => "humanFemale",
            (Gender_Male, Species_Beast) => "beastMale",
            (Gender_Female, Species_Beast) => "beastFemale",
            _ => "",
        };
        if (string.IsNullOrEmpty(subtype)) return null;

        var data = Workspace.ResourceManager.GetActiveResourceInstance(swapDataResourceType, styleHash);
        if (data == null) return null;

        if (data is RSZObjectResource rsz) {
            return rsz.Instance;
        } else if (data is GroupedResource group) {
            return (group.Get(subtype) as RSZObjectResource)?.Instance;
        }

        return null;
    }

    private string? GetMeshOrNull(string meshEntity, uint meshId, string meshField = "mesh")
    {
        if (meshId == 0) return null;

        var path = Workspace.ResourceManager.GetActiveEntityInstance(meshEntity, meshId)
            ?.Get<RSZObjectResource>(meshField)
            ?.Instance
            ?.GetFieldValue("_Mesh") as string;

        return string.IsNullOrEmpty(path) ? null : path;
    }

    private string? GetSkinOrNull(string skinEntity, uint skinId, string skinField = "skin")
    {
        if (skinId == 0) return null;

        var path = Workspace.ResourceManager.GetActiveEntityInstance(skinEntity, skinId)
            ?.Get<RSZObjectResource>(skinField)
            ?.Instance
            ?.GetFieldValue("_Material") as string;

        return string.IsNullOrEmpty(path) ? null : path;
    }
}
