namespace ContentEditor;

public class ConsoleLogger : ILogger
{
    public LogSeverity LoggingLevel { get; set; } = 0;

    public void Debug(params object[] msg)
    {
        if (LoggingLevel <= LogSeverity.Debug) {
            Console.WriteLine("DEBUG: " + string.Join(" ", msg));
        }
    }

    public void Info(object msg)
    {
        if (LoggingLevel <= LogSeverity.Info) {
            Console.WriteLine(msg);
        }
    }

    public void Error(object msg)
    {
        if (LoggingLevel <= LogSeverity.Error) {
            Console.Error.WriteLine(msg);
        }
    }
}