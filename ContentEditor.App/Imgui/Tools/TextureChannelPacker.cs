using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.BackgroundTasks;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.DDS;
using ReeLib.via;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ContentEditor.App;

public class TextureChannelPacker : IWindowHandler, IDisposable
{
    public string HandlerName => "Channel Packer";

    public bool HasUnsavedChanges => false;

    private WindowData data = null!;
    protected UIContext context = null!;

    private ContentWorkspace workspace = null!;

    private readonly List<TextureSlot> slots = new();
    private Texture? outputTexture;
    private Texture.TextureChannel outputDisplayChannel = Texture.TextureChannel.RGB;
    private string convertTemplateConfig = "";
    private DxgiFormat exportFormat = DxgiFormat.BC7_UNORM_SRGB;
    private bool exportMipMaps = true;
    private bool isDirty = false;

    private PackingPreset? selectedPreset;

    private TextureConversionTask? activeTask;

    public enum TextureSourceSwizzle
    {
        Red = 1,
        Green = 2,
        Blue = 4,
        Alpha = 8,
        RG = Red | Green,
        GB = Green | Blue,
        BA = Blue | Alpha,
        RGB = Red | Green | Blue,
        RGBA = Red | Green | Blue | Alpha,
    }

    public enum TextureSwizzle
    {
        Red = 1,
        Green = 2,
        Blue = 4,
        Alpha = 8,
        RG = Red | Green,
        GB = Green | Blue,
        BA = Blue | Alpha,
        GA_NRRx = Green | Alpha,
        RGB = Red | Green | Blue,
        RGBA = Red | Green | Blue | Alpha,
    }

    // mapping of "target channel count" to "possible input texture swizzle sources"
    private static readonly TextureSourceSwizzle[][] SwizzleSources = [
        [ ], // 0
        [ TextureSourceSwizzle.Red, TextureSourceSwizzle.Green, TextureSourceSwizzle.Blue, TextureSourceSwizzle.Alpha ], // 1
        [ TextureSourceSwizzle.RG, TextureSourceSwizzle.GB, TextureSourceSwizzle.BA ], // 2
        [ TextureSourceSwizzle.RGB ], // 3
        [ TextureSourceSwizzle.RGBA ], // 4
    ];
    private static readonly string[][] SwizzleSourceNames = SwizzleSources.Select(ss => ss.Select(s => s.ToString()).ToArray()).ToArray();

