using System.Numerics;

namespace ContentEditor.App.Graphics;

public class GizmoState(Scene scene)
{
    private List<(int id, object target, HandleContainer handles)> previousChildren = new();
    private List<(int id, object target, HandleContainer handles)> children = new();

    public Scene Scene { get; } = scene;

    private class HandleContainer
    {
        public readonly List<object> handles = new();
    }

    public void Push(object obj)
    {
        var handles = previousChildren.ElementAtOrDefault(children.Count).handles ?? new HandleContainer();
        children.Add((children.Count, obj, handles));
    }

    public bool PositionHandle(ref Vector3 position, float screenSize = 0.5f, Vector3 axis = default)
    {
        var current = children.Last();
        //
        return false;
    }
}
