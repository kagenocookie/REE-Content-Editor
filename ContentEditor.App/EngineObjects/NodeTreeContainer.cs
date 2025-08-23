using System.Collections.Concurrent;

namespace ContentEditor.App;

public class NodeTreeContainer
{
    private readonly ConcurrentQueue<Action> deferred = new();

    public void DeferAction(Action action)
    {
        deferred.Enqueue(action);
    }

    internal void ExecuteDeferredActions()
    {
        while (deferred.TryDequeue(out var act)) {
            try {
                act.Invoke();
            } catch (Exception e) {
                Logger.Error(e, "Deferred action failed");
            }
        }
    }
}
