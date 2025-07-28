using System.Collections.Concurrent;
using System.Diagnostics;
using ContentEditor.App.Windowing;

namespace ContentEditor.App;

internal sealed class MainLoop : IDisposable
{
    private readonly List<WindowBase> windows = new();

    public EditorWindow MainWindow => (EditorWindow)windows.First();
    public IReadOnlyList<WindowBase> Windows => windows.AsReadOnly();
    private readonly ConcurrentQueue<WindowBase> queuedWindows = new();
    private readonly ConcurrentQueue<Action> mainThreadActions = new();

    private readonly List<IUpdateable> updateables = new();
    private readonly Time time = new Time();

    private static MainLoop? _instance;
    internal static MainLoop Instance => _instance!;

    private static readonly Thread MainThread = Thread.CurrentThread;

    internal MainLoop()
    {
        if (_instance == null) {
            _instance = this;
        } else {
            throw new Exception("Multiple main loops not allowed!");
        }
        var mainWindow = new MainWindow();
        mainWindow.InitializeWindow();
        windows.Add(mainWindow);
    }

    private CancellationTokenSource _tokenSource = new CancellationTokenSource();

    public void RunEventLoop()
    {
        if (MainWindow == null)
            return;

        var stopwatch = Stopwatch.StartNew();
        float timer = 0;
        windows.First().InitGraphics(null);
        while (windows.FirstOrDefault()?.IsClosing == false) {
            while (mainThreadActions.TryDequeue(out var act)) {
                try {
                    act.Invoke();
                } catch (Exception e) {
                    Logger.Error(e, "Deferred thread action failed");
                }
            }

            while (queuedWindows.TryDequeue(out var wnd)) {
                wnd.InitGraphics(null);
                windows.Add(wnd);
            }
            var deltaTime = stopwatch.ElapsedTicks / (float)Stopwatch.Frequency;
            stopwatch.Restart();
            time.Update(deltaTime, true);
            // Console.WriteLine($"Event delta: {deltaTime}");
            timer += deltaTime;
            for (int i = 0; i < windows.Count; i++) {
                var sub = windows[i];
                sub.TriggerEvents();
                if (sub.IsClosing) {
                    Console.WriteLine("Closing subwindow " + sub.ID);
                    windows.RemoveAt(i--);
                    sub.DestroyWindow();
                }
            }
            foreach (var s in windows) s.TriggerUpdate();

            deltaTime = stopwatch.ElapsedTicks / (float)Stopwatch.Frequency;
            var nextFrameDelay = WaitFpsLimit(deltaTime, AppConfig.EventLoopMaxFrameTime);
            if (nextFrameDelay > 0) {
                Thread.Sleep(TimeSpan.FromSeconds(nextFrameDelay));
                deltaTime += nextFrameDelay;
            }
        }
    }

    internal int Run()
    {
        RunEventLoop();
        return 0;
    }

    private static float WaitFpsLimit(float deltaTime, float maxFrameTime)
    {
        if (maxFrameTime > 0) {
            var delay = maxFrameTime - deltaTime;
            if (delay > 0) {
                // Logger.Log($"FPs delay time: {deltaTime} => {delay}");
                return delay;
            }
        }
        return 0;
    }

    internal void Register(IUpdateable updateable)
    {
        updateables.Add(updateable);
    }

    public void OpenSubwindow(WindowBase window)
    {
        Console.WriteLine("OpenSubwindow on thread: " + Thread.CurrentThread.ManagedThreadId);
        mainThreadActions.Enqueue(() => {
            window.InitializeWindow();
            queuedWindows.Enqueue(window);
        });
    }

    public void InvokeFromUIThread(Action callback)
    {
        mainThreadActions.Enqueue(callback);
    }

    public void Dispose()
    {
        foreach (var sub in windows) sub.Dispose();
        windows.Clear();
    }
}