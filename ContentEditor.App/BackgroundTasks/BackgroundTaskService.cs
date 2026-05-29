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
                task.TaskStatus = TaskStatus.WaitingForActivation;
                waitingTasks.Enqueue(task);
                return;
            }

            task.TaskStatus = TaskStatus.WaitingToRun;
            worker.StartTask(task);
        }
    }

    public Task QueueAwait<T>(T task) where T : class, IBackgroundTask
    {
        Queue(task);
        return Await<T>(t => t == task);
    }

    public void CancelTask(IBackgroundTask pendingTask)
    {
        lock (_lock) {
            pendingTask.TaskStatus = TaskStatus.Canceled;
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
                if (!nextTask.IsEnded) {
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

    public Task Await<T>() where T : class, IBackgroundTask => Await<T>(f => true);
    public async Task Await<T>(Func<T, bool> condition) where T : class, IBackgroundTask
    {
        var task = (T?)workers.FirstOrDefault(w => w.CurrentTask is T cc && condition(cc))?.CurrentTask
            ?? waitingTasks.OfType<T>().FirstOrDefault(t => condition(t));

        if (task == null) return;

        while (!task.IsEnded) {
            await Task.Delay(25);
        }

        if (task.TaskStatus == TaskStatus.Canceled) {
            throw new OperationCanceledException("Background task has been canceled");
        }
        if (task.TaskStatus == TaskStatus.Faulted) {
            throw new Exception("Background task failed to execute");
        }
    }

    public bool HasPendingTask<T>() => workers.Any(w => w.CurrentTask is T) || waitingTasks.Any(t => t is T);
    public bool HasPendingTask<T>(Func<T, bool> condition) => workers.Any(w => w.CurrentTask is T cc && condition(cc)) || waitingTasks.Any(t => t is T cc && condition(cc));
    public T? GetPendingTask<T>() where T : class => workers.FirstOrDefault(w => w.CurrentTask is T)?.CurrentTask as T ?? waitingTasks.FirstOrDefault(t => t is T) as T;

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
            task.TaskStatus = TaskStatus.WaitingToRun;
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
                    var task = CurrentTask;
                    if (task != null) {
                        task.TaskStatus = TaskStatus.Running;
                        task.Execute(token).Wait();
                        task.TaskStatus = TaskStatus.RanToCompletion;
                    }
                } catch (Exception e) {
                    MainLoop.Instance.InvokeFromUIThread(() => Logger.Error($"Background task {CurrentTask} failed: " + e.Message));
                    CurrentTask.TaskStatus = TaskStatus.Faulted;
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
    TaskStatus TaskStatus { get; internal set; }
    string? Status { get; }
    float Progress => -1;

    bool IsCompletedSuccessfully => TaskStatus == TaskStatus.RanToCompletion;
    bool IsEnded => TaskStatus is TaskStatus.Canceled or TaskStatus.Faulted or TaskStatus.RanToCompletion;

    Task Execute(CancellationToken token = default);
}

public static class BackgroundTaskExtensions
{
    public static void LogError(this IBackgroundTask task, string msg)
    {
        MainLoop.Instance.InvokeFromUIThread(() => Logger.Error(msg));
    }
}