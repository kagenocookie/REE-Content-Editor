namespace ContentEditor;

public interface ILogger
{
    // Translate plain messages only; translated objects and dynamic arguments remain verbatim.
    internal static string MessageText(object msg)
        => msg is string text ? Core.UiText.T(text) : msg.ToString() ?? "";

    LogSeverity LoggingLevel { get; set; }
    void Debug(params object[] msg);

    void Info(object msg);
    void Info(params object[] msg) => Info(string.Join(" ", msg));
    void Warn(object msg) => Info("WARNING: " + string.Join(" ", msg));

    void Error(object msg) => Info($"ERROR: {msg}");
    void Error(Exception exception, params object[] msg) => Error($"{string.Join(" ", msg)} (Error: {exception.Message})\nStack:\n{exception.StackTrace ?? "N/A"}");
    void Error(params object[] msg) => Error(string.Join(" ", msg));

    void Log(LogSeverity level, object msg)
    {
        switch (level) {
            case LogSeverity.Debug:
                Debug(msg);
                break;
            case LogSeverity.Info:
                Info(msg);
                break;
            case LogSeverity.Warning:
                Warn(msg);
                break;
            case LogSeverity.Error:
                Error(msg);
                break;
        }
    }
}

public enum LogSeverity : int
{
    Debug,
    Info,
    Warning,
    Error,
}
