namespace ContentEditor.App;

public abstract class EditModeHandler
{
    public abstract string DisplayName { get; }

    public Component? Target { get; private set; }
    public Scene Scene { get; private set; } = null!;

    internal void Init(Scene scene)
    {
        Scene = scene;
    }

    public void SetTarget(Component? component)
    {
        if (Target == component) return;

        var prev = Target;
        Target = component;
        OnTargetChanged(prev);
    }

    protected virtual void OnTargetChanged(Component? previous)
    {
    }

    public virtual void Update()
    {
    }

    public virtual void OnIMGUI()
    {
    }

    public virtual void DrawMainUI()
    {
    }
}