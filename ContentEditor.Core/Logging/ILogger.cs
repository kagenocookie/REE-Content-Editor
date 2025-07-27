namespace ContentEditor;

public interface ILogger
{
    LogSeverity LoggingLevel { get; set; }
    void Debug(params object[] msg);

    void Info(object msg);
    void Info(params object[] msg) => Info(string.Join(" ", msg));

    void Error(object msg) => Info($"ERROR: {msg}");
    void Error(Exception exception, params object[] msg) => Error($"{string.Join(" ", msg)} (Error: {exception.Message})\nStack:\n{exception.StackTrace ?? "N/A"}");
    void Error(params object[] msg) => Error(string.Join(" ", msg));
}

public enum LogSeverity : int
{
    Debug,
    Info,
    Warning,
    Error,
}