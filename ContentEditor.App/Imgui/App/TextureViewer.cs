using System.Diagnostics;
using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.BackgroundTasks;
using ContentEditor.Core;
using ContentEditor.Editor;
using ContentPatcher;
using DirectXTexNet;
using ReeLib;
using ReeLib.DDS;
using static ContentEditor.App.Graphics.Texture;

namespace ContentEditor.App;

public class TextureViewer : IWindowHandler, IDisposable, IFocusableFileHandleReferenceHolder
{
    public bool HasUnsavedChanges => fileHandle?.Modified == true;

    public string HandlerName => $"Texture Viewer";

    public bool CanClose => true;
    public bool CanFocus => true;

    IRectWindow? IFileHandleReferenceHolder.Parent => data.ParentWindow;

    private Texture? texture;
    private string? texturePath;
    private FileHandle? fileHandle;
    private TextureChannel currentChannel = TextureChannel.RGBA;
    private static readonly HashSet<string> StandardImageFileExtensions = [".png", ".bmp", ".gif", ".jpg", ".jpeg", ".webp", ".tga", ".tiff", ".qoi", ".dds"];

    public const string OpenFileFilter = "Supported images (.tga, .png, .dds)|*.tga;*.png;*.dds|TGA (*.tga)|*.tga|PNG (*.png)|*.png|DDS (*.dds)|*.dds";
    public const string SaveFileFilter = "TGA (*.tga)|*.tga|PNG (*.png)|*.png|DDS (*.dds)|*.dds";

    private WindowData data = null!;
    protected UIContext context = null!;

    private ContentWorkspace workspace = null!;
    private FormatPreset selectedFormatPreset;

    private string? lastImportSourcePath;
    private string exportTemplate = "";
    private MipGenOptions mipMapOption = MipGenOptions.Generate;
    private string[] MipGenLabels = ["Leave Unchanged", "Generate All Mip Map Levels", "Remove Mip Maps"];
    private int[] MipGenValues = [0, 1, 2];
    private enum MipGenOptions { Unchanged, Generate, Remove };

    private DxgiFormat exportFormat = DxgiFormat.BC7_UNORM_SRGB;

    internal static DxgiFormat[] DxgiFormats = [
        DxgiFormat.BC7_UNORM,
        DxgiFormat.BC7_UNORM_SRGB,
        DxgiFormat.R8G8B8A8_UNORM,
        DxgiFormat.R8G8B8A8_UNORM_SRGB,
        DxgiFormat.BC1_UNORM,
        DxgiFormat.BC1_UNORM_SRGB,
        DxgiFormat.BC2_UNORM,
        DxgiFormat.BC2_UNORM_SRGB,
        DxgiFormat.BC3_UNORM,
        DxgiFormat.BC3_UNORM_SRGB,
        DxgiFormat.BC4_UNORM,
        DxgiFormat.BC5_SNORM,
        DxgiFormat.BC5_UNORM,
    ];
    internal static string[] DxgiFormatStrings = DxgiFormats.Select(f => f.ToString()).ToArray();

    private static readonly FormatPreset[] Presets = [
        new FormatPreset("Color Texture | BC7_UNORM_SRGB", DxgiFormat.BC7_UNORM_SRGB, MipGenOptions.Generate),
        new FormatPreset("Normal Texture | BC7_UNORM", DxgiFormat.BC7_UNORM, MipGenOptions.Generate),
        new FormatPreset("UI (compressed) | BC7_UNORM_SRGB", DxgiFormat.BC7_UNORM_SRGB, MipGenOptions.Remove),
        new FormatPreset("Uncompressed (Color) | R8G8B8A8_UNORM_SRGB", DxgiFormat.R8G8B8A8_UNORM_SRGB, MipGenOptions.Unchanged),
        new FormatPreset("Uncompressed (Non-Color) | R8G8B8A8_UNORM", DxgiFormat.R8G8B8A8_UNORM, MipGenOptions.Unchanged),
    ];
    private static readonly string[] PresetNames = Presets.Select(x => x.name).ToArray();

