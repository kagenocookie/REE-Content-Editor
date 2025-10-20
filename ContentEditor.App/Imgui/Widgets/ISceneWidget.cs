using System.Numerics;
using ImGuiNET;

namespace ContentEditor.App;

public interface ISceneWidget : IObjectUIHandler
{
    abstract static string WidgetName { get; }
}