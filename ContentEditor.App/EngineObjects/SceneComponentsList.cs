using System.Runtime.CompilerServices;

namespace ContentEditor.App;

public struct SceneComponentsList<T> where T : class
{
    public readonly HashSet<T> components = new();

    public bool IsEmpty => components.Count == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T component) => components.Add(component);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove(T component) => components.Remove(component);

    public SceneComponentsList()
    {
    }
}
