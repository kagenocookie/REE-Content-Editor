using System.Numerics;

namespace ContentEditor.App;

public interface ISceneWidget : IObjectUIHandler
{
    abstract static string WidgetName { get; }
}