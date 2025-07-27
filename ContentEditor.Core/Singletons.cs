namespace ContentEditor;

public static class Logger
{
    public static ILogger CurrentLogger { get; set; } = new ConsoleLogger();

    public static void Debug(params object[] msg) => CurrentLogger.Debug(msg);
    public static void Info<T>(T msg) => CurrentLogger.Info(msg?.ToString()!);
    public static void Info(params object[] msg) => CurrentLogger.Info(msg);

    public static void Error(object msg) => CurrentLogger.Error(msg);
    public static void Error(Exception exception, params object[] msg) => CurrentLogger.Error(exception, msg);
    public static void Error(params object[] msg) => CurrentLogger.Error(msg);
}


public class Singleton<T>
{
    private static T? _instance;
    public static T Instance => _instance ??= Activator.CreateInstance<T>();
};

public class WindowManager : Singleton<WindowManager>
{
    public Action<WindowData>? CloseCallback;
    public Action<string, WindowData?>? ErrorCallback;

    public void CloseWindow(WindowData data)
    {
        if (CloseCallback == null) throw new NotImplementedException();

        CloseCallback.Invoke(data);
    }

    public void ShowError(string message, WindowData? parentWindow = null)
    {
        Logger.Error(message);
        if (ErrorCallback == null) throw new NotImplementedException();

        ErrorCallback.Invoke(message, parentWindow);
    }
}

public class RefCounted<T>(T instance) where T : IDisposable
{
    public T Instance => instance;
    private int refCount = 0;

    public void AddRef()
    {
        refCount++;
    }

    public bool Release()
    {
        refCount--;
        if (refCount <= 0) {
            Dispose();
            return true;
        }

        return false;
    }

    protected void Dispose() => instance.Dispose();
}
