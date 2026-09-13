using System.Numerics;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.ImguiHandling;

public sealed class ConditionalDynamicUIHandler(List<(IResourceCondition, Func<IObjectUIHandler, IObjectUIHandler>?)> handlers, Func<UIContext, IObjectUIHandler> defaultHandler) : IObjectUIHandler, IDisposable
{
    private IResourceCondition? lastPassedCondition;
    private IObjectUIHandler? currentHandler;

    public void OnIMGUI(UIContext context)
    {
        var handler = DetermineHandler(context);
        if (handler == null) {
            ImGui.TextColored(Colors.Warning, "Unable to handle object " + context.GetRaw());
            return;
        }

        handler.OnIMGUI(context);
    }

    private IObjectUIHandler? DetermineHandler(UIContext context)
    {
        var obj = context.GetRaw();
        foreach (var (condition, handler) in handlers) {
            if (condition.IsEnabled(context)) {
                if (condition != lastPassedCondition) {
                    lastPassedCondition = condition;
                    (currentHandler as IDisposable)?.Dispose();
                    currentHandler = handler?.Invoke(defaultHandler.Invoke(context)) ?? defaultHandler.Invoke(context);
                }
                return currentHandler;
            }
        }

        if (lastPassedCondition != null) {
            lastPassedCondition = null;
            (currentHandler as IDisposable)?.Dispose();
            return currentHandler = defaultHandler.Invoke(context);
        }

        currentHandler ??= defaultHandler.Invoke(context);
        return currentHandler;
    }

    public void Dispose()
    {
        (currentHandler as IDisposable)?.Dispose();
    }
}
