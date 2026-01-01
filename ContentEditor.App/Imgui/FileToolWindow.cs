using ContentEditor.Editor;
using ContentPatcher;

namespace ContentEditor.App;

public abstract class FileToolWindow : SimpleWindow, IFileHandleReferenceHolder
{
    public FileHandle File { get; }

    public virtual bool CanClose => true;

    public IRectWindow? Parent => null;

    public FileToolWindow(UIContext parentContext, FileHandle file) : base(parentContext)
    {
        File = file;
        file.References.Add(this);
    }

    public virtual void Close()
    {
    }

    public override bool RequestClose()
    {
        File.References.Remove(this);
        return false;
    }
}