    private record struct FormatPreset(string name, DxgiFormat format, MipGenOptions mips);

    public TextureViewer(string path)
    {
        texturePath = path;
    }

    public TextureViewer(ContentWorkspace env, FileHandle file)
    {
        workspace = env;
        SetImageSource(file);
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

    public void SetImageSource(FileHandle file)
    {
        fileHandle?.References.Remove(this);
        fileHandle = file;
        texture?.Dispose();
        texturePath = file.Filepath;
        texture = new Texture().LoadFromFile(file);
        file.References.Add(this);
        if (string.IsNullOrEmpty(exportTemplate)) {
            if (file.Format.format == KnownFileFormats.Texture) {
                exportTemplate = file.GetFile<TexFile>().CurrentVersionConfig!;
            }

            if (string.IsNullOrEmpty(exportTemplate)) {
                exportTemplate = TexFile.GetGameVersionConfigs(workspace.Env.Config.Game).FirstOrDefault() ?? TexFile.AllVersionConfigs.Last();
            }
        }
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
        workspace = context.GetWorkspace()!;
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
        ShowMenu();
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
            if (ImGui.Button("Path") && texture.Path != null) {
                EditorWindow.CurrentWindow?.CopyToClipboard(texture.Path);
            }
            ImguiHelpers.Tooltip($"{texture.Path}");
            ImGui.SameLine();
            ImGui.Text($"| Size: {texture.Width} x {texture.Height} | Format: {texture.Format}");
            ImGui.Text("Channels:");
            ImGui.SameLine();
            if (ImGui.RadioButton("RGBA", currentChannel == TextureChannel.RGBA)) {
                currentChannel = TextureChannel.RGBA;
                texture.SetChannel(currentChannel);
            }
            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.3f, 0.3f, 0.3f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.5f, 0.5f, 0.5f, 1f));
            ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(1f, 1f, 1f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.8f, 0.8f, 1f));
            if (ImGui.RadioButton("RGB", currentChannel == TextureChannel.RGB)) {
                currentChannel = TextureChannel.RGB;
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
                texture.SetChannel(currentChannel);
            }
            ImGui.PopStyleColor(4);
            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.2f, 0.2f, 0.3f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.4f, 0.4f, 0.6f, 1f));
            ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0.8f, 0.8f, 0.9f, 1f));
            if (ImGui.RadioButton("A", currentChannel == TextureChannel.Alpha)) {
                currentChannel = TextureChannel.Alpha;
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
            ImGui.Image(texture.AsTextureRef(), size);
        }
    }

    private void ShowMenu()
    {
        if (ImGui.BeginMenuBar()) {
            if (ImGui.BeginMenu("File")) {
                if (ImGui.MenuItem("Open")) {
                    var window = EditorWindow.CurrentWindow!;
                    PlatformUtils.ShowFileDialog((files) => {
                        window.InvokeFromUIThread(() => SetImageSource(files[0]));
                    });
                }

                if (texture != null) {
                    if (fileHandle?.Modified == true && File.Exists(texturePath)) {
                        if (ImGui.MenuItem("Save")) {
                            if (fileHandle.Loader is TexFileLoader) {
                                fileHandle.Save(workspace);
                            } else {
                                texture.SaveAs(texturePath);
                            }
                            fileHandle.Modified = false;
                        }
                        if (ImGui.MenuItem("Revert")) {
                            fileHandle.Stream.Dispose();
                            fileHandle.Stream = File.OpenRead(texturePath).ToMemoryStream();
                            if (fileHandle.Loader is TexFileLoader) {
                                fileHandle.GetFile<TexFile>().FileHandler = new FileHandler(fileHandle.Stream, fileHandle.Filepath);
                            } else if (fileHandle.Resource is BaseFileResource<DDSFile> dds) {
                                dds.File.FileHandler = new FileHandler(fileHandle.Stream, fileHandle.Filepath);
                            }
                            fileHandle.Revert(workspace);
                            SetImageSource(fileHandle);
                        }
                    }

                    if (ImGui.MenuItem("Save As ...")) {
                        var baseName = PathUtils.GetFilepathWithoutExtensionOrVersion(texturePath ?? texture.Path);
                        var fileFilter = SaveFileFilter;
                        var currentTexExt = fileHandle?.Loader is TexFileLoader ? PathUtils.GetFilenameExtensionWithSuffixes(texture.Path).ToString() : null;
                        if (!string.IsNullOrEmpty(currentTexExt)) {
                            fileFilter += $"|TEX (.{currentTexExt})|*.{currentTexExt}";
                        }

                        PlatformUtils.ShowSaveFileDialog((file) => {
                            MainLoop.Instance.InvokeFromUIThread(() => SaveTextureToFile(file));
                        }, baseName.ToString(), filter: fileFilter);
                    }
                }


                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Convert")) {
                ShowExportMenu();
                ImGui.EndMenu();
            }
            ImGui.EndMenuBar();
        }
    }

    private bool SaveTextureToFile(string file)
    {
        if (texture == null) return false;

        if (fileHandle == null) {
            texture.SaveAs(file);
            return false;
        }

        var fmt = PathUtils.ParseFileFormat(file);
        if (fmt.format == KnownFileFormats.Texture && fmt.version == -1) {
            fmt.version = TexFile.GetFileExtension(exportTemplate);
            file += "." + fmt.version;
        }
        // saving to tex
        if (fmt.format == KnownFileFormats.Texture) {
            if (fileHandle == null || fileHandle.Format.format != KnownFileFormats.Texture) {
                // not currently a tex file - needs conversion
                texture.SaveAs(file);
            } else {
                // it's already a tex file, ensure it's the same ext and then save it
                if (!file.Contains("." + fileHandle.Format.version.ToString())) {
                    Logger.Error($"The texture file extension was changed from .tex.{fileHandle.Format.version} to {PathUtils.GetFilenameExtensionWithSuffixes(file)}. Use the Convert menu for changing formats.");
                    return false;
                }

                fileHandle.Save(workspace, file);
            }
            return true;
        }

        // saving to dds/other
        if (fileHandle.Format.format == KnownFileFormats.Texture && Path.GetExtension(file) == ".dds") {
            fileHandle.GetFile<TexFile>().SaveAsDDS(file);
        } else {
            texture.SaveAs(file);
        }

        return true;
    }

    private void ShowExportMenu()
    {
        if (texture == null || fileHandle == null || workspace == null) return;

        var isTex = fileHandle.Format.format == KnownFileFormats.Texture;
        var isDds = !isTex && Path.GetExtension(texturePath) == ".dds";
        var filepath = texturePath;

        if (fileHandle.HandleType is FileHandleType.Bundle or FileHandleType.Disk && File.Exists(filepath) && fileHandle.Format.format == KnownFileFormats.Mesh) {
            if (ImGui.Selectable($"{AppIcons.SI_GenericImport} Import From DDS...")) {
                var window = EditorWindow.CurrentWindow!;
                PlatformUtils.ShowFileDialog((files) => {
                    window.InvokeFromUIThread(() => {
                        lastImportSourcePath = files[0];
                        if (workspace.ResourceManager.TryLoadUniqueFile(lastImportSourcePath, out var importedFile)) {
                            importedFile.Save(workspace, filepath.ToString());
                            fileHandle.Stream = File.OpenRead(filepath.ToString()).ToMemoryStream();
                            fileHandle.Revert(workspace);
                            SetImageSource(fileHandle);
                            importedFile.Dispose();
                        }
                    });
                }, lastImportSourcePath, fileExtension: "DDS (*.dds)|*.dds");
            }
        }

        ImGui.SeparatorText("Convert TEX");
        ImguiHelpers.ValueCombo("Tex Version", TexFile.AllVersionConfigsWithExtension, TexFile.AllVersionConfigs, ref exportTemplate);

        ImGui.Spacing();
        if (ImguiHelpers.ValueCombo("Preset", PresetNames, Presets, ref selectedFormatPreset)) {
            exportFormat = selectedFormatPreset.format;
            mipMapOption = selectedFormatPreset.mips;
        }
        ImGui.Spacing();

        ImguiHelpers.ValueCombo("DXGI Format", DxgiFormatStrings, DxgiFormats, ref exportFormat);
        ImguiHelpers.Tooltip("The format to convert non-DDS images to.");
        ImGui.Spacing();
        var mmo = (int)mipMapOption;
        ImGui.BeginGroup();
        if (ImguiHelpers.InlineRadioGroup(MipGenLabels, MipGenValues, ref mmo)) {
            mipMapOption = (MipGenOptions)mmo;
        }
        ImGui.EndGroup();
        ImguiHelpers.Tooltip("Choose what to do with mip maps (lower resolution images for better performance). If the mips are already in the target state, they will be reused unchanged.");

        if (exportFormat != selectedFormatPreset.format || mipMapOption != selectedFormatPreset.mips) {
            selectedFormatPreset = default;
        }
        ImGui.Spacing();
        var conv1 = ImGui.Button($"{AppIcons.SI_GenericConvert} Convert");
        if (fileHandle.Loader is TexFileLoader) {
            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.SI_UpdateTexture} Update Current")) {
                var defaultFilename = GetTexFilenameSuggestion();
                var tex = fileHandle.GetFile<TexFile>();
                ProcessTexture(defaultFilename, (dds) => {
                    MainLoop.Instance.InvokeFromUIThread(() => {
                        tex.LoadDataFromDDS(dds);
                        texture.LoadFromDDS(dds);
                        fileHandle.Modified = true;
                    });
                });
            }
        }

        var bundleConvert = workspace.CurrentBundle != null && ImguiHelpers.SameLine() && ImGui.Button("Save to bundle ...");
        if (conv1 || bundleConvert) {
            var defaultFilename = GetTexFilenameSuggestion();

            ProcessTexture(defaultFilename, (dds) => {
                var tex = new TexFile(new FileHandler(new MemoryStream(), defaultFilename));
                tex.ChangeVersion(exportTemplate);
                tex.LoadDataFromDDS(dds);
                // note: potential memory leak if the user doesn't confirm the save dialog. Surely nobody will have issues because of it...
                if (bundleConvert) {
                    var tempres = new BaseFileResource<TexFile>(tex);
                    ResourcePathPicker.ShowSaveToBundle(fileHandle.Loader, tempres, workspace, defaultFilename, fileHandle.NativePath, () => {
                        dds.Dispose();
                        tex.Dispose();
                    });
                } else {
                    PlatformUtils.ShowSaveFileDialog((path) => {
                        tex.SaveAs(path);
                        dds.Dispose();
                        tex.Dispose();
                    }, defaultFilename);
                }
            });
        }
    }

    private string GetTexFilenameSuggestion()
    {
        var baseName = PathUtils.GetFilepathWithoutExtensionOrVersion(texturePath ?? texture?.Path ?? "texture");
        var ver = TexFile.GetFileExtension(exportTemplate);
        var ext = $".tex.{ver}";
        return baseName.ToString() + ext;
    }

    private unsafe void ProcessTexture(string defaultFilename, Action<DDSFile> callback)
    {
        Debug.Assert(texture != null && fileHandle != null);

        var isTex = fileHandle.Format.format == KnownFileFormats.Texture;
        var isDds = !isTex && Path.GetExtension(texturePath) == ".dds";

        DDSFile dds;
        if (isTex) {
            dds = fileHandle.GetFile<TexFile>().ConvertToDDS(new FileHandler(new MemoryStream(), defaultFilename));
        } else if (!isDds) {
            dds = texture.GetAsDDS(0, 1);
        } else {
            // copy the current source dds file
            dds = new DDSFile(new FileHandler(fileHandle.Stream.ToMemoryStream(false, true), defaultFilename));
            dds.Read();
        }

        var operations = new List<TextureConversionTask.TextureOperation>();
        if (dds.Header.DX10.Format != exportFormat) {
            operations.Add(new TextureConversionTask.ChangeFormat(exportFormat));
        }

        if (mipMapOption == MipGenOptions.Generate) {
            operations.Add(new TextureConversionTask.GenerateMipMaps());
        } else if (mipMapOption == MipGenOptions.Remove) {
            operations.Add(new TextureConversionTask.RemoveMipMaps());
        }

        if (operations.Count == 0) {
            callback(dds);
        } else {
            MainLoop.Instance.BackgroundTasks.Queue(new TextureConversionTask(dds, callback, operations.ToArray()));
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
    public static Dictionary<string, string> TextureTypeNames = new Dictionary<string, string>() {
        { "ALBD", "ALBD - AlbedoDielectric" },
        { "ALBM", "ALBM - AlbedoMetallic" },
        { "ATOC", "ATOC - AlphaTranslucentOcclusionCavity" },
        { "ATOD", "ATOD - AlphaTranslucentOcclusionDirtmask" },
        { "ATOS", "ATOS - AlphaTranslucentOcclusionSubsurfaceScattering" },
        { "NRMR", "NRMR - NormalRoughness" },
        { "NRRA", "NRRA - NormalRoughnessAlpha" },
        { "NRRC", "NRRC - NormalRoughnessCavity" },
        { "NRRO", "NRRO - NormalRoughnessOcclusion" },
        { "NRRT", "NRRT - NormalRoughnessTranslucent" },
        { "OCSD", "OCSD - OcclusionCavitySubsurfaceScatteringDetail" },
        { "OCTD", "OCTD - OcclusionCavityTranslucentDetail" },
    };
    private static bool ShowTextureTypeUI(string path, bool previewOnly = false)
    {
        var mapping = path.ToLowerInvariant() switch {
            "albd" => (TextureTypeNames["ALBD"], new[] { "Albedo R", "Albedo G", "Albedo B", "Dielectric" }),
            "albm" => (TextureTypeNames["ALBM"], new[] { "Albedo R", "Albedo G", "Albedo B", "Metallic" }),
            "atoc" => (TextureTypeNames["ATOC"], new[] { "Alpha", "Translucency", "Ambient Occlusion", "Cavity" }),
            "atod" => (TextureTypeNames["ATOD"], new[] { "Alpha", "Translucency", "Ambient Occlusion", "Dirt Mask" }),
            "atos" => (TextureTypeNames["ATOS"], new[] { "Alpha", "Translucency", "Ambient Occlusion", "Subsurface Scattering" }),
            "nrmr" => (TextureTypeNames["NRMR"], new[] { "Normal X", "Normal Y", "N/A", "Roughness" }),
            "nrra" => (TextureTypeNames["NRRA"], new[] { "Roughness", "Normal Y", "Alpha", "Normal X" }),
            "nrrc" => (TextureTypeNames["NRRC"], new[] { "Roughness", "Normal Y", "Cavity", "Normal X" }),
            "nrro" => (TextureTypeNames["NRRO"], new[] { "Roughness", "Normal Y", "Ambient Occlusion", "Normal X" }),
            "nrrt" => (TextureTypeNames["NRRT"], new[] { "Roughness", "Normal Y", "Translucency", "Normal X" }),
            "ocsd" => (TextureTypeNames["OCSD"], new[] { "Ambient Occlusion", "Cavity", "Subsurface Scattering", "Detail Mask" }),
            "octd" => (TextureTypeNames["OCTD"], new[] { "Ambient Occlusion", "Cavity", "Translucency", "Detail Mask" }),
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
