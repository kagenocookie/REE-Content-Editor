namespace ContentEditor;

public class MultiLogger(params ILogger[] loggers) : ILogger
{
    private LogSeverity _loggingLevel = LogSeverity.Info;
    public LogSeverity LoggingLevel
    {
        get => _loggingLevel;
        set {
            foreach (var log in loggers) log.LoggingLevel = value;
        }
    }

    public void Debug(params object[] msg)
    {
        if (LoggingLevel <= LogSeverity.Debug) {
            foreach (var log in loggers) log.Debug(msg);
        }
    }

    public void Info(object msg)
    {
        if (LoggingLevel <= LogSeverity.Info) {
            foreach (var log in loggers) log.Info(msg);
        }
    }

    public void Warn(object msg)
    {
        if (LoggingLevel <= LogSeverity.Warning) {
            foreach (var log in loggers) log.Warn(msg);
        }
    }

    public void Error(object msg)
    {
        if (LoggingLevel <= LogSeverity.Error) {
            foreach (var log in loggers) log.Error(msg);
        }
    }

    public TLogger GetLogger<TLogger>() where TLogger : ILogger
    {
        return loggers.OfType<TLogger>().First();
    }
}