    private static readonly PackingPreset[] Presets = [
        new PackingPreset("ALBD", DxgiFormat.BC7_UNORM_SRGB, new InputTexture("Albedo", TextureSourceSwizzle.RGB, TextureSwizzle.RGB), new InputTexture("Dielectric", TextureSourceSwizzle.Red, TextureSwizzle.Alpha)),
        new PackingPreset("ALBM", DxgiFormat.BC7_UNORM_SRGB, new InputTexture("Albedo", TextureSourceSwizzle.RGB, TextureSwizzle.RGB), new InputTexture("Metallic", TextureSourceSwizzle.Red, TextureSwizzle.Alpha)),
        new PackingPreset("ATOC", DxgiFormat.BC7_UNORM,
            new InputTexture("Alpha", TextureSourceSwizzle.Red, TextureSwizzle.Red),
            new InputTexture("Translucency", TextureSourceSwizzle.Green, TextureSwizzle.Green),
            new InputTexture("Ambient Occlusion", TextureSourceSwizzle.Blue, TextureSwizzle.Blue),
            new InputTexture("Cavity", TextureSourceSwizzle.Alpha, TextureSwizzle.Alpha)),
        new PackingPreset("ATOD", DxgiFormat.BC7_UNORM,
            new InputTexture("Alpha", TextureSourceSwizzle.Red, TextureSwizzle.Red),
            new InputTexture("Translucency", TextureSourceSwizzle.Green, TextureSwizzle.Green),
            new InputTexture("Ambient Occlusion", TextureSourceSwizzle.Blue, TextureSwizzle.Blue),
            new InputTexture("Dirt Mask", TextureSourceSwizzle.Alpha, TextureSwizzle.Alpha)),
        new PackingPreset("ATOS", DxgiFormat.BC7_UNORM,
            new InputTexture("Alpha", TextureSourceSwizzle.Red, TextureSwizzle.Red),
            new InputTexture("Translucency", TextureSourceSwizzle.Green, TextureSwizzle.Green),
            new InputTexture("Ambient Occlusion", TextureSourceSwizzle.Blue, TextureSwizzle.Blue),
            new InputTexture("Subsurface Scattering", TextureSourceSwizzle.Alpha, TextureSwizzle.Alpha)),
        new PackingPreset("NRRA", DxgiFormat.BC7_UNORM,
            new InputTexture("Normal", TextureSourceSwizzle.RG, TextureSwizzle.GA_NRRx),
            new InputTexture("Roughness", TextureSourceSwizzle.Red, TextureSwizzle.Red),
            new InputTexture("Alpha", TextureSourceSwizzle.Red, TextureSwizzle.Blue)),
        new PackingPreset("NRRC", DxgiFormat.BC7_UNORM,
            new InputTexture("Normal", TextureSourceSwizzle.RG, TextureSwizzle.GA_NRRx),
            new InputTexture("Roughness", TextureSourceSwizzle.Red, TextureSwizzle.Red),
            new InputTexture("Cavity", TextureSourceSwizzle.Red, TextureSwizzle.Blue)),
        new PackingPreset("NRRT", DxgiFormat.BC7_UNORM,
            new InputTexture("Normal", TextureSourceSwizzle.RG, TextureSwizzle.GA_NRRx),
            new InputTexture("Roughness", TextureSourceSwizzle.Red, TextureSwizzle.Red),
            new InputTexture("Translucency", TextureSourceSwizzle.Red, TextureSwizzle.Blue)),
        new PackingPreset("NRRO", DxgiFormat.BC7_UNORM,
            new InputTexture("Normal", TextureSourceSwizzle.RG, TextureSwizzle.GA_NRRx),
            new InputTexture("Roughness", TextureSourceSwizzle.Red, TextureSwizzle.Red),
            new InputTexture("Occlusion", TextureSourceSwizzle.Red, TextureSwizzle.Blue)),
        new PackingPreset("NRMR", DxgiFormat.BC7_UNORM,
            new InputTexture("Normal", TextureSourceSwizzle.RG, TextureSwizzle.RG),
            new InputTexture("Roughness", TextureSourceSwizzle.Red, TextureSwizzle.Alpha)),
        new PackingPreset("Custom", DxgiFormat.BC7_UNORM,
            new InputTexture("Tex1", TextureSourceSwizzle.Red, TextureSwizzle.Red),
            new InputTexture("Tex2", TextureSourceSwizzle.Green, TextureSwizzle.Green),
            new InputTexture("Tex3", TextureSourceSwizzle.Blue, TextureSwizzle.Blue),
            new InputTexture("Tex4", TextureSourceSwizzle.Alpha, TextureSwizzle.Alpha))
    ];

    private static readonly string[] PresetNames = Presets.Select(p => p.name).ToArray();
    private static readonly string[] PresetFullNames = Presets.Select(p => TextureViewer.TextureTypeNames.GetValueOrDefault(p.name, p.name)).ToArray();

    private class TextureSlot
    {
        public InputTexture input;
        public Texture? texture;
        public FileHandle? file;
        public TextureSourceSwizzle inputSwizzle;
        public bool invertSource;
        public TextureSwizzle outputSwizzle;

        public TextureSlot(InputTexture input, Texture? texture, FileHandle? file, TextureSourceSwizzle inputSwizzle, TextureSwizzle outputSwizzle)
        {
            this.input = input;
            this.texture = texture;
            this.file = file;
            this.inputSwizzle = inputSwizzle;
            this.outputSwizzle = outputSwizzle;
        }

