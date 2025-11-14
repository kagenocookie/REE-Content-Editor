using System.Collections.Concurrent;
using System.Diagnostics;
using ContentEditor.App.Windowing;
using ContentEditor.BackgroundTasks;

namespace ContentEditor.App;

internal sealed partial class MainLoop : IDisposable
{
    private readonly List<WindowBase> windows = new();

    public EditorWindow MainWindow => (EditorWindow)windows.First();
    public IReadOnlyList<WindowBase> Windows => windows.AsReadOnly();
    private readonly ConcurrentQueue<WindowBase> queuedWindows = new();
    private readonly ConcurrentQueue<Action> mainThreadActions = new();

    public BackgroundTaskService BackgroundTasks { get; } = new(BackgroundTaskConfig.Default);

    private readonly List<IUpdateable> updateables = new();
    private readonly Time time = new Time();

    private static MainLoop? _instance;
    internal static MainLoop Instance => _instance!;

    private static readonly Thread MainThread = Thread.CurrentThread;
    public static bool IsMainThread => MainThread == Thread.CurrentThread;

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
        windows.First().InitGraphics();
        var fpsLimit = new FpsLimiter();
        while (windows.FirstOrDefault()?.IsClosing == false) {
            var deltaTime = fpsLimit.StartFrame();
            while (mainThreadActions.TryDequeue(out var act)) {
                try {
                    act.Invoke();
                } catch (Exception e) {
                    Logger.Error(e, "Deferred thread action failed");
                }
            }

            while (queuedWindows.TryDequeue(out var wnd)) {
                wnd.InitGraphics();
                windows.Add(wnd);
            }
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

            fpsLimit.SetLimit(PlatformUtils.IsAppInForeground() || windows.Any(w => w.DragDropData != null) ? AppConfig.Instance.MaxFps.Get() : AppConfig.Instance.BackgroundMaxFps.Get());
            fpsLimit.TryLimit();
        }
    }

    internal int Run()
    {
        RunEventLoop();
        return 0;
    }

    private sealed partial class FpsLimiter
    {
        private Stopwatch stopwatch = new();
        private int fpsLimit;
        private double maxFrameTime;
        private static readonly double ticksPerSecond = 1 / (double)Stopwatch.Frequency;

        private const double sleepPrecisionLeeway = 0.001;

#if WINDOWS
        [System.Runtime.InteropServices.LibraryImport("Winmm.DLL")]
        private static partial int timeBeginPeriod(uint period);

        static FpsLimiter()
        {
            System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(
                        // ensure we get accurate enough sleep timings for fps limits
                        // we can just keep it active until the process ends
                        timeBeginPeriod(1));
        }
#endif

        public float StartFrame()
        {
            var deltaTime = stopwatch.ElapsedTicks * ticksPerSecond;
            stopwatch.Restart();
            return (float)deltaTime;
        }

        public void SetLimit(int fps)
        {
            fpsLimit = fps;
            maxFrameTime = 1.0 / fps;
        }

        public void TryLimit()
        {
            if (maxFrameTime > 0) {
                var deltaTime = stopwatch.ElapsedTicks * ticksPerSecond;
                var delay = maxFrameTime - deltaTime;
                if (delay <= 0) return;

                var sleepMs = (int)((delay - sleepPrecisionLeeway) * 1000);
                if (sleepMs > 0) {
                    var sleepStart = stopwatch.Elapsed;
                    Thread.Sleep(sleepMs);
                    delay -= (stopwatch.Elapsed - sleepStart).TotalSeconds;
                }

                while (stopwatch.ElapsedTicks * ticksPerSecond < maxFrameTime) { }
            }
        }
    }

    private static float WaitFpsLimit(float deltaTime, float maxFrameTime)
    {
        if (maxFrameTime > 0) {
            var delay = maxFrameTime - deltaTime;
            if (delay > 0) {
                // Logger.Debug($"FPS delay time: {deltaTime} => {delay}");
                return delay;
            }
        }
        return 0;
    }

    internal void Register(IUpdateable updateable)
    {
        updateables.Add(updateable);
    }

    public void OpenNewWindow(WindowBase window)
    {
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
        BackgroundTasks.Dispose();
    }
}