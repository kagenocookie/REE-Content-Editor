using System.Numerics;
using System.Runtime.InteropServices;
using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Msg;
using ReeLib.Uvs;

namespace ContentEditor.App.ImguiHandling;

public sealed class UVSequenceFileEditor : FileEditor, IWorkspaceContainer, IDisposable
{
    public override string HandlerName => "UV Sequence Editor";
    public string Filename => Handle.Filepath;

    public UvsFile File { get; }

    public ContentWorkspace Workspace { get; }

    private readonly Dictionary<string, Texture?> referencedTextures = new();

    public UVSequenceFileEditor(ContentWorkspace env, FileHandle file) : base(file)
    {
        Workspace = env;
        File = file.GetFile<UvsFile>();
        windowFlags = ImGuiWindowFlags.HorizontalScrollbar;
    }

    protected override void OnFileSaved()
    {
        base.OnFileSaved();
        Reset();
    }

    protected override void Reset()
    {
        selectedPattern = null;
    }

    private sealed class MessageNameComparer : Singleton<MessageNameComparer>, IComparer<MessageEntry>
    {
        public int Compare(MessageEntry? x, MessageEntry? y) => x == null || y == null ? 0 : x.Header.entryName.CompareTo(y.Header.entryName);
    }
    private sealed class MessageTextComparer(int langIndex) : IComparer<MessageEntry>
    {
        public int Compare(MessageEntry? x, MessageEntry? y) => x == null || y == null ? 0 : x.Strings[langIndex].CompareTo(y.Strings[langIndex]);

        public static readonly MessageTextComparer[] LanguageComparers = Enum.GetValues<Language>().Select(l => new MessageTextComparer((int)l)).ToArray();
    }

    private record struct IndexedItem(MessageEntry entry, int index);
    private float imageScale = 1f;

    [StructLayout(LayoutKind.Sequential)]
    private class SequencePatternArrangementHelper()
    {
        public int x = 1;
        public int y = 1;
        public Vector2 topLeftMargin;
        public Vector2 botRightMargin;
        public Vector2 padding;
        public bool autoRearrange;

        public void Infer(SequenceBlock sequence)
        {
            if (sequence.patterns.Count == 0) return;

            x = 0;
            y = 0;
            topLeftMargin.X = 5;
            topLeftMargin.Y = 5;
            var lastX = -1f;
            var lastY = -1f;
            foreach (var p in sequence.patterns) {
                if (p.right > lastX) {x++; lastX = p.right; }
                if (p.bottom > lastY) {y++; lastY = p.bottom; }
                if (p.left < topLeftMargin.X) topLeftMargin.X = p.left;
                if (p.top < topLeftMargin.Y) topLeftMargin.Y = p.top;
            }
            botRightMargin.X = 1 - lastX;
            botRightMargin.Y = 1 - lastY;
        }

        public void Rearrange(SequenceBlock sequence)
        {
            var textureIndices = sequence.patterns.Select(p => p.textureIndex).Distinct().ToList();
            if (textureIndices.Count > 1) {
                Logger.Error("Can't auto re-arrange: sequence has multiple textures defined");
                return;
            }

            var listBefore = new List<UvsPattern>(sequence.patterns);
            var texIndex = textureIndices.Count == 0 ? 0 : textureIndices[0];
            sequence.patterns.Clear();
            var outerArea = new Vector2(topLeftMargin.X, topLeftMargin.Y);
            var itemSize = new Vector2(1f / x, 1f / y);
            for (int i = 0; i < x; ++i) {
                for (int k = 0; k < y; ++k) {
                    sequence.patterns.Add(new UvsPattern() {
                        left = topLeftMargin.X + i * itemSize.X * (1f - botRightMargin.X - topLeftMargin.X) + padding.X * itemSize.X,
                        top = topLeftMargin.Y + k * itemSize.Y * (1f - botRightMargin.Y - topLeftMargin.Y) + padding.Y * itemSize.Y,
                        right = topLeftMargin.X + (i + 1) * itemSize.X * (1f - botRightMargin.X - topLeftMargin.X) - padding.X * itemSize.X,
                        bottom = topLeftMargin.Y + (k + 1) * itemSize.Y * (1f - botRightMargin.Y - topLeftMargin.Y) - padding.Y * itemSize.Y,
                        textureIndex = texIndex,
                    });
                }
            }
            var listAfter = new List<UvsPattern>(sequence.patterns);
            UndoRedo.RecordCallback(null, () => {
                sequence.patterns.Clear();
                sequence.patterns.AddRange(listAfter);
            }, () => {
                sequence.patterns.Clear();
                sequence.patterns.AddRange(listBefore);
            });
            sequence.patternCount = sequence.patterns.Count;
        }