        public void UpdateSwizzle()
        {
            var channel = inputSwizzle switch {
                TextureSourceSwizzle.Red => Texture.TextureChannel.Red,
                TextureSourceSwizzle.Green => Texture.TextureChannel.Green,
                TextureSourceSwizzle.Blue => Texture.TextureChannel.Blue,
                TextureSourceSwizzle.Alpha => Texture.TextureChannel.Alpha,
                TextureSourceSwizzle.RGB => Texture.TextureChannel.RGB,
                TextureSourceSwizzle.RG => Texture.TextureChannel.RG,
                TextureSourceSwizzle.GB => Texture.TextureChannel.GB,
                TextureSourceSwizzle.BA => Texture.TextureChannel.BA,
                _ => Texture.TextureChannel.RGBA,
            };
            texture?.SetChannel(channel);
        }
    }

    private record InputTexture(string Name, TextureSourceSwizzle inputSwizzle, TextureSwizzle outputSwizzle);
    private record PackingPreset(string name, DxgiFormat defaultFormat, params InputTexture[] slots)
    {
        public PackingPreset Instantiate()
        {
            return new PackingPreset(name, defaultFormat, slots.Select(s => new InputTexture(s.Name, s.inputSwizzle, s.outputSwizzle)).ToArray());
        }
    }

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
        workspace = context.GetWorkspace()!;
        convertTemplateConfig = TexFile.GetGameVersionConfigs(workspace.Game).First();
    }

    public void OnIMGUI()
    {
        var selectedName = selectedPreset?.name;
        if (ImguiHelpers.ValueCombo("Preset", PresetFullNames, PresetNames, ref selectedName, 800)) {
            if (selectedPreset != null) {
                foreach (var s in slots) {
                    s.texture?.Dispose();
                }
                slots.Clear();
            }
            if (string.IsNullOrEmpty(selectedName)) {
                selectedPreset = null;
            } else {
                selectedPreset = Presets.First(p => p.name == selectedName).Instantiate();
                exportFormat = selectedPreset.defaultFormat;
                foreach (var src in selectedPreset.slots) {
                    slots.Add(new TextureSlot(src, null, null, src.inputSwizzle, src.outputSwizzle));
                }
                isDirty = true;
                outputTexture?.Dispose();
                outputTexture = null;
            }
        }

        if (selectedPreset == null) {
            return;
        }

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 16);
        var topPos = ImGui.GetCursorPos();
        ImGui.Indent(32);
        ImGui.Dummy(new Vector2(1, 1));

        foreach (var slot in slots) {
            ShowSlot(slot);
        }
        ImGui.Unindent(32);
        if (isDirty) {
            StartProcessImage();
            isDirty = false;
        }

        var outputPos = topPos + new Vector2(220, 0);
        var outputBottomMargin = new Vector2(0, 200);
        ImGui.Indent(outputPos.X);
        if (outputTexture != null) {
            ImGui.SetCursorPosY(outputPos.Y);
            if (ImguiHelpers.CSharpEnumCombo("Display", ref outputDisplayChannel, 800)) {
                outputTexture.SetChannel(outputDisplayChannel);
            }
            ImGui.Text("Size: " + outputTexture.Width + " x " + outputTexture.Height + " (Based on first texture slot)");
            ImGui.Spacing();

            var outputSize = ImGui.GetWindowSize() - new Vector2(outputPos.X, outputPos.Y) - ImGui.GetStyle().WindowPadding * 2 - outputBottomMargin - new Vector2(64, 0);
            ImGui.Image(outputTexture.AsTextureRef(), outputSize);

            ImGui.SeparatorText("Saving");

            var leftEdge = ImGui.GetCursorPosX();
            var save1 = ImGui.Button("Save As ...");
            ImguiHelpers.Tooltip("Save the current texture to a standard file format");
            if (save1) {
                PlatformUtils.ShowSaveFileDialog((outpath) => MainLoop.Instance.InvokeFromUIThread(() => {
                    if (Path.GetExtension(outpath) == ".dds") {
                        MainLoop.Instance.BackgroundTasks.Queue(new TextureConversionTask(outputTexture.GetAsDDS(), (outDDS) => {
                            using var fs = File.Create(outpath);
                            outDDS.FileHandler.Seek(0);
                            outDDS.FileHandler.Stream.CopyTo(fs);
                        }, GetOperations()));
                    } else {
                        outputTexture?.SaveAs(outpath);
                    }
                }), filter: FileFilters.TextureFile);
            }
            ImGui.SameLine();
            var save2 = ImGui.Button("Convert ...");
            ImguiHelpers.Tooltip("Save the current texture to the selected .tex file format");
            if (save2) {
                var ver = TexFile.GetFileExtension(convertTemplateConfig);
                var ext = $".tex.{ver}";
                var defaultName = selectedPreset.name + ext;
                var dds = outputTexture.GetAsDDS();
                MainLoop.Instance.BackgroundTasks.Queue(new TextureConversionTask(dds, (outDDS) => {
                    var tex = new TexFile(new FileHandler());
                    tex.ChangeVersion(convertTemplateConfig);
                    tex.LoadDataFromDDS(outDDS);
                    PlatformUtils.ShowSaveFileDialog((path) => {
                        MainLoop.Instance.InvokeFromUIThread(() => {
                            TextureLoader.SaveTo(tex, path);
                            dds.Dispose();
                            tex.Dispose();
                        });
                    }, defaultName);
                }, GetOperations()));
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.CalcItemWidth() - (ImGui.GetCursorPosX() - leftEdge));
            ImguiHelpers.ValueCombo("Tex Version", TexFile.AllVersionConfigsWithExtension, TexFile.AllVersionConfigs, ref convertTemplateConfig);
            if (ImguiHelpers.ValueCombo("DXGI Format", TextureViewer.DxgiFormatStrings, TextureViewer.DxgiFormats, ref exportFormat)) {
                isDirty = true;
            }
            if (ImGui.Checkbox("Generate Mip Maps", ref exportMipMaps)) {
                isDirty = true;
            }
        }

        ImGui.Unindent(outputPos.X);
    }

    private TextureConversionTask.TextureOperation[] GetOperations() => exportMipMaps
        ? new TextureConversionTask.TextureOperation[] { new TextureConversionTask.ChangeFormat(exportFormat), new TextureConversionTask.GenerateMipMaps() }
        : [new TextureConversionTask.ChangeFormat(exportFormat)];

    private void StartProcessImage()
    {
        if (slots.Count == 0) return;

        var firstTex = slots.FirstOrDefault(s => s.texture != null)?.texture;
        if (firstTex == null) {
            return;
        }

        Int2 res = new Int2(firstTex.Width, firstTex.Height);
        SixLabors.ImageSharp.Image<Rgba32>? outImageData = new Image<Rgba32>(res.x, res.y);
        foreach (var grp in outImageData.GetPixelMemoryGroup()) grp.Span.Fill(new Rgba32(0x00000000));
        foreach (var slot in slots) {
            if (slot.texture != null) {
                using var srcImg = slot.texture.GetAsImage();
                var img = srcImg.Clone();
                if (img.Width != res.x || img.Height != res.y) {
                    img.Mutate(o => o.Resize(res.x, res.y, KnownResamplers.Bicubic));
                }

                var targetPixels = outImageData.GetPixelMemoryGroup();
                var currentPixels = img.GetPixelMemoryGroup();
                for (int y = 0; y < targetPixels.Count; ++y) {
                    var cspan = currentPixels[y].Span;
                    var tspan = targetPixels[y].Span;
                    var size = cspan.Length;
                    for (int x = 0; x < size; ++x) {
                        Rgba32 pixel = tspan[x];
                        switch (slot.inputSwizzle) {
                            case TextureSourceSwizzle.Red: pixel = new Rgba32(cspan[x].R, 0, 0, 0); break;
                            case TextureSourceSwizzle.Green: pixel = new Rgba32(cspan[x].G, 0, 0, 0); break;
                            case TextureSourceSwizzle.Blue: pixel = new Rgba32(cspan[x].B, 0, 0, 0); break;
                            case TextureSourceSwizzle.Alpha: pixel = new Rgba32(cspan[x].A, 0, 0, 0); break;
                            case TextureSourceSwizzle.RG: pixel = new Rgba32(cspan[x].R, cspan[x].G, 0, 0); break;
                            case TextureSourceSwizzle.GB: pixel = new Rgba32(cspan[x].G, cspan[x].B, 0, 0); break;
                            case TextureSourceSwizzle.BA: pixel = new Rgba32(cspan[x].B, cspan[x].A, 0, 0); break;
                            case TextureSourceSwizzle.RGB: pixel = new Rgba32(cspan[x].R, cspan[x].G, cspan[x].B, 0); break;
                        }
                        if (slot.invertSource) pixel = new Rgba32((byte)(255 - pixel.R), (byte)(255 - pixel.G), (byte)(255 - pixel.B), (byte)(255 - pixel.A));

                        switch (slot.outputSwizzle) {
                            case TextureSwizzle.Red: tspan[x].R = pixel.R; break;
                            case TextureSwizzle.Green: tspan[x].G = pixel.R; break;
                            case TextureSwizzle.Blue: tspan[x].B = pixel.R; break;
                            case TextureSwizzle.Alpha: tspan[x].A = pixel.R; break;
                            case TextureSwizzle.RG: tspan[x].R = pixel.R; tspan[x].G = pixel.G; break;
                            case TextureSwizzle.GB: tspan[x].G = pixel.R; tspan[x].B = pixel.G; break;
                            case TextureSwizzle.BA: tspan[x].B = pixel.R; tspan[x].A = pixel.G; break;
                            case TextureSwizzle.RGB: tspan[x].R = pixel.R; tspan[x].G = pixel.G; tspan[x].B = pixel.B; break;
                            case TextureSwizzle.RGBA: tspan[x] = pixel; break;
                            case TextureSwizzle.GA_NRRx: {
                                var vec = new Vector2(pixel.R / 127.5f - 1, pixel.G / 127.5f - 1);
                                const float Cos45 = 0.70710678118654752440084436210485f;
                                vec = new Vector2(Cos45 * vec.X - Cos45 * vec.Y, Cos45 * vec.X + Cos45 * vec.Y);
                                var signs = new Vector2(Math.Sign(vec.X), Math.Sign(vec.Y));

                                vec = new Vector2(MathF.Sqrt(MathF.Abs(vec.X)), MathF.Sqrt(MathF.Abs(vec.Y))) * signs;

                                tspan[x].A = (byte)Math.Round((vec.X + 1f) * 127.5f);
                                tspan[x].G = (byte)Math.Round((vec.Y + 1f) * 127.5f);
                                break;
                            }
                        }
                    }
                }

                if (img != outImageData) img.Dispose();
            }
        }

        if (outImageData != null) {
            outputTexture?.Dispose();
            outputTexture = new Texture();
            outputTexture.LoadFromImage(outImageData);
            outputTexture.SetChannel(outputDisplayChannel);
            outImageData.Dispose();

            if (activeTask != null) {
                MainLoop.Instance.BackgroundTasks.CancelTask(activeTask);
            }

            MainLoop.Instance.BackgroundTasks.Queue(activeTask = new TextureConversionTask(outputTexture.GetAsDDS(0, 1), (outDDS) => {
                MainLoop.Instance.InvokeFromUIThread(() => {
                    outputTexture.LoadFromDDS(outDDS);
                    outDDS.Dispose();
                });
            }, GetOperations()));
        }
    }

    public void OnWindow()
    {
        if (!ImguiHelpers.BeginWindow(data)) {
            WindowManager.Instance.CloseWindow(data);
            return;
        }
        ImGui.BeginGroup();
        OnIMGUI();
        ImGui.EndGroup();
        ImGui.End();
    }

    private unsafe void ShowSlot(TextureSlot slot)
    {
        ImGui.PushID(slot.input.Name);
        var size = new Vector2(128, 128);
        ImGui.PushItemWidth(size.X);
        ImguiHelpers.TextCentered(slot.input.Name + " => " + slot.outputSwizzle, size.X);

        bool click;
        var btnPos = ImGui.GetCursorScreenPos();
        if (slot.texture != null) {
            click = ImGui.ImageButton(slot.input.Name, slot.texture.AsTextureRef(), size - ImGui.GetStyle().FramePadding * 2);
            if (ImGui.IsItemHovered() && ImGui.BeginTooltip()) {
                ImGui.Text("Right click for more options.");
                ImGui.Image(slot.texture.AsTextureRef(), new Vector2(512, 512));
                ImGui.EndTooltip();
            }
            if (ImGui.BeginPopupContextItem()) {
                if (ImGui.Selectable("Clear")) {
                    ReplaceSlotTexture(slot, null);
                }
                if (ImGui.Selectable("Make Black")) {
                    ReplaceSlotTexture(slot, new SixLabors.ImageSharp.Color(new Rgba32(0xff000000)));
                }
                if (ImGui.Selectable("Make White")) {
                    ReplaceSlotTexture(slot, new SixLabors.ImageSharp.Color(new Rgba32(0xffffffff)));
                }
                ImGui.EndPopup();
            }
        } else {
            click = ImGui.Button(slot.input.Name, size);
            ImguiHelpers.Tooltip("Click to browse for a texture or drag & drop it into the box. Right click for more options.");
            if (ImGui.BeginPopupContextItem()) {
                if (ImGui.Selectable("Make Black")) {
                    ReplaceSlotTexture(slot, new SixLabors.ImageSharp.Color(new Rgba32(0xff000000)));
                }
                if (ImGui.Selectable("Make White")) {
                    ReplaceSlotTexture(slot, new SixLabors.ImageSharp.Color(new Rgba32(0xffffffff)));
                }
                ImGui.EndPopup();
            }
        }

        if (ImGui.BeginDragDropTarget()) {
            var payload2 = ImGui.GetDragDropPayload();
            if (payload2.Handle != null && payload2.IsDataType(ImguiHelpers.DragDrop_File)) {
                var payloadPtr = ImGui.AcceptDragDropPayload(ImguiHelpers.DragDrop_File);
                var data = EditorWindow.CurrentWindow?.DragDropData;
                ImGui.GetForegroundDrawList().AddRectFilled(btnPos, btnPos + size, (ImguiHelpers.GetColorU32(ImGuiCol.PlotHistogramHovered) & 0x00ffffff | 0xaa000000));
                if (payloadPtr.Handle != null) {
                    if (data != null && data.filenames?.Length >= 1) {
                        ReplaceSlotTexture(slot, data.filenames[0]);
                    }
                    EditorWindow.CurrentWindow!.ConsumeDragDrop();
                }
            }
            ImGui.EndDragDropTarget();
        }
        if (click) {
            var wnd = EditorWindow.CurrentWindow!;
            PlatformUtils.ShowFileDialog((files) => {
                wnd.InvokeFromUIThread(() => {
                    ReplaceSlotTexture(slot, files[0]);
                    isDirty = true;
                });
            }, slot.texture?.Path, fileExtension: FileFilters.TextureFilesAll);
        }

        var channelCount = BitOperations.PopCount((uint)slot.outputSwizzle);
        if (ImguiHelpers.ValueCombo("##swizzle", SwizzleSourceNames[channelCount], SwizzleSources[channelCount], ref slot.inputSwizzle)) {
            isDirty = true;
            slot.UpdateSwizzle();
        }
        if (ImGui.Checkbox("Invert", ref slot.invertSource)) {
            isDirty = true;
        }
        ImGui.PopItemWidth();
        ImGui.PopID();
    }

    private void ReplaceSlotTexture(TextureSlot slot, string? texPath)
    {
        try {
            slot.texture?.Dispose();
            if (texPath != null) {
                slot.texture = new Texture();
                slot.texture.LoadFromFile(texPath);
                slot.UpdateSwizzle();
            } else {
                slot.texture = null;
            }
            isDirty = true;
        } catch (Exception e) {
            Logger.Error("Failed to replace texture slot: " + e.Message);
        }
    }

    private void ReplaceSlotTexture(TextureSlot slot, SixLabors.ImageSharp.Color fillColor)
    {
        try {
            slot.texture?.Dispose();
            slot.texture = new Texture();
            var img = new Image<Rgba32>(128, 128);
            foreach (var mem in img.GetPixelMemoryGroup()) {
                mem.Span.Fill(fillColor);
            }
            slot.texture.LoadFromImage(img);
            img.Dispose();
            isDirty = true;
        } catch (Exception e) {
            Logger.Error("Failed to replace texture slot: " + e.Message);
        }
    }

    public bool RequestClose()
    {
        return false;
    }

    public void Dispose()
    {
        foreach (var slot in slots) {
            slot.texture?.Dispose();
        }
    }
}