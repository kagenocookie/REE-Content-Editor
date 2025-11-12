using System.Collections.Concurrent;
using System.ComponentModel;
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
    public IEnumerable<string> CurrentJobs {
        get {
            var list = new List<string>();
            for (int i = 0; i < workers.Count; ++i) {
                var worker = workers[i];
                if (worker.IsBusy) {
                    var task = worker.CurrentTask?.Status ?? "Finalizing";
                    list.Add(task + " | " + worker.ToString());
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

    private void FinishTask(BackgroundTaskWorker worker)
    {
        lock (_lock) {
            if (waitingTasks.TryDequeue(out var nextTask)) {
                worker.StartTask(nextTask);
            } else {
                freeWorkers.Push(worker);
            }
        }
    }

    public void Dispose()
    {
        foreach (var worker in workers) {
            worker.Dispose();
        }
    }

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
            CurrentTask = null;
            tokenSource.Cancel();
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
                service.FinishTask(this);
                CurrentTask = null;
            }
        }

        protected override void OnRunWorkerCompleted(RunWorkerCompletedEventArgs e)
        {
            resources?.ReleaseResources();
            base.OnRunWorkerCompleted(e);
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
    // /// <summary>
    // /// 0-1 value of how far along the task is. If -1, will be treated as not having a way to track progress.
    // /// </summary>
    // public float Progress { get; }
    public string? Status { get; }

    public void Execute(CancellationToken token = default);
}

public static class BackgroundTaskExtensions
{
    public static void LogError(this IBackgroundTask task, string msg)
    {
        MainLoop.Instance.InvokeFromUIThread(() => Logger.Error(msg));
    }
}