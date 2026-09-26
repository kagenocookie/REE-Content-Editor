using System.Numerics;
using ContentEditor.App.FileLoaders;
using ContentEditor.App.ImguiHandling;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.DD2;

[ObjectImguiHandler(typeof(PlaceholderResource<PartSwapperPreview>))]
public sealed class DD2PartSwapperPreview : IObjectUIHandler
{
    private string? selectedSubtype;
    private string? swapDataJson;

    public void OnIMGUI(UIContext context)
    {
        var entity = context.GetOwnerEntity();
        var field = context.GetEntityField<PartSwapperPreview>()!;
        var workspace = context.GetWorkspace();
        if (entity == null || workspace == null) {
            ImGui.TextColored(Colors.Error, $"{field} field requires a valid item entity and workspace");
            return;
        }

        if (entity.Type == "ItemData" && entity.Get<RSZObjectResource>("data")?.Instance.RszClass.name is not "app.ItemArmorParam" and not "app.ItemWeaponParam") {
            return;
        }

        if (!ImGui.TreeNode("3D Preview"u8)) {
            return;
        }
        ImGui.SetNextWindowSizeConstraints(new Vector2(200, 400), new Vector2(float.MaxValue));
        ImGui.BeginChild("##PartPreview"u8);
        var sceneView = context.GetChildHandler<EmbeddedWindowHandler>()?.Window as SceneView;
        var isInit = sceneView == null;
        if (sceneView == null) {
            sceneView = context.CreateEmbedded3DScene($"PartPreview{context.GetHashCode()}");

            var go = new GameObject("Preview", workspace.Env) { SceneFlags = SceneFlags.DefaultNonSerialized };
            sceneView.Scene.Add(go);
            var swp = go.AddComponent("app.PartSwapper");
            // default values currently only work on the immediate instance so re-create the _Meta field here
            swp.Data.SetFieldValue("_Meta", workspace.CreateRszInstance("app.CharacterEditDefine.MetaData"));
        }
        var partSwapper = sceneView.Scene.Find("Preview")?.GetComponent<PartSwapper>();
        if (partSwapper != null) {
            if (isInit) {
                context.AddChild("Preview Parameters", partSwapper, new RszInstanceHandler(), getter: c => c!.Data.GetFieldValue("_Meta"));
            }
            var fieldData = entity.Get("data");
            var data = fieldData;
            if (data is GroupedResource grp) {
                selectedSubtype ??= grp.Resources.FirstOrDefault().Key;

                data = selectedSubtype == null ? null : grp.Get(selectedSubtype);
            }
            if (data is IPropertyContainer propData) {
                string partField;
                object curValue = 0;
                if (entity.Type == "ItemData") {
                    var rsz = (RSZObjectResource)data;
                    var eqCat = rsz.Instance.Get(RszFieldCache.DD2.ItemArmorParam._EquipCategory);
                    if ((data as RSZObjectResource)?.Instance.RszClass.name == "app.ItemWeaponParam") {
                        var weaponGo = sceneView.Scene.Find("Preview/Weapon");
                        if (weaponGo == null) {
                            weaponGo = new GameObject("Weapon", workspace.Env);
                            sceneView.Scene.GameObjects.First().AddChild(weaponGo);
                        }
                        var meshComp = weaponGo.GetOrAddComponent<MeshComponent>();
                        HandleWeaponPreview(workspace, weaponGo, propData, eqCat);

                        partField = "";
                    } else {
                        var armorType = eqCat switch {
                            2 => "Helm",
                            3 => "Tops",
                            4 => "Pants",
                            5 => "Mantle",
                            7 => "Facewear",
                            _ => ""
                        };
                        var styleNo = rsz.Instance.Get(RszFieldCache.DD2.ItemArmorParam._StyleNo);
                        var styleEntity = workspace.ResourceManager.GetActiveEntityInstance($"{armorType}Style", styleNo);
                        curValue = (uint)((styleEntity?.Get("styleNo") as EnumMappingResource)?.Value ?? 0);
                        partField = $"_Meta._{armorType}Style";
                    }
                } else {
                    partField = field.PartField;
                    curValue = propData.Get(field.DataField)!;
                }
                if (!string.IsNullOrEmpty(partField)) {
                    var prevValue = partSwapper.Data.GetNestedFieldValue(partField);
                    var newJson = fieldData?.ToJson(workspace.Env).ToJsonString();
                    if (newJson != swapDataJson) {
                        swapDataJson = newJson;
                        partSwapper.ResetCache(false);
                    }
                    if (prevValue?.Equals(curValue) != true) {
                        partSwapper.Data.SetNestedFieldValue(partField, curValue);
                        partSwapper.ResetCache(isInit);
                    }
                }
            }
            if (isInit) {
                sceneView.Scene.ActiveCamera.LookAt(partSwapper.GameObject, true);
            }
        }
        var w = ImGui.GetContentRegionAvail().X - ImGui.GetStyle().WindowPadding.X * 2;
        ImGui.BeginChild("##scene", new Vector2(w * 0.6f, 0));
        sceneView.OnIMGUI();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("##settings", new Vector2(w * 0.4f, 0));
        context.ShowChildrenUI(1, 2);
        ImGui.EndChild();

        ImGui.EndChild();
        ImGui.TreePop();
    }

