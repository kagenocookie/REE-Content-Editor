using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using ContentEditor.App.ImguiHandling;
using ContentEditor.Editor;
using ContentPatcher;
using ImGuiNET;
using ReeLib.Bvh;
using Silk.NET.GLFW;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using SilkWindow = Silk.NET.Windowing.Window;

namespace ContentEditor.App.Windowing;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.

public class WindowBase : IDisposable, IDragDropTarget, IRectWindow
{
    internal IWindow _window;
    internal GL _gl;
    protected ImGuiController _controller;
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

    private DateTime _lastMouseMoveTime;

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
                _lastMouseMoveTime = DateTime.Now;
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
        _controller = new ImGuiController(_gl, _window, _inputContext, onConfigureIO: UI.ConfigureImgui);
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
        _controller.MakeCurrent();
        _currentWindow = this;

        var io = ImGui.GetIO();
        var prevPos = io.MousePos;
        _controller.Update((float)delta);
        Update((float)delta);
        var newPos = io.MousePos;
        // hack: prevent imgui from receiving mouse position when the current window is not actually hovered
        // (e.g. when there's another window in front of us)
        // ideally we'd just modify the imgui controller but it's using several internal methods
        if (newPos != prevPos) {
            if ((DateTime.Now - _lastMouseMoveTime).TotalSeconds > 0.0625f) {
                io.MousePos = new Vector2(-1, -1);
            }
        }
        _currentWindow = null;
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

    private void OnRender(double delta)
    {
        _currentWindow = this;
        HandleRenderActions();
        _gl.Enable(Silk.NET.OpenGL.EnableCap.DepthTest);
        _gl.ClearColor(System.Drawing.Color.FromArgb(ClearColor.BGRA));
        _gl.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);

        Render((float)delta);
        OnIMGUI();
        if (AppConfig.Instance.ShowFps) {
            smoothFrametime = (smoothFrametime * (1 - SmoothFpsScale) + (float)delta * SmoothFpsScale);
            var fps = (1 / smoothFrametime).ToString("0.0", CultureInfo.InvariantCulture);
            var frametime = (smoothFrametime * 1000).ToString("0.00", CultureInfo.InvariantCulture) + "ms";
            var fpsText = fps + " / " + frametime;
            var fpsSize = ImGui.CalcTextSize(fpsText);
            ImGui.GetForegroundDrawList().AddText(new Vector2(Size.X - fpsSize.X, 0) - ImGui.GetStyle().FramePadding, 0xffffffff, fpsText);
        }
        _controller.Render();
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
        ImGui.Begin("Dockspace", ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);
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

        // _controller.Dispose();
        // manually do what ImguiController.Dispose() would do - except leave the shader intact because it seems to share data
        // if we dispose the shader, all imgui windows stop rendering
        // may not be an issue with single-threaded windowing?
        // leaving this here in case we need it again
        var ctrlType = _controller.GetType();

        RemoveDelegate(_window, nameof(IView.Resize), _controller);
        // RemoveDelegate(_inputContext.Keyboards[0], nameof(IKeyboard.KeyChar), _controller);
        static void RemoveDelegate(object owner, string name, object target)
        {
            var eventField = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var dell = eventField?.GetValue(owner) as Delegate;
            // var invo = dell?.GetInvocationList().FirstOrDefault(d => d.Target == target);
            // if (invo != null) owner.GetType().GetEvent(name)!.RemoveEventHandler(owner, invo);
            var invo = dell!.GetInvocationList().FirstOrDefault(d => d.Target == target);
            owner.GetType().GetEvent(name)!.RemoveEventHandler(owner, invo);
        }

        _gl.DeleteBuffer((uint)ctrlType.GetField("_vboHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_controller)!);
        _gl.DeleteBuffer((uint)ctrlType.GetField("_elementsHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_controller)!);
        _gl.DeleteVertexArray((uint)ctrlType.GetField("_vertexArrayObject", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_controller)!);

        (ctrlType.GetField("_fontTexture", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_controller) as IDisposable)!.Dispose();

        // Silk.NET.OpenGL.Extensions.ImGui.Shader
        // var _shader = ctrlType.GetField("_shader", BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(_controller);
        // _shader.GetType().GetMethod("Dispose").Invoke(_shader, []);

        ImGuiNET.ImGui.DestroyContext(_controller.Context);

        _controller = null!;
    }

    protected virtual void OnFileDrop(string[] filename, Vector2D<int> position)
    {
    }

    void IDragDropTarget.DragEnter(DragDropContextObject data, uint keyState, Point position, ref uint effect)
    {
        using var _ = new OverrideCurrentWindow(this);
        Logger.Debug("DragEnter: " + data + " at " + position);
    }

    void IDragDropTarget.DragOver(uint keyState, Point position, ref uint effect)
    {
        // Logger.Info("DragOver " + position);
    }

    void IDragDropTarget.DragLeave()
    {
        // Logger.Info("DragLeave");
    }

    void IDragDropTarget.Drop(DragDropContextObject data, uint keyState, Point position, ref uint effect)
    {
        using var _ = new OverrideCurrentWindow(this);
        Logger.Debug("DragDrop: " + data + " at " + position);
        if (data.text != null) {
            if (Path.IsPathFullyQualified(data.text) && (File.Exists(data.text) || Directory.Exists(data.text))) {
                OnFileDrop([data.text], new Vector2D<int>(position.X, position.Y));
            } else {
                // TODO fake input if it's dropped on a text input?
            }
            return;
        }
        if (data.filenames != null) {
            OnFileDrop(data.filenames, new Vector2D<int>(position.X, position.Y));
        }
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
}
