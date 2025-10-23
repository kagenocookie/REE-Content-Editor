using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Editor;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using System.Numerics;
using static ContentEditor.App.Graphics.Texture;

namespace ContentEditor.App;

public class TextureViewer : IWindowHandler, IDisposable, IFocusableFileHandleReferenceHolder
{
    public bool HasUnsavedChanges => false;

    public string HandlerName => $"Texture Viewer";

    public bool CanClose => true;
    public bool CanFocus => true;

    IRectWindow? IFileHandleReferenceHolder.Parent => data.ParentWindow;

    private Texture? texture;
    private string? texturePath;
    private FileHandle? fileHandle;
    private TextureChannel currentChannel = TextureChannel.RGBA;
    private static readonly HashSet<string> StandardImageFileExtensions = [".png", ".bmp", ".gif", ".jpg", ".jpeg", ".webp", ".tga", ".tiff", ".qoi", ".dds"];

    private WindowData data = null!;
    protected UIContext context = null!;

    public TextureViewer(string path)
    {
        texturePath = path;
    }

    public TextureViewer(FileHandle file)
    {
        texturePath = file.Filepath;
        texture = new Texture();
        texture.LoadFromFile(file);
        file.References.Add(this);
        fileHandle = file;
    }

    public TextureViewer()
    {
    }

    public void Focus()
    {
        var data = context.Get<WindowData>();
        ImGui.SetWindowFocus(data.Name ?? $"{data.Handler}##{data.ID}");
    }

    public void Close()
    {
        var data = context.Get<WindowData>();
        EditorWindow.CurrentWindow?.CloseSubwindow(data);
    }

