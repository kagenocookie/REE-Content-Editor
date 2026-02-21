namespace ContentEditor.App;

using ContentEditor.App.Internal;
using ContentEditor.App.Windowing;
using ContentEditor.Editor;
using ReeLib;

sealed class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledExceptions;
        MainLoop? loop = null;
        try {
            SetupConfigs();
            loop = new MainLoop();
            if (AppConfig.Instance.EnableUpdateCheck) {
                AutoUpdater.QueueAutoUpdateCheck();
            }
            var result = loop.Run();
            loop.Dispose();
            return result;
        } catch (Exception e) {
            HandleUnhandledExceptions(null!, new UnhandledExceptionEventArgs(e, true));
            try {
                loop?.Dispose();
            } catch (Exception) {
                // ignore, we're on our way out anyway
            }
            return 1;
        }
    }

    private static void HandleUnhandledExceptions(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) {
            File.AppendAllText("crashlog.txt", $"---------------------------\nCrashed in thread {System.Threading.Thread.CurrentThread.ManagedThreadId}\n{DateTime.UtcNow.ToString("O")}\n{ex.Message}\n\n{ex.StackTrace}\n");
#if WINDOWS
            System.Windows.Forms.MessageBox.Show($"""
                Unhandled exception occurred in thread {System.Threading.Thread.CurrentThread.ManagedThreadId}: {ex.Message}

                Error details have been written to crashlog.txt.
                Consider reporting the error on GitHub and include the crashlog.txt and content_editor_log.txt files as well as how to reproduce the issue if possible.

                {ex.StackTrace}
                """);
#endif
        } else {
            File.AppendAllText("crashlog.txt", $"---------------------------\nCrashed in thread {System.Threading.Thread.CurrentThread.ManagedThreadId}\n{DateTime.UtcNow.ToString("O")}\n{e.ExceptionObject}\n");
#if WINDOWS
            System.Windows.Forms.MessageBox.Show($"""
                Unhandled exception occurred in thread {System.Threading.Thread.CurrentThread.ManagedThreadId}
                Error details have been written to crashlog.txt.
                Consider reporting the error on GitHub and include the crashlog.txt and content_editor_log.txt files as well as how to reproduce the issue if possible.

                {e.ExceptionObject}
                """);
#endif
        }
    }

    private static void SetupConfigs()
    {
        AppConfig.LoadConfigs();
        WindowManager.Instance.CloseCallback = (data) => EditorWindow.CurrentWindow!.CloseSubwindow(data);
        WindowManager.Instance.ErrorCallback = (msg, parent) => EditorWindow.CurrentWindow!.AddSubwindow(new ErrorModal("Error", msg, parent?.Handler as IRectWindow));

        var evtLogger = new EventLogger();
        Logger.CurrentLogger = AppConfig.Instance.LogToFile.Get()
            ? new MultiLogger(Logger.CurrentLogger, evtLogger, new FileLogger())
            : new MultiLogger(Logger.CurrentLogger, evtLogger);
        Logger.CurrentLogger.LoggingLevel = (LogSeverity)AppConfig.Instance.LogLevel.Get();
        AppConfig.Instance.LogLevel.ValueChanged += (level) => {
            Logger.CurrentLogger.LoggingLevel = (LogSeverity)level;
        };
        UI.FontSize = AppConfig.Instance.FontSize.Get();
        UI.FontSizeLarge = AppConfig.Instance.FontSize.Get() * 3;
        ResourceRepository.MetadataRemoteSource = AppConfig.Instance.RemoteDataSource.Get() ?? ResourceRepository.MetadataRemoteSource!;
        ConsoleWindow.EventLogger = evtLogger;
        ReeLib.Common.Log.LogCallback = (level, msg) => {
            switch (level) {
                case ReeLib.Common.Log.LogLevel.Debug:
                    Logger.Debug(msg);
                    break;
                case ReeLib.Common.Log.LogLevel.Info:
                    Logger.Info(msg);
                    break;
                case ReeLib.Common.Log.LogLevel.Warn:
                    Logger.Warn(msg);
                    break;
                case ReeLib.Common.Log.LogLevel.Error:
                    Logger.Error(msg);
                    break;
            }
        };
    }
}
