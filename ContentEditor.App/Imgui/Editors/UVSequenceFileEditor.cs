using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Msg;
using ReeLib.Uvs;
using System.Numerics;
using System.Runtime.InteropServices;

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
    private bool shouldAnimate;
    private UvsPattern? selectedPattern;
    private SequenceBlock? selectedSequence;
    private int selectedSequenceIndex = 0;
    private int animationFrame;
    private float animationTime;
    private Vector4 animationColor = new Vector4(1, 1, 1, 1);
    private readonly int[] fpsOptions = new[] { 1, 12, 24, 30, 60 };
    private int selectedFpsIDX = 4;
    private float playbackFps => fpsOptions[selectedFpsIDX];
    private int? dragPatternIDX;
    private int? dragTargetIDX;
    private bool isDraggingPattern;

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
        var sequence = selectedSequenceIndex >= 0 && selectedSequenceIndex < File.Sequences.Count ? File.Sequences[selectedSequenceIndex] : null;
        this.selectedSequence = sequence;
        float minW = 350 * UI.UIScale;

        ImGui.Separator();
        ImGui.Spacing();
        ImGui.SetNextWindowSizeConstraints(new Vector2(minW, 0), new Vector2(minW * 3, float.MaxValue));
        ImGui.BeginChild("Sequence Tabs", new System.Numerics.Vector2(), ImGuiChildFlags.ResizeX | ImGuiChildFlags.Borders);
        if (sequence != null) {
            ShowTabs(sequence);
        }
        ImGui.EndChild();

        if (sequence != null && workspace != null) {
            ShowToolbar(sequence, workspace);
            ImGui.Spacing();
            ShowTimeline(sequence);
            if (selectedPattern != null) {
                ShowPreview(sequence);
                ImGui.SameLine();
                ShowSelectedPattern();
            }
            ImGui.EndChild();
        }
        ImGui.PopID();
    }
    private void ShowTabs(SequenceBlock sequence)
    {
        var seqCtx = context.GetChild(sequence) ?? context.AddChild($"Sequence {selectedSequenceIndex}", sequence);

        ImGui.BeginTabBar($"Sequence##{selectedSequenceIndex}");
        if (ImGui.BeginTabItem("Overview")) {
            ShowOverview(sequence);
            ImGui.SeparatorText("Grid Arrangement");
            ShowArrangement(sequence, seqCtx);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Individual Patterns")) {
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconSecondary);
            if (ImGui.Button($"{AppIcons.SI_GenericAdd}")) {
                UndoRedo.RecordListAdd(context, sequence.patterns, new UvsPattern());
                sequence.patternCount = sequence.patterns.Count;
            }
            ImGui.PopStyleColor();
            ImguiHelpers.Tooltip("Add pattern");
            ImGui.Separator();

            int patId = 0;
            foreach (var pattern in sequence.patterns) {
                if (ImguiHelpers.TreeNodeSuffix($"Pattern {patId++}", $"[{pattern}]")) {
                    ShowPatternEdit(pattern);
                    ImGui.TreePop();
                }
            }
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void ShowOverview(SequenceBlock sequence)
    {
        ImGui.Spacing();
        foreach (var texId in sequence.patterns.Select(p => p.textureIndex).Distinct()) {
            var texPath = File.Textures[texId].path;
            var tex = GetOrLoadTexture(texPath);
            if (tex == null) {
                ImGui.TextColored(Colors.Warning, "Could not find texture " + texPath);
            } else {
                var maxwidth = ImGui.GetWindowSize().X - ImGui.GetCursorPosX() - 40;
                var scale = (maxwidth / tex.Width);
                var size = new System.Numerics.Vector2(maxwidth, scale * tex.Height);
                var imageStart = ImGui.GetCursorScreenPos();
                ImguiHelpers.BeginRect();
                ImGui.Image(tex.AsTextureRef(), size);
                ImguiHelpers.EndRect(0);
                var afterImagePos = ImGui.GetCursorPos();

                var drawlist = ImGui.GetWindowDrawList();
                var pi = 0;
                foreach (var pattern in sequence.patterns) {
                    if (pattern.textureIndex != texId) continue;

                    var (uv0, uv1) = pattern.GetBoundingPoints();
                    var isActiveFrame = pi == animationFrame;
                    var color = isActiveFrame ? Colors.IconActive : (ImguiHelpers.GetColor(ImGuiCol.Border) with { W = 0.3f });
                    drawlist.AddRect(imageStart + size * uv0, imageStart + size * uv1, color.ToArgb());
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
                ImGui.Dummy(Vector2.Zero);
            }
        }
    }

    private static void ShowArrangement(SequenceBlock sequence, UIContext seqCtx)
    {
        var arrange = seqCtx.GetChildValue<SequencePatternArrangementHelper>();
        if (arrange == null) {
            arrange = new SequencePatternArrangementHelper();
            seqCtx.AddChild("Arrangement", arrange);
            arrange.Infer(sequence);
        }

        arrange.OnIMGUI(sequence);
    }

    private void ShowToolbar(SequenceBlock sequence, ContentWorkspace workspace)
    {
        var texPath = File.Textures[sequence.patterns[0].textureIndex].path;
        ImGui.SameLine();
        ImGui.BeginChild("Sequence Editor", new Vector2(ImGui.GetContentRegionAvail().X, 0), ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.Borders);
        var seqlist = Enumerable.Range(0, File.Sequences.Count).Select(n => $"  {n}  ").ToArray();
        if (ImguiHelpers.Tabs(seqlist, ref selectedSequenceIndex, true, "Sequences:")) {
            selectedPattern = null;
            shouldAnimate = false;
        }
        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator();
        ImGui.SameLine();
        if (ImGui.Button($"{AppIcons.SI_GenericAdd}")) {
            selectedSequenceIndex = File.Sequences.Count;
            UndoRedo.RecordListAdd(context, File.Sequences, new SequenceBlock() { patterns = new List<UvsPattern>() { new UvsPattern() { bottom = 1, right = 1 } } });
            selectedPattern = null;
            Handle.Modified = true;
        }
        ImguiHelpers.Tooltip("New Sequence"u8);
        ImguiHelpers.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconTertiary);
        if (selectedSequence != null) {
            if (ImGui.Button($"{AppIcons.SI_GenericDelete2}")) {
                EditorWindow.CurrentWindow!.AddSubwindow(new ConfirmationDialog(
                    "Deleting sequence",
                    "Are you sure you wish to delete the sequence " + selectedSequenceIndex + "?",
                    context.GetWindow() ?? throw new Exception("Missing parent window"),
                    () => {
                        UndoRedo.RecordListRemove(context, File.Sequences, selectedSequence);
                        if (selectedSequenceIndex >= File.Sequences.Count) {
                            selectedSequenceIndex = 0;
                        }
                        selectedPattern = null;
                        context.ClearChildren();
                    }
                ));
            }
        }
        ImGui.PopStyleColor();
        ImguiHelpers.Tooltip("Delete Sequence"u8);
        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator();
        ImGui.SameLine();
        if (ImGui.Button($"{AppIcons.SI_WindowOpenNew}") && texPath != null) {
            if (workspace.ResourceManager.TryResolveGameFile(texPath, out var texHandle)) {
                EditorWindow.CurrentWindow?.AddSubwindow(new TextureViewer(workspace, texHandle));
            }
        }
        ImguiHelpers.Tooltip($"Open Texture - {texPath}");
    }

    private unsafe void ShowTimeline(SequenceBlock sequence)
    {
        ImGui.SeparatorText("Timeline");
        int frameCount = sequence.patternCount;
        if (frameCount > 0) {
            animationFrame = Math.Clamp(animationFrame, 0, frameCount - 1);
        } else {
            animationFrame = 0;
        }
        using (var _ = ImguiHelpers.Disabled(frameCount == 0)) {
            ImguiHelpers.ToggleButton($"{AppIcons.Play}", ref shouldAnimate, Colors.IconActive);
            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && AppConfig.Instance.Key_UVS_Pause.Get().IsPressed()) {
                shouldAnimate = !shouldAnimate;
            }
            if (shouldAnimate && selectedPattern == null) {
                selectedPattern = sequence.patterns[0];
            }
            ImguiHelpers.Tooltip("Animate"u8);
            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.Previous}") || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && AppConfig.Instance.Key_UVS_PrevPattern.Get().IsPressed()) {
                animationFrame = (animationFrame - 1 + frameCount) % frameCount;
                shouldAnimate = false;
            }
            ImguiHelpers.Tooltip("Previous pattern"u8);
            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.Next}") || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && AppConfig.Instance.Key_UVS_NextPattern.Get().IsPressed()) {
                animationFrame = (animationFrame + 1) % frameCount;
                shouldAnimate = false;
            }
            ImguiHelpers.Tooltip("Next pattern"u8);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 75 - ImGui.CalcTextSize("FPS").X - ImGui.GetStyle().FramePadding.X * 3);
            if (ImGui.SliderInt("##Frame", ref animationFrame, 0, frameCount - 1)) {
                shouldAnimate = false;
            }
            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) {
                if (AppConfig.Instance.Key_UVS_IncreaseSpeed.Get().IsPressed() && selectedFpsIDX < fpsOptions.Length - 1) {
                    selectedFpsIDX++;
                }
                if (AppConfig.Instance.Key_UVS_DecreaseSpeed.Get().IsPressed() && selectedFpsIDX > 0) {
                    selectedFpsIDX--;
                }
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(75f);
            if (ImGui.BeginCombo("FPS", fpsOptions[selectedFpsIDX].ToString())) {
                for (int i = 0; i < fpsOptions.Length; i++) {
                    bool isSelected = i == selectedFpsIDX;
                    if (ImGui.Selectable(fpsOptions[i].ToString(), isSelected)) {
                        selectedFpsIDX = i;
                        animationFrame = 0;
                    }
                }
                ImGui.EndCombo();
            }
        }
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        for (int i = 0; i < sequence.patterns.Count; i++) {
            var pattern = sequence.patterns[i];
            float patternW = 30;
            var texPath = File.Textures[sequence.patterns[0].textureIndex].path;
            var tex = GetOrLoadTexture(texPath);
            
            ImGui.PushID(i);
            if (i > 0) {
                if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + patternW < ImGui.GetWindowPos().X + ImGui.GetContentRegionAvail().X) {
                    ImGui.SameLine();
                }
            }

            var size = new Vector2(patternW, patternW);
            var cursor = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();

            if (tex != null) {
                var (uv0, uv1) = pattern.GetBoundingPoints();
                ImGui.Image(tex.AsTextureRef(), size, uv0, uv1);
            }

            ImGui.SetCursorScreenPos(cursor);
            ImGui.InvisibleButton("##timelinePattern", size);

            if (ImGui.IsItemClicked() && !ImGui.IsMouseDragging(ImGuiMouseButton.Left)) {
                animationFrame = i;
                selectedPattern = pattern;
                shouldAnimate = false;
            }
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.PayloadNoCrossContext | ImGuiDragDropFlags.PayloadNoCrossProcess | ImGuiDragDropFlags.SourceNoPreviewTooltip)) {
                if (!isDraggingPattern) {
                    dragPatternIDX = i;
                    dragTargetIDX = i;
                    isDraggingPattern = true;
                }
                ImGui.SetDragDropPayload("PATTERN"u8, null, 0);
                ImGui.BeginTooltip();
                ImGui.Text($"Pattern {dragPatternIDX}");
                ImGui.EndTooltip();
                ImGui.EndDragDropSource();
            }

            bool isLast = i == sequence.patterns.Count - 1;
            if (isDraggingPattern && dragPatternIDX.HasValue && ImGui.BeginDragDropTarget()) {
                if (isLast && ImGui.IsMouseHoveringRect(cursor, cursor + size)) {
                    dragTargetIDX = sequence.patterns.Count;
                } else {
                    dragTargetIDX = i;
                }

                ImGui.EndDragDropTarget();
            }

            bool isActive = i == animationFrame;
            var borderColor = isActive ? Colors.IconActive : ImguiHelpers.GetColor(ImGuiCol.Border);
            drawList.AddRect(cursor, cursor + size, borderColor.ToArgb(), 0f, 0, isActive ? 2f : 1f);
            if (isDraggingPattern && (dragTargetIDX == i || (isLast && dragTargetIDX == sequence.patterns.Count))) {
                float lineX = (dragTargetIDX == sequence.patterns.Count) ? cursor.X + size.X + 2 : cursor.X - 2;
                drawList.AddLine(new Vector2(lineX, cursor.Y), new Vector2(lineX, cursor.Y + size.Y), Colors.IconActive.ToArgb(), 2f);
            }
            ImGui.PopID();
        }

        if (isDraggingPattern && !ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
            if (dragPatternIDX.HasValue && dragTargetIDX.HasValue) {
                ReorderTimelinePattern(sequence, dragPatternIDX.Value, dragTargetIDX.Value);
            }

            dragPatternIDX = null;
            dragTargetIDX = null;
            isDraggingPattern = false;
        }
    }

    private void ShowPreview(SequenceBlock sequence)
    {
        ImGui.Spacing();
        ImGui.SetNextWindowSizeConstraints(new Vector2(ImGui.GetContentRegionAvail().X / 2, ImGui.GetContentRegionAvail().Y), new Vector2(ImGui.GetContentRegionAvail().X / 2, ImGui.GetContentRegionAvail().Y));
        ImGui.BeginChild("PatternPreview");
        ImGui.SeparatorText("Preview");
        if (sequence.patterns.Count > 0) {
            var texPath = File.Textures[sequence.patterns[0].textureIndex].path;
            var tex = GetOrLoadTexture(texPath);
            if (tex != null) {
                var (uv0, uv1) = sequence.patterns[animationFrame].GetBoundingPoints();
                var size = new Vector2(tex.Width, tex.Height) * (uv1 - uv0);
                AnimationSequence(sequence, size, ref animationFrame, ref animationTime, ref animationColor, shouldAnimate, playbackFps);
            }
        }
        ImGui.EndChild();
    }

    private void ShowSelectedPattern()
    {
        ImGui.BeginChild("PatternSelect");
        ImGui.SeparatorText("Selected Pattern");
        ImGui.Text(selectedPattern?.ToString());
        ShowPatternEdit(selectedPattern!);
        ImGui.EndChild();
    }

    private void AnimationSequence(SequenceBlock sequence, Vector2 size, ref int frame, ref float time, ref Vector4 color, bool animate, float fps)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fps, 1);
        if (animate) {
            time += Time.Delta;
            if (time > 1 / fps) {
                time -= 1 / fps;
                frame++;
            }

            if (frame >= sequence.patternCount) {
                frame = 0;
            }
        }

        var pattern = sequence.patterns[frame];
        var texPath = File.Textures[pattern.textureIndex].path;
        var tex = GetOrLoadTexture(texPath);
        if (tex != null) {
            var (uv0, uv1) = pattern.GetBoundingPoints();
            // var size = new Vector2(tex.Width, tex.Height) * (uv1 - uv0);
            ImGui.Image(tex.AsTextureRef(), size, uv0, uv1);
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
                tex.AsTextureRef(),
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

        var workspace = context.GetWorkspace();
        if (workspace == null) {
            return referencedTextures[path] = null;
        }
        if (workspace.ResourceManager.TryResolveGameFile(path, out var resolvedFile) && resolvedFile.Format.format == KnownFileFormats.Texture) {
            var texture = new Texture().LoadFromTex(resolvedFile.GetFile<TexFile>());
            referencedTextures[path] = texture;
            return texture;
        }

        return referencedTextures[path] = null;
    }

    private void ReorderTimelinePattern(SequenceBlock sequence, int from, int to)
    {
        var item = sequence.patterns[from];
        sequence.patterns.RemoveAt(from);

        if (to > from) to--;

        sequence.patterns.Insert(to, item);
        sequence.patternCount = sequence.patterns.Count;
        Handle.Modified = true;
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