    public void SetImageSource(string filepath)
    {
        fileHandle?.References.Remove(this);
        fileHandle = null;
        texture?.Dispose();
        texturePath = filepath;
        texture = new Texture().LoadFromFile(filepath);
    }

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    public void OnWindow()
    {
        if (texture != null) {
            ImGui.SetNextWindowSize(new Vector2(texture.Width, texture.Height + ImGui.GetFrameHeight()));
        }
        if (!ImguiHelpers.BeginWindow(data, null, ImGuiWindowFlags.MenuBar)) {
            WindowManager.Instance.CloseWindow(data);
            return;
        }
        if (ImGui.BeginMenuBar()) {
            if (ImGui.BeginMenu("File")) {
                if (ImGui.MenuItem("Open")) {
                    var window = EditorWindow.CurrentWindow!;
                    PlatformUtils.ShowFileDialog((files) => {
                        window.InvokeFromUIThread(() => SetImageSource(files[0]));
                    });
                }

                if (texture != null && texturePath != null) {
                    var baseName = PathUtils.GetFilepathWithoutExtensionOrVersion(texturePath);
                    if (ImGui.MenuItem("Save As ...")) {
                        var window = EditorWindow.CurrentWindow!;
                        PlatformUtils.ShowSaveFileDialog((file) => {
                            window.InvokeFromUIThread(() => {
                                if (!string.IsNullOrEmpty(file)) {
                                    if (fileHandle != null && fileHandle.Format.format == KnownFileFormats.Texture) {
                                        fileHandle.GetFile<TexFile>().SaveAsDDS(file);
                                    } else {
                                        texture.SaveAs(file);
                                    }
                                }
                            });
                        }, baseName.ToString(), filter: "TGA (*.tga)|*.tga|PNG (*.png)|*.png|DDS (*.dds)|*.dds");
                    }
                }
                ImGui.EndMenu();
            }
            ImGui.EndMenuBar();
        }
        ImGui.BeginGroup();
        OnIMGUI();
        ImGui.EndGroup();
        ImGui.End();
    }
    public void OnIMGUI()
    {
        if (texture == null) {
            if (texturePath == null) {
                ImGui.Text("No texture selected");
                return;
            }
            this.SetImageSource(texturePath);
        }

        if (texture != null) {
            ImGui.Text($"Path: {texture.Path}");
            ImGui.Text($"Size: {texture.Width} x {texture.Height} | Format: {texture.Format} | Channels:");
            ImGui.SameLine();
            if (ImGui.RadioButton("RGBA", currentChannel == TextureChannel.RGBA)) {
                currentChannel = TextureChannel.RGBA;
                texture.Bind();
                texture.SetChannel(currentChannel);
            }
            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.3f, 0.3f, 0.3f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.5f, 0.5f, 0.5f, 1f));
            ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(1f, 1f, 1f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1f));
            if (ImGui.RadioButton("RGB", currentChannel == TextureChannel.RGB)) {
                currentChannel = TextureChannel.RGB;
                texture.Bind();
                texture.SetChannel(currentChannel);
            }
            ImGui.PopStyleColor(4);
            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.3f, 0f, 0f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.6f, 0f, 0f, 1f));
            ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(1f, 0f, 0f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0f, 0f, 1f));
            if (ImGui.RadioButton("R", currentChannel == TextureChannel.Red)) {
                currentChannel = TextureChannel.Red;
                texture.Bind();
                texture.SetChannel(currentChannel);
            }
            ImGui.PopStyleColor(4);
            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0f, 0.3f, 0f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0f, 0.6f, 0f, 1f));
            ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0f, 1f, 0f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0f, 1f, 0f, 1f));
            if (ImGui.RadioButton("G", currentChannel == TextureChannel.Green)) {
                currentChannel = TextureChannel.Green;
                texture.Bind();
                texture.SetChannel(currentChannel);
            }
            ImGui.PopStyleColor(4);
            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0f, 0f, 0.4f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0f, 0f, 0.6f, 1f));
            ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0f, 0.5f, 1f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0f, 0.5f, 1f, 1f));
            if (ImGui.RadioButton("B", currentChannel == TextureChannel.Blue)) {
                currentChannel = TextureChannel.Blue;
                texture.Bind();
                texture.SetChannel(currentChannel);
            }
            ImGui.PopStyleColor(4);
            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.2f, 0.2f, 0.3f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.4f, 0.4f, 0.6f, 1f));
            ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0.8f, 0.8f, 0.9f, 1f));
            if (ImGui.RadioButton("A", currentChannel == TextureChannel.Alpha)) {
                currentChannel = TextureChannel.Alpha;
                texture.Bind();
                texture.SetChannel(currentChannel);
            }
            ImGui.PopStyleColor(3);
            ImGui.SameLine();
            if (texture.Path != null) {
                string suffix = GetTextureTypeSuffix(texture.Path);
                bool isKnownTextureType = ShowTextureTypeUI(suffix, previewOnly: true);
                using (var _ = ImguiHelpers.Disabled(!isKnownTextureType)) {
                    if (ImGui.Button("Channel Breakdown")) {
                        ImGui.OpenPopup("ChannelBreakdownPopup");
                    }
                }
                if (isKnownTextureType && ImGui.BeginPopup("ChannelBreakdownPopup")) {
                    ShowTextureTypeUI(suffix, previewOnly: false);
                    ImGui.EndPopup();
                }
            }
            ImGui.Separator();
            ImGui.Spacing();

            Vector2 tabPadding = new Vector2(10, 10);
            Vector2 tabSize = ImGui.GetContentRegionAvail() - tabPadding * 2;
            Vector2 size;
            float tabSpace = tabSize.X / tabSize.Y;
            float texSize = (float)texture.Width / texture.Height;

            if (texSize > tabSpace) {
                size = new Vector2(tabSize.X, tabSize.X / texSize);
            } else {
                size = new Vector2(tabSize.Y * texSize, tabSize.Y);
            }
            ImGui.Image((nint)texture.Handle, size);
        }
    }
    private static string GetTextureTypeSuffix(string path)
    {
        int lastUnderscoreIDX = path.LastIndexOf('_');
        if (lastUnderscoreIDX == -1) return "";

        int firstDotIDX = path.IndexOf('.', lastUnderscoreIDX);
        if (firstDotIDX == -1) return "";

        path = path.Substring(lastUnderscoreIDX + 1, firstDotIDX - lastUnderscoreIDX - 1);
        return path;
    }
    private static bool ShowTextureTypeUI(string path, bool previewOnly = false)
    {
        var mapping = path switch {
            "albd" => ("ALBD - AlbedoDielectric", new[] { "Albedo R", "Albedo G", "Albedo B", "Dielectric" }),
            "albm" => ("ALBM - AlbedoMetallic", new[] { "Albedo R", "Albedo G", "Albedo B", "Metallic" }),
            "atoc" => ("ATOC - AlphaTranslucentOcclusionCavity", new[] { "Alpha", "Translucency", "Ambient Occlusion", "Cavity" }),
            "atod" => ("ATOD - AlphaTranslucentOcclusionDirtmask", new[] { "Alpha", "Translucency", "Ambient Occlusion", "Dirt Mask" }),
            "atos" => ("ATOS - AlphaTranslucentOcclusionSubsurfaceScattering", new[] { "Alpha", "Translucency", "Ambient Occlusion", "Subsurface Scattering" }),
            "nrmr" => ("NRMR - NormalRoughness", new[] { "Normal X", "Normal Y", "N/A", "Roughness" }),
            "nrra" => ("NRRA - NormalRoughnessAlpha", new[] { "Roughness", "Normal Y", "Alpha", "Normal X" }),
            "nrrc" => ("NRRC - NormalRoughnessCavity", new[] { "Roughness", "Normal Y", "Cavity", "Normal X" }),
            "nrro" => ("NRRO - NormalRoughnessOcclusion", new[] { "Roughness", "Normal Y", "Ambient Occlusion", "Normal X" }),
            "nrrt" => ("NRRT - NormalRoughnessTranslucent", new[] { "Roughness", "Normal Y", "Translucency", "Normal X" }),
            "ocsd" => ("OCSD - OcclusionCavitySubsurfaceScatteringDetail", new[] { "Ambient Occlusion", "Cavity", "Subsurface Scattering", "Detail Mask" }),
            "octd" => ("OCTD - OcclusionCavityTranslucentDetail", new[] { "Ambient Occlusion", "Cavity", "Translucency", "Detail Mask" }),
            _ => (null, null)
        };

        if (mapping.Item1 == null || mapping.Item2 == null) {
            return false;
        }

        if (previewOnly) {
            return true;
        }

        ImGui.SeparatorText(mapping.Item1);
        if (ImGui.BeginTable("textureChannelBreakdown", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterV)) {
            ImGui.TableSetupColumn("Channel", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Texture", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            string[] column1 = { "Red", "Green", "Blue", "Alpha" };
            for (int i = 0; i < column1.Length; i++) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(column1[i]);
                ImGui.TableNextColumn();
                ImGui.Text(mapping.Item2[i]);
            }
            ImGui.EndTable();
        }
        return true;
    }

    public static bool IsSupportedFileExtension(string filepathOrExtension)
    {
        var format = PathUtils.ParseFileFormat(filepathOrExtension);
        if (format.format == KnownFileFormats.Texture) {
            return true;
        }

        return StandardImageFileExtensions.Contains(Path.GetExtension(filepathOrExtension));
    }

    public bool RequestClose()
    {
        return false;
    }

    public void Dispose()
    {
        fileHandle?.References.Remove(this);
        texture?.Dispose();
        texture = null;
    }
}
