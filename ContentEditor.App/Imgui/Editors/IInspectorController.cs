namespace ContentEditor.App.ImguiHandling;

public interface IInspectorController
{
    public void SetPrimaryInspector(object target);
    public ObjectInspector AddInspector(object target);
    public object? PrimaryTarget { get; }
}
