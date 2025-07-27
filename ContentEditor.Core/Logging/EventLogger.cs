namespace ContentEditor;

public class EventLogger : ILogger
{
    public LogSeverity LoggingLevel { get; set; } = 0;
    public event Action<string, LogSeverity>? MessageReceived;

    public void Debug(params object[] msg)
    {
        if (LoggingLevel <= 0) {
            MessageReceived?.Invoke(string.Join(" ", msg), LogSeverity.Debug);
        }
    }

    public void Info(object msg)
    {
        if (LoggingLevel <= LogSeverity.Info) {
            MessageReceived?.Invoke(msg.ToString()!, LogSeverity.Info);
        }
    }

    public void Error(object msg)
    {
        if (LoggingLevel <= LogSeverity.Error) {
            MessageReceived?.Invoke(msg.ToString()!, LogSeverity.Error);
        }
    }
}