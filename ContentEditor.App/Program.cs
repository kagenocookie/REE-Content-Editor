namespace ContentEditor.App;

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
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
                Task.Run(CheckForUpdate);
            }
            var result = loop.Run();
            loop.Dispose();
            return result;
        } catch (Exception e) {
            HandleUnhandledExceptions(null!, new UnhandledExceptionEventArgs(e, true));
            loop?.Dispose();
            return 1;
        }
    }

    private static async Task CheckForUpdate()
    {
        try {
            var config = AppConfig.Instance;
            if (AppConfig.Version == "0.0.0") {
                config.LatestVersion.Set(null);
                return;
            }
            if ((DateTime.UtcNow - config.LastUpdateCheck.Get()) < TimeSpan.FromHours(4)) {
                AppConfig.IsOutdatedVersion = config.LatestVersion.Get() != null && config.LatestVersion.Get() != AppConfig.Version;
                return;
            }
            var http = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.github.com/repos/kagenocookie/REE-Content-Editor/releases/latest"));

            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("User-Agent", $"REE-Content-Editor/{AppConfig.Version}");
            var response = await http.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK) {
                var content = await response.Content.ReadAsStringAsync();
                config.LastUpdateCheck.Set(DateTime.UtcNow);
                try {
                    var data = JsonSerializer.Deserialize<GithubReleaseInfo>(content);
                    var remoteTag = data?.TagName?.Replace("v", "");
                    config.LatestVersion.Set(remoteTag);
                    if (remoteTag != null && remoteTag != AppConfig.Version) {
                        AppConfig.IsOutdatedVersion = true;
                    }
                } catch (Exception) {
                    // ignore
                }
            }
        } catch (Exception e) {
            Logger.Warn("Automatic update check failed: " + e.Message);
        }
    }

    private sealed class GithubReleaseInfo
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }
    }

    private static void HandleUnhandledExceptions(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) {
            File.AppendAllText("crashlog.txt", $"---------------------------\nCrashed in thread {System.Threading.Thread.CurrentThread.ManagedThreadId}\n{DateTime.UtcNow.ToString("O")}\n{ex.Message}\n\n{ex.StackTrace}\n");
#if WINDOWS
            System.Windows.Forms.MessageBox.Show($"""
                Unhandled exception occurred in thread {System.Threading.Thread.CurrentThread.ManagedThreadId}: {ex.Message}

                Error details have been written to crashlog.txt.
                Consider reporting the error on GitHub and include the crashlog.txt file as well as how to reproduce the issue if possible.

                {ex.StackTrace}
                """);
#endif
        } else {
            File.AppendAllText("crashlog.txt", $"---------------------------\nCrashed in thread {System.Threading.Thread.CurrentThread.ManagedThreadId}\n{DateTime.UtcNow.ToString("O")}\n{e.ExceptionObject}\n");
#if WINDOWS
            System.Windows.Forms.MessageBox.Show($"""
                Unhandled exception occurred in thread {System.Threading.Thread.CurrentThread.ManagedThreadId}
                Error details have been written to crashlog.txt.
                Consider reporting the error on GitHub and include the crashlog.txt file as well as how to reproduce the issue if possible.

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
        Logger.CurrentLogger = new MultiLogger(Logger.CurrentLogger, evtLogger);
        Logger.CurrentLogger.LoggingLevel = (LogSeverity)AppConfig.Instance.LogLevel.Get();
        AppConfig.Instance.LogLevel.ValueChanged += (level) => {
            Logger.CurrentLogger.LoggingLevel = (LogSeverity)level;
        };
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
