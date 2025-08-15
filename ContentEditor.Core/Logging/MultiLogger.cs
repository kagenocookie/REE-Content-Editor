namespace ContentEditor;

public class MultiLogger(ILogger logger1, ILogger logger2) : ILogger
{
    private LogSeverity _loggingLevel = LogSeverity.Info;
    public LogSeverity LoggingLevel
    {
        get => _loggingLevel;
        set => logger2.LoggingLevel = logger1.LoggingLevel = _loggingLevel = value;
    }

    public void Debug(params object[] msg)
    {
        if (LoggingLevel <= LogSeverity.Debug) {
            logger1.Debug(msg);
            logger2.Debug(msg);
        }
    }

    public void Info(object msg)
    {
        if (LoggingLevel <= LogSeverity.Info) {
            logger1.Info(msg);
            logger2.Info(msg);
        }
    }

    public void Warn(object msg)
    {
        if (LoggingLevel <= LogSeverity.Warning) {
            logger1.Warn(msg);
            logger2.Warn(msg);
        }
    }

    public void Error(object msg)
    {
        if (LoggingLevel <= LogSeverity.Error) {
            logger1.Error(msg);
            logger2.Error(msg);
        }
    }

    public TLogger GetLogger<TLogger>() where TLogger : ILogger
    {
        return this is TLogger t ? t : logger1 is TLogger t2 ? t2 : logger2 is TLogger t3 ? t3 : throw new Exception();
    }
}