namespace ContentEditor.App;

public class EntityFieldHandler : IObjectUIHandler
{
    public static readonly EntityFieldHandler Instance = new ();

    public void OnIMGUI(UIContext context)
    {
        context.ShowChildrenUI();
    }
}