    private void HandleWeaponPreview(ContentWorkspace workspace, GameObject weaponGo, IPropertyContainer data, int equipCategory)
    {
        var weaponId = (uint)data.Get("_WeaponId")!;
        var weapon = workspace.ResourceManager.GetActiveEntityInstance("Weapon", weaponId);
        if (weapon == null) {
            return;
        }
        var expectedPfbPath = weapon.Get<RSZObjectResource>("data")?.Instance
            .Get(RszFieldCache.DD2.WeaponCatalogData.Prefab)
            .Get(RszFieldCache.Prefab.Path);

        weaponGo.Parent?.GetOrAddComponent<Motion>();

        var pfbInstance = weaponGo.Children.FirstOrDefault();
        if (pfbInstance == null || pfbInstance.PrefabPath != expectedPfbPath) {
            if (pfbInstance != null) {
                weaponGo.RemoveChild(pfbInstance);
                pfbInstance.Dispose();
            }

            if (string.IsNullOrEmpty(expectedPfbPath)) {
                return;
            }

            if (!workspace.ResourceManager.TryResolveGameFile(expectedPfbPath, out var handle)) {
                return;
            }

            pfbInstance = handle.GetCustomContent<Prefab>()!.Instantiate(weaponGo.Scene);
            weaponGo.AddChild(pfbInstance);
        }

        RszInstance? offsetSettings = workspace.ResourceManager.GetActiveEntityInstance("WeaponOffset", weaponId)?.Get<RSZObjectResource>("data")?.Instance;
        if (offsetSettings == null) {
            var weaponEnum = workspace.Env.TypeCache.GetEnumDescriptor("app.WeaponID", RszFieldType.S32);
            var weaponIdStr = weaponEnum.GetLabel(weaponId);
            var parts = weaponIdStr.Split('_', 3);
            // fallback weapon offsets to matching by category
            if (parts.Length == 3) {
                var cat2 = $"{parts[0]}_{parts[1]}";
                var cat2I = weaponEnum.GetValue(cat2);
                offsetSettings = workspace.ResourceManager.GetActiveEntityInstance("WeaponOffset", weaponEnum.GetValue(cat2).GetUInt32())?.Get<RSZObjectResource>("data")?.Instance;
                offsetSettings ??= workspace.ResourceManager.GetActiveEntityInstance("WeaponOffset", weaponEnum.GetValue(parts[0]).GetUInt32())?.Get<RSZObjectResource>("data")?.Instance;
            }
        }

        var offset = offsetSettings?
            .Get(RszFieldCache.DD2.WeaponSetting_OffsetSetting.DrawSetting)
            .OfType<RszInstance>()
            .FirstOrDefault(o => o.Get(RszFieldCache.DD2.WeaponSetting_Offset.IsLeftSetting) == (equipCategory == 1));

        if (offset == null) {
            weaponGo.Transform.ParentJoint = equipCategory == 1 ? "L_PropA" : "R_PropA";
            weaponGo.Transform.ResetLocalTransform();
        } else {
            weaponGo.Transform.ParentJoint = offset.Get(RszFieldCache.DD2.WeaponSetting_Offset.ParentJointName);
            weaponGo.Transform.LocalPosition = offset.Get(RszFieldCache.DD2.WeaponSetting_Offset.LocalPosition);
            weaponGo.Transform.LocalRotation = offset.Get(RszFieldCache.DD2.WeaponSetting_Offset.LocalRotation);
            weaponGo.Transform.LocalScale = new Vector3(offset.Get(RszFieldCache.DD2.WeaponSetting_Offset.Scale));
        }
    }
}
