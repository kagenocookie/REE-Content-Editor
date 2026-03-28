namespace ContentEditor;

public abstract class UIService
{
    public virtual void ShowMessage(string message, LogSeverity level = LogSeverity.Info, UIContext? context = null, params (string? label, Action action)[] buttons) => Logger.Log(level, message);
    public abstract void SaveAs(FileHandleBase file, UIContext? context = null);
    public abstract void SaveToBundle(FileHandleBase file, UIContext? context = null);
}

public class UIServiceStub : UIService
{
    public override void SaveAs(FileHandleBase file, UIContext? context = null) => throw new NotImplementedException();
    public override void SaveToBundle(FileHandleBase file, UIContext? context = null) => throw new NotImplementedException();
}

public class FileHandleBase { }
