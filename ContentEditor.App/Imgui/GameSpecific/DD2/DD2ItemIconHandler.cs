using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentPatcher;
using ContentPatcher.DD2;
using ReeLib;

namespace ContentEditor.App.DD2;

[ObjectImguiHandler(typeof(ItemIconResource))]
public sealed class DD2ItemIconHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var entity = context.GetOwnerEntity();
        var field = context.GetEntityField()!;
        var data = entity?.Get<RSZObjectResource>("data");
        var workspace = context.GetWorkspace();
        if (entity == null || data == null || workspace == null) {
            ImGui.TextColored(Colors.Error, $"{field} field requires a valid item entity and workspace");
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
            var uvsFile = workspace.ResourceManager.GetFileContents<UvsFile>("gui/ui01/common/item/c00/uvs_ui01c00.uvs");
            var pattern = uvsFile.Sequences[sequenceId].patterns[patternId];
            var texPath = uvsFile.Textures[pattern.textureIndex].path;
            if (texture == null || texture.Path?.Contains(texPath) != true) {
                context.ClearChildren();
                var (th, tex) = workspace.ResourceManager.GetFileHandleAndContents<TexFile>(workspace.Env.AppendFileVersion(texPath));
                context.AddChild("texture", texture = new Texture().LoadFromTex(tex));
                workspace.ResourceManager.CloseFile(th, true);
            }

            var (uv0, uv1) = pattern.GetBoundingPoints();
            ImGui.Image(texture.AsTextureRef(), new Vector2(200, 200), uv0, uv1);
            if (entity.Id < entity.Config.PrimaryField?.Config.CustomIDRange![0]) {
                return;
            }
        }

        if (instance == null) {
            ImGui.Text(context.label);
            if (workspace != null && field != null) {
                ImGui.SameLine();
                if (ImGui.Button("Add custom icon")) {
                    UndoRedo.RecordSet(context, new ItemIconResource(field.Config));
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
            new ResourcePathPicker(workspace, KnownFileFormats.Texture) { Flags = ResourcePathPicker.PathPickerFlags.IngameDefaultNoConfirm                                                                                          },
            getter: (ctx) => ((ItemIconResource.ItemRectData)ctx.target!).IconTexture,
            setter: (ctx, val) => ((ItemIconResource.ItemRectData)ctx.target!).IconTexture = val as string
        );
        texHandler.ShowUI();

        if (ImGui.Button("Remove custom icon")) {
            UndoRedo.RecordSet<object?>(context, null);
            return;
        }

        {
            var texPath = instance.data.IconTexture;
            var texture = context.GetChildValue<Texture>();
            if (texPath != null && (texture == null || texture.Path?.Contains(texPath) != true)) {
                texture?.Dispose();
                if (!workspace.ResourceManager.TryResolveGameFile(texPath, out var texhandle)) {
                    ImGui.TextColored(Colors.Danger, "Texture not found");
                    return;
                }

                var texfile = texhandle.GetFile<TexFile>();
                context.ClearChildren();
                var (th, tex) = workspace.ResourceManager.GetFileHandleAndContents<TexFile>(workspace.Env.AppendFileVersion(texPath));
                context.AddChild("texture", texture = new Texture().LoadFromTex(tex));
                workspace.ResourceManager.CloseFile(th, true);
            }

            if (string.IsNullOrEmpty(instance.data.IconTexture) || texture == null) {
                return;
            }

            var v0 = new Vector2(instance.data.IconRect.x, instance.data.IconRect.y);
            var v0_2 = v0;
            var v1 = new Vector2(instance.data.IconRect.w, instance.data.IconRect.h);
            var v1_2 = v1;
            if (ImGui.DragFloat2("Margin Left/Top", ref v0_2, 0.15f, 0, texture.Width)) {
                UndoRedo.RecordCallbackSetter(context, instance.data, v0, v0_2, (d, v) => {d.IconRect.x = v.X; d.IconRect.y = v.Y; }, $"{context.GetHashCode()}_icon1");
            }
            if (ImGui.DragFloat2("Width/Height", ref v1_2, 0.15f, 0, texture.Height)) {
                UndoRedo.RecordCallbackSetter(context, instance.data, v1, v1_2, (d, v) => {d.IconRect.w = v.X; d.IconRect.h = v.Y; }, $"{context.GetHashCode()}_icon2");
            }

            var wh = new Vector2(texture.Width, texture.Height);
            ImGui.Image(texture.AsTextureRef(), new System.Numerics.Vector2(200, 200), v0 / wh, (v0 + v1) / wh);
            return;
        }
    }
}
