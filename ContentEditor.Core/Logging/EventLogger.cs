namespace ContentEditor;

public class EventLogger : ILogger
{
    public LogSeverity LoggingLevel { get; set; } = 0;
    public event Action<string, LogSeverity>? MessageReceived;

    public void Debug(params object[] msg)
    {
        if (LoggingLevel <= 0) {
            MessageReceived?.Invoke(string.Join(" ", msg.Select(ILogger.MessageText)), LogSeverity.Debug);
        }
    }

    public void Info(object msg)
    {
        if (LoggingLevel <= LogSeverity.Info) {
            MessageReceived?.Invoke(ILogger.MessageText(msg), LogSeverity.Info);
        }
    }

    public void Warn(object msg)
    {
        if (LoggingLevel <= LogSeverity.Warning) {
            MessageReceived?.Invoke(ILogger.MessageText(msg), LogSeverity.Warning);
        }
    }

    public void Error(object msg)
    {
        if (LoggingLevel <= LogSeverity.Error) {
            MessageReceived?.Invoke(ILogger.MessageText(msg), LogSeverity.Error);
        }
    }
}