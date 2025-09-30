using System.Diagnostics;

namespace ContentEditor.App;

public class ProgressContainer
{
    public int progress;
    public int total = 1;
    public string? status;

    public float Percentage => (float)progress / total;
    public bool IsFinished => progress >= total;

    public ProgressContainer(int progress, int endProgress)
    {
        this.progress = progress;
        this.total = endProgress;
    }

    public ProgressContainer()
    {
    }

    public void Init(int currentProgress, int maxProgress, string? currentStatus = null)
    {
        Debug.Assert(maxProgress > 0);
        progress = currentProgress;
        total = maxProgress;
        status = currentStatus;
    }

    public void Increment(string? currentStatus = null)
    {
        Interlocked.Increment(ref progress);
        status = currentStatus;
    }

    public override string ToString() => string.IsNullOrEmpty(status) ? $"{progress}/{total}" : $"{progress}/{total}: {status}";
}