using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;
using ContentEditor.Editor;
using ContentPatcher;
using Silk.NET.GLFW;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SilkWindow = Silk.NET.Windowing.Window;

namespace ContentEditor.App.Windowing;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.

public class WindowBase : IDisposable, IDragDropTarget, IRectWindow
{
    internal IWindow _window;
    internal GL _gl;
    protected Silk.NET.OpenGL.Extensions.Hexa.ImGui.ImGuiController _controller;
    protected IInputContext _inputContext;

    public GL GLContext => _gl;

    protected readonly List<WindowData> subwindows = new();
    private readonly List<WindowData> removeSubwindows = new();
    public IReadOnlyList<WindowData> ActiveImguiWindows => subwindows.AsReadOnly();
    public bool HasOpenInspectorForTarget(object target)
    {
        foreach (var sub in subwindows) {
            if (sub.Handler is ObjectInspector inspector && inspector.Target == target) {
                return true;
            }
        }
        return false;
    }

    private readonly ManualResetEventSlim isClosing = new(false);
    public bool IsClosing => isClosing.IsSet;

    private volatile bool isReady;

    public int ID { get; }
    public bool Exists => _window != null && (!_window.IsInitialized || !_window.IsClosing);

    public ReeLib.via.Color ClearColor { get; set; } = AppConfig.Instance.BackgroundColor;

    public Vector2 Size => new Vector2(_window.Size.X, _window.Size.Y);

    // public Vector2 Position => new Vector2(_window.Position.X, _window.Position.Y);
    public Vector2 Position => new Vector2(0, 0);

    private readonly ConcurrentQueue<Action> renderThreadActions = new();
    private readonly ConcurrentQueue<Action> uiThreadActions = new();
    private static Thread mainThread = Thread.CurrentThread;

    protected bool _disableIntroGuide;
    protected static WindowBase? _currentWindow;
    protected UIContext context;

    protected event Action? Ready;

    private Vector2 lastMousePosition;
    private bool windowNotHovered;
    private int updatesSinceLastMove;

    private OverlaysWindow imguiOverlays = null!;
    public OverlaysWindow Overlays => imguiOverlays;

    protected static readonly HashSet<Type> DefaultWindows = [typeof(OverlaysWindow), typeof(ConsoleWindow)];

    private static int nextSubwindowID = 1;

    private float smoothFrametime = 16f;
    private const float SmoothFpsScale = 0.1f;

    public bool SaveInProgress { get; set; }

    private enum State
    {
        Uninitialized,
        Ready,
        Closed,
    }

    internal WindowBase(int id)
    {
        ID = id;
    }

    internal virtual void InitializeWindow()
    {
        InitWindowEvents(WindowOptions.Default with {
            Title = "REE Content Editor v" + AppConfig.Version,
        });
    }

    internal void MakeCurrent()
    {
        _window.MakeCurrent();
    }

    protected virtual void InitWindowEvents(WindowOptions options)
    {
        if (ClearColor.A < 255) {
            options.TransparentFramebuffer = true;
        }
        _window = SilkWindow.Create(options);
        // _window.IsEventDriven = true;
        _window.ShouldSwapAutomatically = true;

        _window.Load += OnLoad;
        _window.FramebufferResize += OnFrameBufferResize;
        _window.Render += OnRender;
        _window.Update += OnUpdate;
        _window.Closing += OnClose;

        _window.Initialize();
        _inputContext = _window.CreateInput();
        foreach (var kb in _inputContext.Keyboards) {
            kb.KeyDown += OnKeyDown;
            kb.KeyUp += OnKeyUp;
            SetupKeyboard(kb);
        }
        var mouse = _inputContext.Mice.FirstOrDefault();
        if (mouse != null) {
            mouse.MouseMove += (m, vec) => {
                lastMousePosition = vec;
                windowNotHovered = false;
                updatesSinceLastMove = 0;
            };
            SetupMouse(mouse);
        }
    }

    protected virtual void SetupMouse(IMouse mouse)
    {
    }

    protected virtual void SetupKeyboard(IKeyboard keyboard)
    {
    }