        public void OnIMGUI(SequenceBlock sequence)
        {
            var changed = ImGui.DragInt2("Count X/Y", ref x, 0.025f, 1, 20);

            changed = ImGui.DragFloat2("Margin Top/Left", ref topLeftMargin, 0.001f, 0, 1) || changed;
            changed = ImGui.DragFloat2("Margin Bottom/Right", ref botRightMargin, 0.001f, 0, 1) || changed;
            changed = ImGui.DragFloat2("Item Padding", ref padding, 0.001f, 0, 0.4999f) || changed;
            ImGui.Checkbox("Automatically re-arrange", ref autoRearrange);

            if (autoRearrange ? changed : ImguiHelpers.SameLine() && ImGui.Button("Re-arrange")) {
                Rearrange(sequence);
            }
            if (ImguiHelpers.SameLine() && ImGui.Button("Reset")) {
                Infer(sequence);
            }
        }
    }

    protected override void DrawFileContents()
    {
        ImGui.PushID(Filename);
        if (File.Sequences.Count == 0) {
            File.Read();
        }

        var workspace = context.GetWorkspace();

        ImGui.SliderFloat("Image scale", ref imageScale, 0.05f, 5f);

        var seqlist = Enumerable.Range(0, File.Sequences.Count).Select(n => n.ToString()).ToArray();
        if (ImguiHelpers.Tabs(seqlist, ref selectedSequenceIndex, false, "Sequences")) {
            selectedPattern = null;
        }
        ImGui.SameLine();
        if (ImGui.Button("New sequence")) {
            selectedSequenceIndex =  File.Sequences.Count;
            UndoRedo.RecordListAdd(context, File.Sequences, new SequenceBlock() { patterns = new List<UvsPattern>() { new UvsPattern() { bottom = 1, right = 1 } }});
            selectedPattern = null;
            Handle.Modified = true;
        }
        if (selectedSequence != null && ImguiHelpers.SameLine() && ImGui.Button("Delete sequence")) {
            EditorWindow.CurrentWindow!.AddSubwindow(new ConfirmationDialog(
                "Deleting sequence",
                "Are you sure you wish to delete the sequence " + selectedSequenceIndex + "?",
                context.GetWindow() ?? throw new Exception("Missing parent window"),
                () => {
                    UndoRedo.RecordListRemove(context, File.Sequences, selectedSequence);
                    context.ClearChildren();
                }
            ));
        }

        var sequence = selectedSequenceIndex >= 0 && selectedSequenceIndex < File.Sequences.Count ? File.Sequences[selectedSequenceIndex] : null;
        this.selectedSequence = sequence;

        ImGui.BeginChild("Sequence data", new System.Numerics.Vector2(), ImGuiChildFlags.ResizeX|ImGuiChildFlags.Borders);
        if (sequence != null) {
            var seqCtx = context.GetChild(sequence) ?? context.AddChild($"Sequence {selectedSequenceIndex}", sequence);
            ImGui.Text(seqCtx.label);
            if (ImguiHelpers.SameLine() && ImGui.Button("Add pattern")) {
                UndoRedo.RecordListAdd(context, sequence.patterns, new UvsPattern());
            }
            if (ImGui.TreeNode("Arrangement")) {
                var arrange = seqCtx.GetChildValue<SequencePatternArrangementHelper>();
                if (arrange == null) {
                    arrange = new SequencePatternArrangementHelper();
                    seqCtx.AddChild("Arrangement", arrange);
                    arrange.Infer(sequence);
                }

                arrange.OnIMGUI(sequence);
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Overview")) {
                foreach (var texId in sequence.patterns.Select(p => p.textureIndex).Distinct()) {
                    var texPath = File.Textures[texId].path;
                    var tex = GetOrLoadTexture(texPath);
                    if (tex == null) {
                        ImGui.TextColored(Colors.Warning, "Could not find texture " + texPath);
                    } else {
                        ImGui.Text(texPath);
                        var maxwidth = ImGui.GetWindowSize().X - ImGui.GetCursorPosX() - 40;
                        var scale = (maxwidth / tex.Width);
                        var size = new System.Numerics.Vector2(maxwidth, scale * tex.Height);
                        var imageStart = ImGui.GetCursorScreenPos();
                        ImguiHelpers.BeginRect();
                        ImGui.Image((nint)tex.Handle, size);
                        ImguiHelpers.EndRect(0);
                        var afterImagePos = ImGui.GetCursorPos();

                        var drawlist = ImGui.GetWindowDrawList();
                        var pi = 0;
                        foreach (var pattern in sequence.patterns) {
                            if (pattern.textureIndex != texId) continue;

                            var (uv0, uv1) = pattern.GetBoundingPoints();
                            drawlist.AddRect(imageStart + size * uv0, imageStart + size * uv1, (ImguiHelpers.GetColor(ImGuiCol.Border) with { W = 0.3f }).ToArgb());
                            ImGui.SetCursorScreenPos(imageStart + size * uv0);

                            ImGui.BeginGroup();
                            var patSize = new System.Numerics.Vector2(tex.Width * scale * (uv1.X - uv0.X), tex.Height * scale * (uv1.Y - uv0.Y));
                            using var btncol = ImguiHelpers.OverrideStyleCol(ImGuiCol.Button, 0);
                            using var hovcol = ImguiHelpers.OverrideStyleCol(ImGuiCol.ButtonHovered, ImguiHelpers.GetColor(ImGuiCol.ButtonHovered) with { W = 0.25f });
                            if (ImGui.Button($"##{pi++}", patSize)) {
                                selectedPattern = pattern;
                            }
                            ImGui.EndGroup();
                        }

                        ImGui.SetCursorPos(afterImagePos);
                    }
                }
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Individual Patterns")) {
                int patId = 0;
                foreach (var pattern in sequence.patterns) {
                    if (ImguiHelpers.TreeNodeSuffix($"Pattern {patId++}", $"[{pattern}]")) {
                        ShowPatternEdit(pattern);
                        ImGui.TreePop();
                    }
                }
                ImGui.TreePop();
            }
        }

        ImGui.EndChild();
        if (sequence != null) {
            ImGui.SameLine();
            ImGui.BeginChild("Edit", new System.Numerics.Vector2(), ImGuiChildFlags.AutoResizeX|ImGuiChildFlags.Borders);
            ImGui.Text("Sequence " + File.Sequences.IndexOf(sequence));
            ImGui.Checkbox("Animate", ref shouldAnimate);
            if (shouldAnimate && sequence.patterns.Count > 0) {
                var pat = sequence.patterns[0];
                var texPath = File.Textures[pat.textureIndex].path;
                var tex = GetOrLoadTexture(texPath);
                if (tex != null) {
                    var (uv0, uv1) = pat.GetBoundingPoints();
                    var size = new Vector2(tex.Width, tex.Height) * (uv1 - uv0);
                    AnimationSequence(sequence, size, ref animationFrame, ref animationTime, ref animationColor);
                }
            }

            if (selectedPattern != null) {
                ImGui.SeparatorText("Selected pattern");
                ImGui.Text(selectedPattern.ToString());
                ShowPatternEdit(selectedPattern);
            }
            ImGui.EndChild();
        }
        ImGui.PopID();
    }

