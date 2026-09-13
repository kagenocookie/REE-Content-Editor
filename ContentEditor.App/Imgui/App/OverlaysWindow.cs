using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using System.Numerics;

namespace ContentEditor.App;

public class OverlaysWindow : IWindowHandler
{
    public string HandlerName => "Overlays";

    public bool HasUnsavedChanges => false;
    public bool ShowHelp { get; set; }

    private WindowData data = null!;
    protected UIContext context = null!;
    private List<ToastData> Toasts { get; } = new();

    public WindowData Window => data;

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    private string? tooltipMsg;
    private float tooltipTime = 0f;

    public void ShowTooltip(string message, float duration)
    {
        tooltipMsg = message;
        tooltipTime = duration;
    }

    public void ShowToast(float duration, string message, params (string? label, Action action)[] buttons)
    {
        Toasts.Add(new ToastData(NextToastMsgId++) {
            message = message,
            disappearAt = DateTime.Now.AddSeconds(duration),
            Buttons = buttons ?? [],
        });
    }

    public void OnWindow() => this.ShowDefaultWindow(context);
    public void OnIMGUI()
    {
        var size = ImGui.GetWindowViewport().Size - ImGui.GetStyle().WindowPadding;
        ShowHelpText(size);
        ShowTimedTooltip();
        ShowBackgroundTasks(size);
        ShowToasts(size);
        if (AppConfig.Instance.Key_HotkeyHint.Get().IsDown()) {
            ShowHotkeyHints(size);
        }
    }

    private void ShowHelpText(Vector2 size)
    {
        string? helptext = null;
        if (!AppConfig.Instance.HasAnyGameConfigured) {
            helptext = "Go into the Tools > Settings menu and configure the game(s) you wish to edit";
        } else if (context.GetWorkspace() == null) {
            helptext = "Activate the game you wish to edit in the menu";
        } else if (ShowHelp) {
            //helptext = "Drag & drop a supported RE Engine resource file here or use the menu to open one.\nYou can access the game files directly using the Windows > PAK File Browser option.";
        }
        var editorWindow = data.ParentWindow as EditorWindow;
        if (helptext != null) {
            var linecount = helptext.Count(c => c == '\n') + 1;
            var wndSize = new Vector2(Math.Min(600, size.X), Math.Min(20 + linecount * 20, size.Y)) * UI.UIScale;
            ImGui.SetNextWindowPos(new Vector2((size.X - wndSize.X) / 2, (size.Y - wndSize.Y) / 2));
            ImGui.SetNextWindowSize(wndSize);
            ImGui.Begin("Guide", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus);
            ImguiHelpers.TextCentered(helptext);
            if (editorWindow != null && ImGui.IsItemClicked()) {
                PlatformUtils.ShowFileDialog((files) => {
                    Logger.Info(string.Join("\n", files));
                    editorWindow.OpenFiles(files);
                });
            }
            ImGui.End();
        }
    }

    private void ShowTimedTooltip()
    {
        if (tooltipTime > 0) {
            ImGui.BeginTooltip();
            ImGui.Text(tooltipMsg);
            ImGui.EndTooltip();
            tooltipTime -= Time.Delta;
            if (tooltipTime <= 0) {
                tooltipMsg = null;
            }
        }
    }

    private static void ShowBackgroundTasks(Vector2 size)
    {
        var bg = MainLoop.Instance.BackgroundTasks;
        var runningTasks = bg.PendingTasks;
        if (runningTasks > 0) {
            var taskWindowSize = new Vector2(400, 200);
            ImGui.SetNextWindowPos(size - taskWindowSize - ImGui.GetStyle().WindowPadding, ImGuiCond.Appearing);
            ImGui.SetNextWindowSize(taskWindowSize, ImGuiCond.Appearing);
            ImGui.SetNextWindowCollapsed(false, ImGuiCond.Appearing);
            if (ImGui.Begin("Background Tasks", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoFocusOnAppearing)) {
                ShowBackgroundTaskProgress(ImGui.GetWindowWidth());
            }
            ImGui.End();
        }
    }

    internal static void ShowBackgroundTaskProgress(float maxWidth)
    {
        var bg = MainLoop.Instance.BackgroundTasks;
        var runningTasks = bg.PendingTasks;
        ImGui.Text("Pending background tasks: " + runningTasks);

        var jobSize = new Vector2(maxWidth - ImGui.GetStyle().WindowPadding.X * 2, 30);
        foreach (var (job, progress) in bg.CurrentJobs) {
            ImGui.Separator();
            ImGui.ProgressBar(progress >= 0 && progress <= 1 ? progress : -1 * (float)ImGui.GetTime(), jobSize);
            ImGui.TextWrapped(job);
        }
    }

