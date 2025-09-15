namespace ContentEditor;

public class FileLogger : ILogger, IDisposable
{
    public LogSeverity LoggingLevel { get; set; } = 0;
    public static readonly string DefaultLogFilePath = Path.Combine(AppContext.BaseDirectory, "content_editor_log.txt");

    private StreamWriter writer;

    private static string Timestamp => DateTime.UtcNow.ToString("O");

    public FileLogger()
    {
        writer = new StreamWriter(File.Open(DefaultLogFilePath, FileMode.Create, FileAccess.Write, FileShare.Read));
        writer.WriteLine($"[{Timestamp}] INFO: Log initialized");
    }

    public void Debug(params object[] msg)
    {
        if (LoggingLevel <= LogSeverity.Debug) {
            writer.WriteLine($"[{Timestamp}] DEBUG: {string.Join(" ", msg)}");
            writer.Flush();
        }
    }

    public void Info(object msg)
    {
        if (LoggingLevel <= LogSeverity.Info) {
            writer.WriteLine($"[{Timestamp}] INFO: {msg}");
            writer.Flush();
        }
    }

    public void Warn(object msg)
    {
        if (LoggingLevel <= LogSeverity.Warning) {
            writer.WriteLine($"[{Timestamp}] WARN: {msg}");
            writer.Flush();
        }
    }

    public void Error(object msg)
    {
        if (LoggingLevel <= LogSeverity.Error) {
            writer.WriteLine($"[{Timestamp}] ERROR: {msg}");
            writer.Flush();
        }
    }

    public void Dispose()
    {
        writer.Dispose();
    }
}