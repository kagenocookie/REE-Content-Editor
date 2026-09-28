namespace ContentEditor.App;

/// <summary>
/// A window in the viewport slot that provides its own content for the hierarchy and inspector panels while it's the active viewport window.
/// When the active viewport window doesn't implement this (e.g. the scene view), the panels show their regular scene and inspector tabs.
/// </summary>
public interface IViewportDocument
{
    void OnHierarchyIMGUI();
    void OnInspectorIMGUI();
}