    private bool shouldAnimate;
    private UvsPattern? selectedPattern;
    private SequenceBlock? selectedSequence;
    private int selectedSequenceIndex = 0;
    private int animationFrame;
    private float animationTime;
    private Vector4 animationColor = new Vector4(1, 1, 1, 1);

    private void AnimationSequence(SequenceBlock sequence, Vector2 size, ref int frame, ref float time, ref Vector4 color, float fps = 60)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fps, 1);
        time += Time.Delta;
        if (time > 1 / fps) {
            time -= 1 / fps;
            frame++;
        }

        if (frame >= sequence.patternCount) {
            frame = 0;
        }

        var pattern = sequence.patterns[frame];
        var texPath = File.Textures[pattern.textureIndex].path;
        var tex = GetOrLoadTexture(texPath);
        if (tex != null) {
            ImGui.ColorEdit4("Color tint", ref color);
            var (uv0, uv1) = pattern.GetBoundingPoints();
            // var size = new Vector2(tex.Width, tex.Height) * (uv1 - uv0);
            ImGui.Image(tex, size, uv0, uv1, color);
        }
    }

    private void ShowPatternEdit(UvsPattern pattern)
    {
        var texObject = File.Textures[pattern.textureIndex];
        var texPath = texObject.path;
        var curseq = selectedSequence;
        var resourcePicker = context.GetChild<ResourcePathPicker>();
        if (resourcePicker == null || resourcePicker.target != texObject) {
            context.ClearChildren();
            resourcePicker = context.AddChild<TextureBlock, string>(
                "Texture",
                texObject,
                new ResourcePathPicker(context.GetWorkspace(), KnownFileFormats.Texture),
                (target) => target?.path,
                (target, newval) => {
                    UndoRedo.RecordCallbackSetter(context, target, target.path, newval, (p, newval) => {
                        if (File.Sequences.Any(s => s != curseq && s.patterns.Any(p => p.textureIndex == pattern.textureIndex))) {
                            // ensure we make a new texture block in case the same texture is already used by other sequences
                            pattern.textureIndex = File.Textures.Count;
                            File.Textures.Add(new TextureBlock() {
                                path = newval ?? string.Empty,
                            });
                        }
                        p.path = newval ?? string.Empty;
                    }, $"Pattern {pattern.GetHashCode()} Texture");
                }
            );
        }
        resourcePicker.ShowUI();
        ImGui.Spacing();

        var tex = GetOrLoadTexture(texPath);
        if (tex == null) {
            ImGui.TextColored(Colors.Danger, "Could not find texture");
        } else {
            var backup = (pattern.left, pattern.right);
            if (ImGui.DragFloatRange2("Left Right", ref pattern.left, ref pattern.right, 0.01f, 0, 1)) {
                UndoRedo.RecordCallbackSetter(context, pattern, backup, (pattern.left, pattern.right), static (p, vals) => (p.left, p.right) = vals, $"Pattern {pattern.GetHashCode()} LR");
                Handle.Modified = true;
            }
            backup = (pattern.top, pattern.bottom);
            if (ImGui.DragFloatRange2("Top Bottom", ref pattern.top, ref pattern.bottom, 0.01f, 0, 1)) {
                UndoRedo.RecordCallbackSetter(context, pattern, backup, (pattern.top, pattern.bottom), static (p, vals) => (p.top, p.bottom) = vals, $"Pattern {pattern.GetHashCode()} TB");
                Handle.Modified = true;
            }
            float drag = 0;
            if (ImGui.DragFloat("Move X", ref drag, 0.001f)) {
                var span = pattern.right - pattern.left;
                backup = (pattern.left, pattern.right);
                pattern.left = Math.Min(1 - span, Math.Clamp(pattern.left + drag, 0, 1));
                pattern.right = Math.Max(span, Math.Clamp(pattern.right + drag, 0, 1));
                UndoRedo.RecordCallbackSetter(context, pattern, backup, (pattern.left, pattern.right), static (p, vals) => (p.left, p.right) = vals, $"Pattern {pattern.GetHashCode()} X");
                Handle.Modified = true;
            }
            drag = 0;
            if (ImGui.DragFloat("Move Y", ref drag, 0.001f)) {
                var span = pattern.bottom - pattern.top;
                backup = (pattern.top, pattern.bottom);
                pattern.top = Math.Min(1 - span, Math.Clamp(pattern.top + drag, 0, 1));
                pattern.bottom = Math.Max(span, Math.Clamp(pattern.bottom + drag, 0, 1));
                UndoRedo.RecordCallbackSetter(context, pattern, backup, (pattern.top, pattern.bottom), static (p, vals) => (p.top, p.bottom) = vals, $"Pattern {pattern.GetHashCode()} Y");
                Handle.Modified = true;
            }
            ImguiHelpers.BeginRect();
            var (uv0, uv1) = pattern.GetBoundingPoints();
            ImGui.Image(
                (nint)tex.Handle,
                new System.Numerics.Vector2(tex.Width * (uv1.X - uv0.X), tex.Height * (uv1.Y - uv0.Y)),
                uv0,
                uv1
            );
            ImguiHelpers.EndRect(0);
        }
    }

    private Texture? GetOrLoadTexture(string path)
    {
        if (referencedTextures.TryGetValue(path, out var tex)) {
            return tex;
        }

        string? fullPath = null;
        var workspace = context.GetWorkspace();
        if (workspace == null) {
            return referencedTextures[path] = null;
        }
        var file = workspace.Env.FindSingleFile(path, out fullPath);
        if (file != null) {
            var texture = new Texture().LoadFromTex(file, fullPath!);
            referencedTextures[path] = texture;
            return texture;
        }

        return referencedTextures[path] = null;
    }

    protected override void Dispose(bool disposing)
    {
        foreach (var tex in referencedTextures.Values) {
            tex?.Dispose();
        }
        referencedTextures.Clear();
        base.Dispose(disposing);
    }
}
