using ContentEditor.App.Github;

namespace ContentEditor.App.Internal;

internal static class AutoUpdater
{
    public static bool UpdateCheckInProgress { get; private set; }

    private const int UpdateCheckIntervalMillis = 2 * 60 * 60 * 1000; // 2-hourly

    private static readonly GithubApi github = new GithubApi();

    public static void QueueAutoUpdateCheck()
    {
        Task.Run(() => CheckForUpdate(false));
    }

    public static void CheckForUpdateInBackground()
    {
        Task.Run(() => CheckForUpdate(true));
    }

    private static async Task CheckForUpdate(bool isManualCheck)
    {
        var config = AppConfig.Instance;
        if (!isManualCheck && !config.EnableUpdateCheck) {
            return;
        }

        var knownLatest = AppConfig.Settings.Changelogs.LatestReleaseVersion;

        if (!isManualCheck && (DateTime.UtcNow - config.LastUpdateCheck.Get()).TotalMilliseconds < UpdateCheckIntervalMillis) {
            AppConfig.IsOutdatedVersion = !string.IsNullOrEmpty(knownLatest) && knownLatest != AppConfig.Version;
            if (!AppConfig.IsOutdatedVersion) {
                _ = Task.Delay(UpdateCheckIntervalMillis).ContinueWith(t => QueueAutoUpdateCheck());
            }
            return;
        }

        UpdateCheckInProgress = true;
        try {
            if (isManualCheck) Logger.Info("Checking for updated version...");
            var data = await github.FetchLatestRelease();
            config.LastUpdateCheck.Set(DateTime.UtcNow);
            knownLatest = data?.TagName?.Replace("v", "");
            if (data != null) {
                // use the main thread just in case to prevent potential multithreading issues
                MainLoop.Instance.InvokeFromUIThread(() => {
                    AppConfig.Settings.Changelogs.StoreReleaseInfo(data);
                });
            }
            if (!string.IsNullOrEmpty(knownLatest) && knownLatest != AppConfig.Version) {
                AppConfig.IsOutdatedVersion = true;
                if (isManualCheck) Logger.Info("New version is available: " + knownLatest);
            } else if (isManualCheck) {
                Logger.Info("We are still up to date");
            } else {
                _ = Task.Delay(UpdateCheckIntervalMillis).ContinueWith(t => QueueAutoUpdateCheck());
            }
        } catch (Exception e) {
            Logger.Warn("Update check failed: " + e.Message);
        } finally {
            UpdateCheckInProgress = false;
        }
    }

}