using System.Drawing;

namespace ContentEditor.App;

public interface IDragDropTarget
{
    void DragEnter(DragDropContextObject data, uint keyState, Point position, ref uint effect);
    void DragOver(uint keyState, Point position, ref uint effect);
    void DragLeave();
    void Drop(DragDropContextObject data, uint keyState, Point position, ref uint effect);
}

public class DragDropContextObject
{
    public string? text;
    public string[]? filenames;

    public override string ToString()
    {
        if (filenames != null && filenames.Length > 1) return $"{filenames.Length} Files";
        if (filenames != null && filenames.Length == 1) return $"File: {filenames[0]}";
        if (text != null) return $"Text: {text}";

        return nameof(DragDropContextObject);
    }
}