    private void ShowToasts(Vector2 size)
    {
        var toastOffsetY = 0f;
        var toastBottom = size.Y - ImGui.GetStyle().WindowPadding.Y;
        for (int i = Toasts.Count - 1; i >= 0; i--) {
            var toast = Toasts[i];
            if (toast.disappearAt <= DateTime.Now) {
                Toasts.RemoveAt(i);
                continue;
            }

            var textSize = ImGui.CalcTextSize(toast.message);
            var windowPadding = ImGui.GetStyle().WindowPadding;

            var height = textSize.Y + windowPadding.Y * 2;
            if (toast.Buttons.Length > 0) height += ImGui.GetFrameHeightWithSpacing() + 4;

            var toastWidth = textSize.X + windowPadding.X * 2 + ImGui.GetFrameHeight();
            var toastLeft = size.X - toastWidth - windowPadding.X * 2;

            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 3f);
            var pos = new Vector2(toastLeft, toastBottom - height - toastOffsetY);
            ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(toastWidth, height), ImGuiCond.Always);
            ImGui.SetNextWindowCollapsed(false, ImGuiCond.Always);
            var closePos = new Vector2(toastLeft + toastWidth - ImGui.GetFrameHeight(), pos.Y + windowPadding.Y);
            var close = false;
            if (ImGui.Begin($"###Toast{toast.ID}", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoNavInputs | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoMove)) {
                ImGui.Text(toast.message);
                if (toast.Buttons.Length > 0) {
                    ImGui.Separator();
                    int c = 0;
                    foreach (var (label, act) in toast.Buttons) {
                        if (c++ > 0) ImGui.SameLine();
                        if (ImGui.Button(string.IsNullOrEmpty(label) ? "Confirm" : label)) {
                            act.Invoke();
                            close = true;
                        }
                    }
                }
            }
            close |= ImGuiP.CloseButton(ImGui.GetID("CloseButton"), closePos);
            if (close) {
                Toasts.RemoveAt(i);
            }
            ImGui.End();
            ImGui.PopStyleVar();
            toastOffsetY += height + ImGui.GetStyle().ItemSpacing.Y * 2;
        }
    }

    private static int NextToastMsgId = 1;

    private sealed class ToastData(int id)
    {
        public string message = "";
        public DateTime disappearAt;
        public (string? label, Action action)[] Buttons = [];

        public int ID { get; } = id;
    }

    private class HotkeyHintGroup
    {
        public required FixedString GroupName { get; set; }
        public List<HotkeyHint> HotkeyList { get; set; } = new();
    }
    private class HotkeyHint
    {
        public required FixedString Description { get; set; }
        public Func<string>? Hotkey { get; set; }
        public bool IsSeparator { get; set; }
    }
    private static readonly HotkeyHintGroup globalHotkeys = new()
    {
        GroupName = Lang.Settings.Group_Global, HotkeyList = {
            new HotkeyHint { Description = Lang.Settings.Bind_Undo, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_Undo.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_Redo, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_Redo.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_Copy, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_Copy.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_Paste, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_Paste.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_Open, Hotkey = () => AppImguiHelpers.FormatHotkeyString( AppConfig.Instance.Key_Open.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_Save, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_Save.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_Close, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_Close.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_HomePage, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_HomePage.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_OpenPakBrowser, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_OpenPakBrowser.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_OpenMacroShelf, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_OpenMacroShelf.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_ShowHotkeyHints, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_HotkeyHint.Get())},
        }
    };
    private static readonly HotkeyHintGroup pakBrowserHotkeys = new() {
        GroupName = Lang.Settings.Group_Pak, HotkeyList = {
            new HotkeyHint { Description = Lang.Settings.Bind_Back, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_Back.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_PakBrowser_OpenBookmarks, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_PakBrowser_OpenBookmarks.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_PakBrowser_Bookmark, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_PakBrowser_Bookmark.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_PakBrowser_JumpToPageTop, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_PakBrowser_JumpToPageTop.Get())},
        }
    };
    private static readonly HotkeyHintGroup meshViewerHotkeys = new() {
        GroupName = Lang.Settings.Group_Mesh, HotkeyList = {
            new HotkeyHint { Description = Lang.Settings.Section_FPSCamera, IsSeparator = true},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_CameraTranslate, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_CameraTranslate.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_CameraRotate, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_CameraRotate.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_CameraZoom, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_CameraZoom.Get())},
            new HotkeyHint { Description = Lang.Settings.Section_OrthoCamera, IsSeparator = true},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_CameraTranslate, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_OrthoCameraTranslate.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_CameraRotate, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_OrthoCameraRotate.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_CameraZoom, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_OrthoCameraZoom.Get())},
            new HotkeyHint { Description = Lang.Settings.Section_PivotCamera, IsSeparator = true},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_CameraTranslate, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_PivotCameraTranslate.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_CameraRotate, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_PivotCameraRotate.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_CameraZoom, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_PivotCameraZoom.Get())},
            new HotkeyHint { Description = Lang.Settings.Section_Animator, IsSeparator = true},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_PauseAnim, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_PauseAnim.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_NextAnimFrame, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_NextAnimFrame.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_PrevAnimFrame, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_PrevAnimFrame.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_IncreaseAnimSpeed, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_IncreaseAnimSpeed.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_DecreaseAnimSpeed, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_DecreaseAnimSpeed.Get())},
            new HotkeyHint { Description = Lang.Settings.Section_MeshEditor, IsSeparator = true},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_VertexSelection, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_VertexSelection.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_EdgeSelection, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_EdgeSelection.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_FaceSelection, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_FaceSelection.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_MoveGeometry, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_MoveGeometry.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_MeshViewer_SelectAll, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_MeshViewer_SelectAll.Get())},
        }
    };
    private static readonly HotkeyHintGroup textureViewerHotkeys = new() {
        GroupName = Lang.Settings.Group_Texture, HotkeyList = {
            new HotkeyHint { Description = Lang.Settings.Bind_TextureViewer_ResetView, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_TextureViewer_ResetView.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_TextureViewer_ZoomIn, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_TextureViewer_ZoomIn.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_TextureViewer_ZoomOut, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_TextureViewer_ZoomOut.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_TextureViewer_NextChannel, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_TextureViewer_NextChannel.Get())},
            new HotkeyHint { Description = Lang.Settings.Bind_TextureViewer_PrevChannel, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_TextureViewer_PrevChannel.Get())},
        }
    };
    private static readonly HotkeyHintGroup sceneHotkeys = new() {
        GroupName = Lang.Settings.Group_Scene, HotkeyList = {
            new HotkeyHint {Description = Lang.Settings.Bind_Scene_Focus3D, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_Scene_Focus3D.Get())},
            new HotkeyHint {Description = Lang.Settings.Bind_Scene_FocusUI, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_Scene_FocusUI.Get())},
            new HotkeyHint {Description = Lang.Settings.Bind_Scene_Hide, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_Scene_Hide.Get())},
            new HotkeyHint {Description = Lang.Settings.Bind_Scene_UnhideAll, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_Scene_UnhideAll.Get())},
            new HotkeyHint {Description = Lang.Settings.Bind_Scene_Delete, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_Scene_Delete.Get())},
        }
    };
    private static readonly HotkeyHintGroup uvsHotkeys = new() {
        GroupName = Lang.Settings.Group_UVS, HotkeyList = {
            new HotkeyHint {Description = Lang.Settings.Bind_UVS_Pause, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_UVS_Pause.Get())},
            new HotkeyHint {Description = Lang.Settings.Bind_UVS_NextPattern, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_UVS_NextPattern.Get())},
            new HotkeyHint {Description = Lang.Settings.Bind_UVS_PrevPattern, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_UVS_PrevPattern.Get())},
            new HotkeyHint {Description = Lang.Settings.Bind_UVS_IncreaseSpeed, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_UVS_IncreaseSpeed.Get())},
            new HotkeyHint {Description = Lang.Settings.Bind_UVS_DecreaseSpeed, Hotkey = () => AppImguiHelpers.FormatHotkeyString(AppConfig.Instance.Key_UVS_DecreaseSpeed.Get())},
        }
    };
    private static object? lastWinHandler = null;
    private static float hotkeyHintAnimStartTime = -1f;

    public static void ShowHotkeyHints(Vector2 size)
    {
        var currWinHandler = EditorWindow.CurrentWindow?.FocusedWindow?.Handler;
        var currGroup = currWinHandler switch {
            ContentEditor.App.PakBrowser => pakBrowserHotkeys,
            ContentEditor.App.MeshViewer => meshViewerHotkeys,
            ContentEditor.App.TextureViewer => textureViewerHotkeys,
            ContentEditor.App.SceneView => sceneHotkeys,
            ContentEditor.App.ImguiHandling.UVSequenceFileEditor => uvsHotkeys,
            _ => globalHotkeys
        };
        if (currWinHandler != lastWinHandler) {
            lastWinHandler = currWinHandler;
            hotkeyHintAnimStartTime = (float)ImGui.GetTime();
        }
        var animT = hotkeyHintAnimStartTime < 0f ? 1f : Math.Clamp(((float)ImGui.GetTime() - hotkeyHintAnimStartTime) / 0.5f, 0f, 1f);
        var animEase = animT * animT * (3f - 2f * animT);

        var style = ImGui.GetStyle();
        var lineH = ImGui.GetTextLineHeight();
        var descW = currGroup.HotkeyList.Max(x => ImGui.CalcTextSize(x.Description).X);
        var hotkeyW = currGroup.HotkeyList.Where(x => x.Hotkey is not null).Select(x => ImGui.CalcTextSize(x.Hotkey!()).X).Max();
        var hintOverlayW = Math.Max(250, descW + hotkeyW + style.ItemSpacing.X + style.WindowPadding.X * 2);
        var hkCount = currGroup.HotkeyList.Count;

        var rowH = new float[hkCount];
        var rowOffsetH = new float[hkCount];
        float cursorH = style.WindowPadding.Y + lineH + style.ItemSpacing.Y;
        for (int i = 0; i < hkCount; i++) {
            var hk = currGroup.HotkeyList[i];
            var h = hk.IsSeparator ? lineH + style.SeparatorTextPadding.Y * 2f : lineH + style.CellPadding.Y * 2f;
            rowOffsetH[i] = cursorH;
            rowH[i] = h;
            cursorH += h;
        }

        var windowPos = new Vector2(size.X - hintOverlayW - style.WindowPadding.X * 3, style.WindowPadding.Y * 3);
        var windowSize = new Vector2(hintOverlayW, cursorH + style.WindowPadding.Y);
        var revealH = windowSize.Y * animEase;
        var drawList = ImGui.GetForegroundDrawList();
        var headerColor = ImGui.GetColorU32(Colors.IconOverlay);

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, animEase);
        drawList.AddRectFilled(windowPos, windowPos + windowSize, ImGui.GetColorU32(ImGuiCol.WindowBg), style.WindowRounding);
        drawList.PushClipRect(windowPos, windowPos + new Vector2(windowSize.X, revealH), true);
        drawList.AddRectFilled(windowPos, windowPos + new Vector2(windowSize.X, 34 * UI.UIScale), headerColor);
        drawList.AddRectFilled(new Vector2(windowPos.X + windowSize.X - 4, windowPos.Y), windowPos + windowSize, headerColor);

        var headerText = "Hotkeys: " + currGroup.GroupName;
        drawList.AddText(new Vector2(windowPos.X + style.WindowPadding.X, windowPos.Y + style.WindowPadding.Y), ImGui.GetColorU32(ImGuiCol.WindowBg), headerText);

        var column0W = windowPos.X + style.WindowPadding.X;
        var column1W = windowPos.X + windowSize.X - style.WindowPadding.X - hotkeyW;
        int rowIDX = 0;
        for (int i = 0; i < hkCount; i++) {
            var hk = currGroup.HotkeyList[i];
            var rowMin = new Vector2(windowPos.X, windowPos.Y + rowOffsetH[i]);
            var rowMax = new Vector2(windowPos.X + windowSize.X, windowPos.Y + rowOffsetH[i] + rowH[i]);
            if (hk.IsSeparator) {
                var sepH = rowMin.Y + rowH[i] * 0.5f;
                drawList.AddText(new Vector2(column0W, rowMin.Y + style.SeparatorTextPadding.Y), ImGui.GetColorU32(Colors.TextActive), hk.Description);
                drawList.AddLine(new Vector2(column0W + ImGui.CalcTextSize(hk.Description).X + style.ItemSpacing.X, sepH), new Vector2(rowMax.X - style.WindowPadding.X, sepH), ImGui.GetColorU32(ImGuiCol.Separator), 2f * UI.UIScale);
                continue;
            }
            drawList.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(rowIDX++ % 2 == 0 ? ImGuiCol.TableRowBg : ImGuiCol.TableRowBgAlt));
            drawList.AddText(new Vector2(column0W, rowMin.Y + style.CellPadding.Y), ImGui.GetColorU32(ImGuiCol.Text), hk.Description);
            drawList.AddText(new Vector2(column1W, rowMin.Y + style.CellPadding.Y), ImGui.GetColorU32(ImGuiCol.TextDisabled), hk.Hotkey!());
        }

        drawList.PopClipRect();
        ImGui.PopStyleVar();
    }
    public bool RequestClose()
    {
        return false;
    }
}
