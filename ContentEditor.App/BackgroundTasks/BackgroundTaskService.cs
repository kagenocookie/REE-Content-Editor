using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using ContentEditor.App;

namespace ContentEditor.BackgroundTasks;

public struct BackgroundTaskConfig
{
    public int MaxThreads { get; set; }

    public static readonly BackgroundTaskConfig Default = new BackgroundTaskConfig() {
        MaxThreads = 2
    };
}

public sealed class BackgroundTaskService : IDisposable
{
    private readonly ConcurrentQueue<IBackgroundTask> waitingTasks = new();

    private readonly List<BackgroundTaskWorker> workers = new();
    private readonly ConcurrentStack<BackgroundTaskWorker> freeWorkers = new();

    public int PendingTasks => waitingTasks.Count + (workers.Count - freeWorkers.Count);
    public int ActiveWorkerCount => workers.Count - freeWorkers.Count;
    public IEnumerable<(string status, float progress)> CurrentJobs {
        get {
            var list = new List<(string, float)>();
            for (int i = 0; i < workers.Count; ++i) {
                var worker = workers[i];
                var task = worker.CurrentTask;
                if (task != null) {
                    var status = task.Status ?? "Finalizing";
                    var pct = task.Progress;
                    if (pct > 1) {
                        list.Add(($"{status} | {Math.Round(pct * 10) / 10} | {worker}", pct));
                    } else if (pct >= 0) {
                        list.Add(($"{status} | {Math.Round(pct * 100)}% | {worker}", pct));
                    } else {
                        list.Add(($"{status} | {worker}", -1));
                    }
                }
            }
            return list;
        }
    }

    private readonly object _lock = new();
    private readonly BackgroundTaskConfig config;

    public BackgroundTaskService()
    {
        config = BackgroundTaskConfig.Default;
    }

    public BackgroundTaskService(BackgroundTaskConfig config)
    {
        this.config = config;
    }

    public void Queue(IBackgroundTask task)
    {
        lock (_lock) {
            BackgroundTaskWorker? worker = null;
            if (!freeWorkers.TryPop(out worker)) {
                if (workers.Count - freeWorkers.Count < config.MaxThreads) {
                    workers.Add(worker = new BackgroundTaskWorker(this));
                }
            }

            if (worker == null) {
                // no free workers, wait for one of the tasks to finish
                waitingTasks.Enqueue(task);
                return;
            }

            worker.StartTask(task);
        }
    }

    public void CancelTask(IBackgroundTask pendingTask)
    {
        lock (_lock) {
            pendingTask.IsCancelled = true;
            foreach (var w in workers) {
                if (w.CurrentTask == pendingTask) {
                    w.CancelAsync();
                    break;
                }
            }
        }
    }

    private bool TryGetNextTask(BackgroundTaskWorker worker, [MaybeNullWhen(false)] out IBackgroundTask nextTask)
    {
        lock (_lock) {
            while (waitingTasks.TryDequeue(out nextTask)) {
                if (!nextTask.IsCancelled) {
                    return true;
                }
            }

            return false;
        }
    }

    private void FreeWorker(BackgroundTaskWorker worker)
    {
        lock (_lock) {
            freeWorkers.Push(worker);
        }
    }

    public void Dispose()
    {
        foreach (var worker in workers) {
            worker.Dispose();
        }
    }

    public bool HasPendingTask<T>() => workers.Any(w => w.CurrentTask is T) || waitingTasks.Any(t => t is T);

    private class BackgroundTaskWorker : BackgroundWorker, IDisposable
    {
        private CancellationTokenSource tokenSource = new();
        private readonly BackgroundTaskService service;

        public IBackgroundTask? CurrentTask { get; private set; }

        private BackgroundResources? resources;

        public BackgroundTaskWorker(BackgroundTaskService service)
        {
            WorkerSupportsCancellation = true;
            this.service = service;
        }

        public void StartTask(IBackgroundTask task)
        {
            CurrentTask = task;
            if (!IsBusy) {
                RunWorkerAsync();
            }
        }

        public new void CancelAsync()
        {
            tokenSource.Cancel();
            CurrentTask = null;
            tokenSource = new();
            base.CancelAsync();
        }

        protected override void OnDoWork(DoWorkEventArgs args)
        {
            resources = BackgroundResources.Instance.Value;
            var token = tokenSource.Token;
            while (!CancellationPending && !token.IsCancellationRequested) {
                if (CurrentTask == null) {
                    Thread.Sleep(500);
                    continue;
                }

                try {
                    CurrentTask?.Execute(token);
                } catch (Exception e) {
                    MainLoop.Instance.InvokeFromUIThread(() => Logger.Error($"Background task {CurrentTask} failed: " + e.Message));
                }

                CurrentTask = null;
                if (!service.TryGetNextTask(this, out var nextTask)) {
                    break;
                }

                CurrentTask = nextTask;
            }
        }

        protected override void OnRunWorkerCompleted(RunWorkerCompletedEventArgs e)
        {
            resources?.ReleaseResources();
            base.OnRunWorkerCompleted(e);
            if (service.TryGetNextTask(this, out var task)) {
                StartTask(task);
            } else {
                service.FreeWorker(this);
            }
        }

        protected override void Dispose(bool disposing)
        {
            CancelAsync();
            base.Dispose(disposing);
        }

        public override string ToString() => CurrentTask?.ToString() ?? "BackgroundWorker";
    }
}

public interface IBackgroundTask
{
    bool IsCancelled { get; internal set; }
    string? Status { get; }
    float Progress => -1;

    void Execute(CancellationToken token = default);
}

public static class BackgroundTaskExtensions
{
    public static void LogError(this IBackgroundTask task, string msg)
    {
        MainLoop.Instance.InvokeFromUIThread(() => Logger.Error(msg));
    }
}