namespace ContentEditor.App.ImguiHandling;

public class FloatRangeHandler(float min, float max) : IObjectUIHandler
{
    public static readonly FloatRangeHandler Range01 = new(0, 1);

    public unsafe void OnIMGUI(UIContext context)
    {
        var num = context.Get<float>();
        if (ImGui.SliderFloat(context.label, ref num, min, max)) {
            UndoRedo.RecordSet(context, num);
        }
    }
}