    private unsafe void RemoveDropCallback()
    {
        // var wnd = (_window as GlfwWindow);
        // var nwnd = (_window.Native as GlfwNativeWindow);
        var _glfw = (Glfw)_window.GetType().GetField("_glfw", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_window)!;
        var _glfwHandlePtr = (Pointer)_window.GetType().GetField("_glfwWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_window)!;
        var _glfwHandle = (WindowHandle*)Pointer.Unbox(_glfwHandlePtr);
        _glfw.SetDropCallback(_glfwHandle, null);
    }

    internal void InitGraphics()
    {
        _gl = _window.CreateOpenGL();
        _controller = new Silk.NET.OpenGL.Extensions.Hexa.ImGui.ImGuiController(_gl, _window, _inputContext, onConfigureIO: UI.ConfigureImgui);
        UI.ApplyTheme(AppConfig.Instance.Theme.Get() ?? "default");
        // RemoveDropCallback();
        PlatformUtils.SetupDragDrop(this, _window);
        isReady = true;
        context = UIContext.CreateRootContext(ID.ToString(), this);
        Ready?.Invoke();
    }

    public WindowData AddSubwindow(IWindowHandler subwindow)
    {
        var pos = new Vector2(Size.X / 5 + Random.Shared.NextSingle() * 80, Size.Y / 5 + Random.Shared.NextSingle() * 80);
        var window = new WindowData() { Handler = subwindow, Position = pos, ParentWindow = this };
        AddSubwindow(window);
        return window;
    }

    protected void AddSubwindow(WindowData data)
    {
        if (data.Handler != null && data.Handler.FixedID != 0) {
            data.ID = data.Handler.FixedID;
        } else if (data.Handler != null) {
            var sameTypeWindows = subwindows.Where(sw => sw.Handler?.GetType() == data.Handler.GetType());
            if (sameTypeWindows.Any()) {
                data.ID = sameTypeWindows.Max(w => w.ID) + 1;
            } else {
                data.ID = 1;
            }
        } else {
            data.ID = nextSubwindowID++;
        }
        var label = $"{data.Handler?.HandlerName}##{data.ID}";
        if (string.IsNullOrEmpty(data.Name)) {
            data.Name = label;
        }
        data.Context = context.AddChild(label, this, data.Handler as IObjectUIHandler, (w) => data);
        data.Handler?.Init(data.Context);
        subwindows.Add(data);
        data.Handler?.OnOpen();
    }

    public void CopyToClipboard(string text, string? userNotice = "Copied!")
    {
        if (_inputContext.Keyboards.FirstOrDefault() is IKeyboard kb) {
            kb.ClipboardText = text;
            if (userNotice != null) this.Overlays.ShowTooltip(userNotice, 1.5f);
        }
    }

    public string? GetClipboard()
    {
        if (_inputContext.Keyboards.FirstOrDefault() is IKeyboard kb) {
            try {
                return kb.ClipboardText;
            } catch (Exception) {
                // non-text clipboards throw exceptions
                return null;
            }
        }
        return null;
    }

    public bool HasSubwindow<THandlerType>([MaybeNullWhen(false)] out WindowData window, Func<WindowData, bool>? condition = null) where THandlerType : IWindowHandler
    {
        foreach (var w in subwindows) {
            if (w.Handler is THandlerType handler) {
                if (condition == null || condition.Invoke(w)) {
                    window = w;
                    return true;
                }
            }
        }
        window = null;
        return false;
    }

    public WindowData AddUniqueSubwindow<THandlerType>(THandlerType subwindow) where THandlerType : IWindowHandler
    {
        var data = subwindows.Where(sw => sw.Handler is THandlerType).FirstOrDefault();
        if (data != null) {
            removeSubwindows.Add(data);
        }
        AddSubwindow(subwindow);
        data = subwindows.Last();
        ImGui.SetWindowFocus(data.Name);
        return data;
    }

    public void CloseSubwindow(IWindowHandler subwindow)
    {
        var data = subwindows.FirstOrDefault(s => s.Handler == subwindow);
        if (data != null) {
            removeSubwindows.Add(data);
        }
    }
    public void CloseSubwindow(WindowData subwindow)
    {
        removeSubwindows.Add(subwindow);
    }
    public void CloseAllSubwindows()
    {
        removeSubwindows.AddRange(subwindows);
    }
    public bool RequestCloseAllSubwindows()
    {
        bool anyNotClosed = false;
        foreach (var window in subwindows.Where(sw => !IsDefaultWindow(sw)).ToArray()) {
            if (window.Handler?.RequestClose() == true) {
                anyNotClosed = true;
                continue;
            }

            removeSubwindows.Add(window);
        }

        return !anyNotClosed;
    }

    internal void TriggerEvents()
    {
        if (isReady) {
            _controller.MakeCurrent();
            try {
                _window.DoEvents();
            } catch (Exception e) {
                Logger.Error($"Failed to handle input events: {e.Message}");
            }
            var isInputting = ImGui.GetIO().WantTextInput;
            if (!isInputting) {
                var cfg = AppConfig.Instance;
                if (UndoRedo.CanUndo(this) && cfg.Key_Undo.Get().IsPressed()) {
                    UndoRedo.Undo(this);
                }
                if (UndoRedo.CanRedo(this) && cfg.Key_Redo.Get().IsPressed()) {
                    UndoRedo.Redo(this);
                }
                if ((this as EditorWindow)?.Workspace is ContentWorkspace workspace && cfg.Key_Save.Get().IsPressed()) {
                    SaveInProgress = true;
                    Task.Run(() => {
                        try {
                            int count = workspace.SaveModifiedFiles(this);
                            if (count > 0) {
                                Overlays.ShowTooltip($"Saved {count} files!", 1);
                            }
                        } finally {
                            SaveInProgress = false;
                        }
                    });
                }
            }
        }
    }

    private volatile int _hasPendingResize = 0;
    internal void TriggerUpdate()
    {
        if (_hasPendingResize > 0 && ID != 0) {
            HandleRenderActions();
            return;
        }
        _window.DoUpdate();
        _window.DoRender();
    }

    private void OnClose()
    {
        if (RequestAllowClose()) {
            isClosing.Set(); // Here we trigger the ManualEvent to let those waiting on it know the window is closing.
            _window.ContinueEvents(); // We wake up the Main Thread here 'cause we'll be done soon.
            // _window.Dispose();
            _window.IsClosing = true;
        } else {
            // _window.IsClosing = false;
        }
    }

    protected virtual bool RequestAllowClose()
    {
        return true;
    }

    private void OnLoad()
    {
    }

    private void OnFrameBufferResize(Vector2D<int> size)
    {
        // Console.WriteLine("Framebufresize" + size + "on wind" + ID);
        _window.MakeCurrent();
        _gl.Viewport(size);
    }

    private void OnUpdate(double delta)
    {
        if (_window.WindowState == WindowState.Minimized) {
            return;
        }
        _controller.MakeCurrent();
        _currentWindow = this;
        var mousePos = _inputContext.Mice[0].Position;

        var io = ImGui.GetIO();
        _controller.Update((float)delta);
        Update((float)delta);
        // hack: prevent imgui from receiving mouse position when the current window is not actually hovered
        // (e.g. when there's another window in front of us)
        // ideally we'd just modify the imgui controller but it's using several internal methods
        // add a minimum distance because MouseMove doesn't seem to always trigger for small movements
        if (windowNotHovered || updatesSinceLastMove++ > 1 && (mousePos - lastMousePosition).LengthSquared() > 16) {
            io.MousePos = new Vector2(-1, -1);
            windowNotHovered = true;
        }
        _currentWindow = null;
    }

    internal void NotifyCursorMoved()
    {
        updatesSinceLastMove = -1;
    }

    public void HandleRenderActions()
    {
        while (renderThreadActions.TryDequeue(out var act)) {
            try {
                act.Invoke();
            } catch (Exception e) {
                Logger.Error(e, "Error in render thread callback");
            }
        }
    }

    private unsafe void HandleExtDragDrop()
    {
        if (DragDropData == null) return;

        ImGui.GetIO().MousePos = dragDropPosition;
        if (!fileWasDropped) {
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceExtern)) {
                if (DragDropData.filenames?.Length >= 1) {
                    ImGui.SetDragDropPayload(ImguiHelpers.DragDrop_File, null, 0);
                } else {
                    ImGui.SetDragDropPayload(ImguiHelpers.DragDrop_Text, null, 0);
                }
                ImGui.EndDragDropSource();
            }
        }
    }

    private void OnRender(double delta)
    {
        if (_window.WindowState == WindowState.Minimized) {
            return;
        }
        _currentWindow = this;
        HandleRenderActions();
        _gl.Enable(Silk.NET.OpenGL.EnableCap.DepthTest);
        _gl.ClearColor(System.Drawing.Color.FromArgb(ClearColor.BGRA));
        _gl.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
        ImGui.PushFont(null, UI.FontSize);

        Render((float)delta);
        HandleExtDragDrop();
        OnIMGUI();
        if (AppConfig.Instance.ShowFps) {
            smoothFrametime = (smoothFrametime * (1 - SmoothFpsScale) + (float)delta * SmoothFpsScale);
            var fps = (1 / smoothFrametime).ToString("0.0", CultureInfo.InvariantCulture);
            var frametime = (smoothFrametime * 1000).ToString("0.00", CultureInfo.InvariantCulture) + "ms";
            var fpsText = fps + " / " + frametime;
            var fpsSize = ImGui.CalcTextSize(fpsText);
            ImGui.GetForegroundDrawList().AddText(new Vector2(Size.X - fpsSize.X, 0) - ImGui.GetStyle().FramePadding, 0xffffffff, fpsText);
        }
        ImGui.PopFont();
        _controller.Render();
        if (fileWasDropped && DragDropData != null) {
            var data = DragDropData;
            if (data != null) {
                if (data.text != null) {
                    if (Path.IsPathFullyQualified(data.text) && (File.Exists(data.text) || Directory.Exists(data.text))) {
                        OnFileDrop([data.text], new Vector2D<int>());
                    }
                } else if (data.filenames?.Length > 0) {
                    OnFileDrop(data.filenames, new Vector2D<int>());
                }
            }
            DragDropData = null;
            fileWasDropped = false;
        }
        _currentWindow = null;
    }

    protected virtual void Render(float deltaTime)
    {
    }

    protected void BeginDockableBackground(Vector2 offset)
    {
        var ss = ImGui.GetStyle();
        var padding = ss.WindowPadding;
        offset -= padding;
        var size = ImGui.GetWindowViewport().Size - offset + padding;
        ImGui.SetNextWindowPos(offset, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.Begin("Dockspace", ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBringToFrontOnFocus);
        ImGui.DockSpace(ImGui.GetID("_dock"), new Vector2(0, 0), ImGuiDockNodeFlags.PassthruCentralNode);
    }
    protected void EndDockableBackground()
    {
        ImGui.End();
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int arg3)
    {
        _controller.MakeCurrent();
        if (key is Key.ControlLeft or Key.ControlRight) {
            ImGui.GetIO().AddKeyEvent(ImGuiKey.ModCtrl, true);
        }
        if (key is Key.AltLeft or Key.AltRight) {
            ImGui.GetIO().AddKeyEvent(ImGuiKey.ModAlt, true);
        }
        if (key is Key.ShiftLeft or Key.ShiftRight) {
            ImGui.GetIO().AddKeyEvent(ImGuiKey.ModShift, true);
        }
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int arg3)
    {
        _controller.MakeCurrent();
        if (key is Key.ControlLeft or Key.ControlRight) {
            ImGui.GetIO().AddKeyEvent(ImGuiKey.ModCtrl, false);
        }
        if (key is Key.AltLeft or Key.AltRight) {
            ImGui.GetIO().AddKeyEvent(ImGuiKey.ModAlt, false);
        }
        if (key is Key.ShiftLeft or Key.ShiftRight) {
            ImGui.GetIO().AddKeyEvent(ImGuiKey.ModShift, false);
        }
    }

    protected virtual void Update(float deltaTime)
    {

    }

    protected void DrawImguiWindows()
    {
        for (int i = 0; i < removeSubwindows.Count; i++) {
            var close = removeSubwindows[i];
            if (subwindows.Remove(close)) {
                (close.Handler as IDisposable)?.Dispose();
                close.Context.Get<WindowData>().Handler?.OnClosed();
            }
        }
        removeSubwindows.Clear();
        // note: executing removes before thread actions, so we have an easy way of doing things after the window properly closes (unsaved changes)
        while (uiThreadActions.TryDequeue(out var action)) {
            try {
                action.Invoke();
            } catch (Exception e) {
                Logger.Error(e, "Deferred callback failed");
            }
        }

        var saving = SaveInProgress;
        for (int i = 0; i < subwindows.Count; i++) {
            var sub = subwindows[i];
            if (sub.Handler != null) {
                if (saving && sub.Handler is not IKeepEnabledWhileSaving) ImGui.BeginDisabled();
                try {
                    ImGui.PushID(sub.ID);
                    sub.Handler.OnWindow();
                    ImGui.PopID();
                } catch (Exception e) {
                    Logger.Error(e, $"Error occurred in window {sub.Name}");
                }
                if (saving && sub.Handler is not IKeepEnabledWhileSaving) ImGui.EndDisabled();
            }
        }

        if (imguiOverlays == null) {
            imguiOverlays = new OverlaysWindow();
            var imguiOverlaysData = new WindowData() { Handler = imguiOverlays, ParentWindow = this };
            imguiOverlaysData.Context = context.AddChild("__overlays", imguiOverlaysData);
            imguiOverlays.Init(imguiOverlaysData.Context);
        }
        imguiOverlays.OnIMGUI();

        var imguiCursor = ImGui.GetMouseCursor();
        var cursor = this._inputContext.Mice[0].Cursor;
        switch (imguiCursor) {
            case ImGuiMouseCursor.Arrow:
                cursor.StandardCursor = StandardCursor.Arrow;
                break;
            case ImGuiMouseCursor.TextInput:
                cursor.StandardCursor = StandardCursor.IBeam;
                break;
            case ImGuiMouseCursor.NotAllowed:
                cursor.StandardCursor = StandardCursor.NotAllowed;
                break;
            case ImGuiMouseCursor.Hand:
                cursor.StandardCursor = StandardCursor.Hand;
                break;
            case ImGuiMouseCursor.ResizeAll:
                cursor.StandardCursor = StandardCursor.ResizeAll;
                break;
            case ImGuiMouseCursor.ResizeNwse:
                cursor.StandardCursor = StandardCursor.NwseResize;
                break;
            case ImGuiMouseCursor.ResizeNesw:
                cursor.StandardCursor = StandardCursor.NeswResize;
                break;
            case ImGuiMouseCursor.ResizeEw:
                cursor.StandardCursor = StandardCursor.HResize;
                break;
            case ImGuiMouseCursor.ResizeNs:
                cursor.StandardCursor = StandardCursor.VResize;
                break;
        }
    }

    protected static bool IsDefaultWindow(WindowData data) => data.Handler != null && DefaultWindows.Contains(data.Handler.GetType());

    public void InvokeFromUIThread(Action action)
    {
        uiThreadActions.Enqueue(action);
    }

    protected virtual void OnIMGUI()
    {
        DrawImguiWindows();
    }

    public void DestroyWindow()
    {
        var w = _window;
        DisposeMainThread();
        Dispose();
        w.Reset();
    }

    public void Dispose()
    {
        UndoRedo.Clear(this);
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    internal void DisposeMainThread()
    {
        _inputContext?.Dispose();
        _inputContext = null!;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing) {
            _window.MakeCurrent();
            _controller?.MakeCurrent();
            _controller?.Dispose();
            // DisposeImguiContext();
            _inputContext?.Dispose();
            _window?.Dispose();
            _gl?.Dispose();
            _controller = null!;
            _inputContext = null!;
            _window = null!;
            _gl = null!;
        }
    }

    private void DisposeImguiContext()
    {
        if (_controller == null) return;

        _controller.Dispose();
        _controller = null!;
    }

    protected virtual void OnFileDrop(string[] filename, Vector2D<int> position)
    {
    }

    public void ConsumeDragDrop()
    {
        DragDropData = null;
        fileWasDropped = false;
    }

    private Vector2 dragDropPosition;
    public DragDropContextObject? DragDropData { get; private set; }
    private bool fileWasDropped;

    void IDragDropTarget.DragEnter(DragDropContextObject data, uint keyState, Point position, ref uint effect)
    {
        DragDropData = data;
    }

    void IDragDropTarget.DragOver(uint keyState, Point position, ref uint effect)
    {
        var clientPos = _window.PointToClient(new Vector2D<int>(position.X, position.Y));
        dragDropPosition = new Vector2(clientPos.X, clientPos.Y);
    }

    void IDragDropTarget.DragLeave()
    {
        DragDropData = null;
    }

    void IDragDropTarget.Drop(DragDropContextObject data, uint keyState, Point position, ref uint effect)
    {
        DragDropData ??= data;
        fileWasDropped = true;
    }

    internal struct OverrideCurrentWindow : IDisposable
    {
        private WindowBase? backup;
        public OverrideCurrentWindow(WindowBase window)
        {
            if (Thread.CurrentThread != mainThread) {
                throw new Exception("Cannot override current window outside of main thread!");
            }

            backup = _currentWindow;
            _currentWindow = window;
        }

        public void Dispose()
        {
            _currentWindow = backup;
        }
    }

#if WINDOWS
    static WindowBase()
    {
        // source: https://github.com/HexaEngine/Hexa.NET.ImGui/issues/35#issuecomment-3172275411

        // By default the Visual C++ runtime library seems to consider .NET apps to be console apps (presumably
        // because the UCRT code to detect the app type didn't run) and thus a message box is not shown when
        // assert() is triggered. This is important for ImGui, which uses IM_ASSERT() liberally.
        // By calling _set_error_mode we can force the message box to be shown.
        const int _OUT_TO_MSGBOX = 2;
        _ = _set_error_mode(_OUT_TO_MSGBOX);
    }

    /// <summary>
    /// Specifies the behavior of the assert() function in the Visual C++ runtime library.
    /// </summary>
    /// <param name="mode_val"> An enum value that specifies the desired behavior. </param>
    /// <returns> The old setting or <c>-1</c> if an error occurs. </returns>
    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int _set_error_mode(int mode_val);
#endif
}
