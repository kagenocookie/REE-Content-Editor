namespace ContentEditor.App.ImguiHandling;

public interface IInspectorController
{
    void SetPrimaryInspector(object? target);
    ObjectInspector AddInspector(object target);
    void EmitSave();

    object? PrimaryTarget { get; }
}
