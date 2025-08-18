using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentPatcher;
using ContentPatcher.DD2;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App.DD2;

[CustomFieldHandler(typeof(ItemIconField), "dd2")]
public sealed class DD2ItemIconHandler(CustomField field) : IObjectUIHandler, IObjectUIInstantiator
{
    public static Func<CustomField, IObjectUIHandler> GetFactory() => (field) => new DD2ItemIconHandler(field);

    public void OnIMGUI(UIContext context)
    {
        var entity = context.GetOwnerEntity();
        var data = entity?.Get("data") as RSZObjectResource;
        var workspace = context.GetWorkspace();
        if (entity == null || data == null || workspace == null) {
            ImGui.TextColored(Colors.Error, $"{field.label} field requires a valid item entity and workspace");
            return;
        }

        var instance = context.Get<ItemIconResource>();
        var iconNo = (ushort)data.Instance.GetFieldValue("_IconNo")!;
        if (iconNo <= 10000) {
            // default items
            var sequenceId = iconNo / 1000;
            var patternId = iconNo % 1000;
            if (sequenceId >= 10 || patternId >= 100) {
                ImGui.TextColored(Colors.Error, $"Invalid IconNo {iconNo} for presumed vanilla item");
                return;
            }
            var texture = context.GetChildValue<Texture>();
            var uvsFile = workspace.ResourceManager.ReadFileResource<UvsFile>("natives/stm/gui/ui01/common/item/c00/uvs_ui01c00.uvs.8");
            var pattern = uvsFile.Sequences[sequenceId].patterns[patternId];
            var texPath = uvsFile.Textures[pattern.textureIndex].path;
            if (texture == null || texture.Path?.Contains(texPath) != true) {
                context.ClearChildren();
                var tex = workspace.ResourceManager.ReadFileResource<TexFile>(workspace.Env.AppendFileVersion(texPath));
                context.AddChild("texture", texture = new Texture().LoadFromTex(tex));
            }

            var (uv0, uv1) = pattern.GetBoundingPoints();
            ImGui.Image(texture, new System.Numerics.Vector2(200, 200), uv0, uv1);
            if (entity.Id < entity.Config.CustomIDRange![0]) {
                return;
            }
        }

        if (instance == null) {
            ImGui.Text(context.label);
            if (workspace != null) {
                ImGui.SameLine();
                if (ImGui.Button("Add custom icon")) {
                    context.Set(new ItemIconResource());
                }
            }
            return;
        }

        if (iconNo != entity.Id) {
            data.Instance.SetFieldValue("_IconNo", (ushort)entity.Id);
        }
        var texHandler = context.GetChild<ResourcePathPicker>() ?? context.AddChild(
            "Icon Path",
            instance.data,
            new ResourcePathPicker(workspace, KnownFileFormats.Texture) { SaveWithNativePath = false },
            getter: (ctx) => ((ItemIconResource.ItemRectData)ctx.target!).IconTexture,
            setter: (ctx, val) => ((ItemIconResource.ItemRectData)ctx.target!).IconTexture = val as string
        );
        texHandler.ShowUI();

        if (ImGui.Button("Remove custom icon")) {
            context.ClearChildren();
            context.Set<object?>(null);
            return;
        }

        {
            var texPath = instance.data.IconTexture;
            var texture = context.GetChildValue<Texture>();
            if (texture == null || texPath != null && texture.Path?.Contains(texPath) != true) {
                var texfile = workspace.Env.FindSingleFile(workspace.Env.AppendFileVersion(texPath));
                if (texfile == null) {
                    ImGui.TextColored(Colors.Danger, "Texture not found");
                    return;
                }
                context.ClearChildren();
                var tex = workspace.ResourceManager.ReadFileResource<TexFile>(workspace.Env.AppendFileVersion(texPath));
                context.AddChild("texture", texture = new Texture().LoadFromTex(tex));
            }

            if (string.IsNullOrEmpty(instance.data.IconTexture)) {
                return;
            }

            var v0 = new Vector2(instance.data.IconRect.x,instance.data.IconRect.y);
            var v1 = new Vector2(instance.data.IconRect.w,instance.data.IconRect.h);
            var changed = ImGui.DragFloat2("Margin Top/Left", ref v0, 0.05f, 0, texture.Width);
            changed = ImGui.DragFloat2("Margin Bottom/Right", ref v1, 0.05f, 0, texture.Height) || changed;
            if (changed) {
                instance.data.IconRect.x = v0.X;
                instance.data.IconRect.y = v0.Y;
                instance.data.IconRect.w = v1.X;
                instance.data.IconRect.h = v1.Y;
                context.Changed = true;
            }

            ImGui.Image(texture, new System.Numerics.Vector2(200, 200), v0 / texture.Width, v1 / texture.Height);
            return;
        }
    }
}
