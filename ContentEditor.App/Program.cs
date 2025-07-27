namespace ContentEditor.App;

using ContentEditor.App.Windowing;
using ContentEditor.Editor;
using ReeLib;

sealed class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try {
            SetupConfigs();
            using var loop = new MainLoop();
            var result = loop.Run();
            return result;
        } catch (Exception e) {
            File.AppendAllText("crashlog.txt", $"---------------------------\n{DateTime.UtcNow.ToString("O")}\n{e.Message}\n{e.StackTrace}\n");
            return 1;
        }
    }

    private static void SetupConfigs()
    {
        AppConfig.LoadConfigs();
        WindowManager.Instance.CloseCallback = (data) => EditorWindow.CurrentWindow!.CloseSubwindow(data);
        WindowManager.Instance.ErrorCallback = (msg, parent) => EditorWindow.CurrentWindow!.AddSubwindow(new ErrorModal("Error", msg, parent?.Handler as IRectWindow));

        var evtLogger = new EventLogger();
        Logger.CurrentLogger = new MultiLogger(Logger.CurrentLogger, evtLogger);
        Logger.CurrentLogger.LoggingLevel = (LogSeverity)AppConfig.Instance.LogLevel.Get();
        AppConfig.Instance.LogLevel.ValueChanged += (level) => {
            Logger.CurrentLogger.LoggingLevel = (LogSeverity)level;
        };
        ResourceRepository.MetadataRemoteSource = AppConfig.Instance.RemoteDataSource.Get() ?? ResourceRepository.MetadataRemoteSource!;
        ConsoleWindow.EventLogger = evtLogger;
    }
}
