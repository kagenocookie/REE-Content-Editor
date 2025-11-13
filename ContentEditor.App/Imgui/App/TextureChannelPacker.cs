using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.BackgroundTasks;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
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
    private bool exportMipMaps;
    private bool isDirty = false;

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
        NRRx = Green | Alpha,
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
        new PackingPreset("ALBD", new InputTexture("Albedo", TextureSourceSwizzle.RGB, TextureSwizzle.RGB), new InputTexture("Dielectric", TextureSourceSwizzle.Red, TextureSwizzle.Alpha)),
        new PackingPreset("ALBM", new InputTexture("Albedo", TextureSourceSwizzle.RGB, TextureSwizzle.RGB), new InputTexture("Metallic", TextureSourceSwizzle.Red, TextureSwizzle.Alpha)),
        new PackingPreset("ATOC",
            new InputTexture("Alpha", TextureSourceSwizzle.Red, TextureSwizzle.Red),
            new InputTexture("Translucency", TextureSourceSwizzle.Green, TextureSwizzle.Green),
            new InputTexture("Ambient Occlusion", TextureSourceSwizzle.Blue, TextureSwizzle.Blue),
            new InputTexture("Cavity", TextureSourceSwizzle.Alpha, TextureSwizzle.Alpha)),
        new PackingPreset("ATOD",
            new InputTexture("Alpha", TextureSourceSwizzle.Red, TextureSwizzle.Red),
            new InputTexture("Translucency", TextureSourceSwizzle.Green, TextureSwizzle.Green),
            new InputTexture("Ambient Occlusion", TextureSourceSwizzle.Blue, TextureSwizzle.Blue),
            new InputTexture("Dirt Mask", TextureSourceSwizzle.Alpha, TextureSwizzle.Alpha)),
        new PackingPreset("ATOS",
            new InputTexture("Alpha", TextureSourceSwizzle.Red, TextureSwizzle.Red),
            new InputTexture("Translucency", TextureSourceSwizzle.Green, TextureSwizzle.Green),
            new InputTexture("Ambient Occlusion", TextureSourceSwizzle.Blue, TextureSwizzle.Blue),
            new InputTexture("Subsurface Scattering", TextureSourceSwizzle.Alpha, TextureSwizzle.Alpha)),
        new PackingPreset("NRRA",
            new InputTexture("Normal", TextureSourceSwizzle.RG, TextureSwizzle.NRRx),
            new InputTexture("Roughness", TextureSourceSwizzle.Red, TextureSwizzle.Red),
            new InputTexture("Alpha", TextureSourceSwizzle.Red, TextureSwizzle.Blue)),
        new PackingPreset("NRRC",
            new InputTexture("Normal", TextureSourceSwizzle.RG, TextureSwizzle.NRRx),
            new InputTexture("Roughness", TextureSourceSwizzle.Red, TextureSwizzle.Red),
            new InputTexture("Cavity", TextureSourceSwizzle.Red, TextureSwizzle.Blue)),
        new PackingPreset("NRRT",
            new InputTexture("Normal", TextureSourceSwizzle.RG, TextureSwizzle.NRRx),
            new InputTexture("Roughness", TextureSourceSwizzle.Red, TextureSwizzle.Red),
            new InputTexture("Translucency", TextureSourceSwizzle.Red, TextureSwizzle.Blue)),
        new PackingPreset("NRRO",
            new InputTexture("Normal", TextureSourceSwizzle.RG, TextureSwizzle.NRRx),
            new InputTexture("Roughness", TextureSourceSwizzle.Red, TextureSwizzle.Red),
            new InputTexture("Occlusion", TextureSourceSwizzle.Red, TextureSwizzle.Blue)),
        new PackingPreset("NRMR",
            new InputTexture("Normal", TextureSourceSwizzle.RG, TextureSwizzle.RG),
            new InputTexture("Roughness", TextureSourceSwizzle.Red, TextureSwizzle.Alpha)),
        new PackingPreset("Custom",
            new InputTexture("Tex1", TextureSourceSwizzle.Red, TextureSwizzle.Red),
            new InputTexture("Tex2", TextureSourceSwizzle.Green, TextureSwizzle.Green),
            new InputTexture("Tex3", TextureSourceSwizzle.Blue, TextureSwizzle.Blue),
            new InputTexture("Tex4", TextureSourceSwizzle.Alpha, TextureSwizzle.Alpha))
    ];

    private PackingPreset? selectedPreset;

    private static readonly string[] PresetNames = Presets.Select(p => p.name).ToArray();

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
    private record PackingPreset(string name, params InputTexture[] slots)
    {
        public PackingPreset Instantiate()
        {
            return new PackingPreset(name, slots.Select(s => new InputTexture(s.Name, s.inputSwizzle, s.outputSwizzle)).ToArray());
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
        if (ImguiHelpers.ValueCombo("Preset", PresetNames, PresetNames, ref selectedName)) {
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
                foreach (var src in selectedPreset.slots) {
                    slots.Add(new TextureSlot(src, null, null, src.inputSwizzle, src.outputSwizzle));
                }
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
            if (ImguiHelpers.CSharpEnumCombo("Display", ref outputDisplayChannel)) {
                outputTexture.SetChannel(outputDisplayChannel);
            }
            ImGui.Text("Size: " + outputTexture.Width + " x " + outputTexture.Height + " (Based on first texture slot)");
            ImGui.Spacing();

            var outputSize = ImGui.GetWindowSize() - new Vector2(outputPos.X, outputPos.Y) - ImGui.GetStyle().WindowPadding * 2 - outputBottomMargin - new Vector2(64, 0);
            ImGui.Image((nint)outputTexture.Handle, outputSize);

            ImGui.SeparatorText("Saving");

            var save1 = ImGui.Button("Save As ...");
            ImguiHelpers.Tooltip("Save the current texture to a standard file format");
            if (save1) {
                PlatformUtils.ShowSaveFileDialog((outpath) => {
                    if (Path.GetExtension(outpath) == ".dds") {
                        MainLoop.Instance.BackgroundTasks.Queue(new TextureConversionTask(outputTexture.GetAsDDS(), (outDDS) => {
                            PlatformUtils.ShowSaveFileDialog((path) => {
                                MainLoop.Instance.InvokeFromUIThread(() => outDDS.SaveAs(path));
                            }, filter: "DDS (*.dds)|*.dds");
                        }, GetOperations()));
                    } else {
                        MainLoop.Instance.InvokeFromUIThread(() => outputTexture?.SaveAs(outpath));
                    }
                }, filter: TextureViewer.SaveFileFilter);
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
                            tex.SaveAs(path);
                            dds.Dispose();
                            tex.Dispose();
                        });
                    }, defaultName);
                }, GetOperations()));
            }
            ImGui.SameLine();
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
                    // TODO pick better quality sampler?
                    img.Mutate(o => o.Resize(res.x, res.y));
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
                        if (slot.invertSource) pixel = new Rgba32(1 - pixel.R, 1 - pixel.G, 1 - pixel.B, 1 - pixel.A);

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
                            case TextureSwizzle.NRRx: {
                                var vec = new Vector2(pixel.R, pixel.G) / 255f - new Vector2(127);
                                // TODO verify NRRx math correctness
                                const float Cos45 = 0.70710678118654752440084436210485f;
                                var signs = new Vector2(Math.Sign(vec.X), Math.Sign(vec.Y));
                                vec = new Vector2(Cos45 * vec.X - Cos45 * vec.Y, Cos45 * vec.X + Cos45 * vec.Y);
                                vec = vec * vec * signs;

                                tspan[x].A = (byte)Math.Round((vec.X + 127) * 255f);
                                tspan[x].G = (byte)Math.Round((vec.Y + 127) * 255f);
                                break;
                            }
                        }
                    }
                }

                if (img != outImageData) img.Dispose();
            }
        }

        if (outImageData != null) {
            outputTexture = new Texture();
            outputTexture.LoadFromImage(outImageData);
            outputTexture.SetChannel(outputDisplayChannel);
            outImageData.Dispose();

            MainLoop.Instance.BackgroundTasks.Queue(new TextureConversionTask(outputTexture.GetAsDDS(), (outDDS) => {
                MainLoop.Instance.InvokeFromUIThread(() => outputTexture.LoadFromDDS(outDDS));
            }, GetOperations()));
        }
    }

    public void OnWindow()
    {
        ImGui.SetNextWindowSize(new Vector2(800, 500), ImGuiCond.FirstUseEver);
        if (!ImguiHelpers.BeginWindow(data, null, ImGuiWindowFlags.MenuBar)) {
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
        ImguiHelpers.TextCentered(slot.input.Name, size.X);

        bool click;
        var btnPos = ImGui.GetCursorScreenPos();
        if (slot.texture != null) {
            click = ImGui.ImageButton(slot.input.Name, (nint)slot.texture.Handle, size - ImGui.GetStyle().FramePadding * 2);
            if (ImGui.IsItemHovered() && ImGui.BeginTooltip()) {
                ImGui.Text("Right click for more options.");
                ImGui.Image((nint)slot.texture.Handle, new Vector2(512, 512));
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
            if (payload2.NativePtr != null && payload2.IsDataType(ImguiHelpers.DragDrop_File)) {
                var payloadPtr = ImGui.AcceptDragDropPayload(ImguiHelpers.DragDrop_File);
                var data = EditorWindow.CurrentWindow?.DragDropData;
                ImGui.GetForegroundDrawList().AddRectFilled(btnPos, btnPos + size, (ImguiHelpers.GetColorU32(ImGuiCol.PlotHistogramHovered) & 0x00ffffff | 0xaa000000));
                if (payloadPtr.NativePtr != null) {
                    if (data != null && data.filenames?.Length >= 1) {
                        ReplaceSlotTexture(slot, data.filenames[0]);
                    }
                    EditorWindow.CurrentWindow!.ConsumeDragDrop();
                }
            }
            ImGui.EndDragDropTarget();
        }
        if (click) {
            PlatformUtils.ShowFileDialog((files) => {
                MainLoop.Instance.InvokeFromUIThread(() => {
                    ReplaceSlotTexture(slot, files[0]);
                    isDirty = true;
                });
            }, slot.texture?.Path, fileExtension: TextureViewer.OpenFileFilter);
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