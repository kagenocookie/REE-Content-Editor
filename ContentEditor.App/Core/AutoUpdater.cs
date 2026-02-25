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
            if (AppConfig.IsDebugBuild) {
                var commits = config.JsonSettings.Changelogs.FindCurrentAndNewCommits();
                AppConfig.IsOutdatedVersion = AppConfig.RevisionHash != null && commits.Count > 0 && commits[0].Sha != AppConfig.RevisionHash;
            } else {
                AppConfig.IsOutdatedVersion = !string.IsNullOrEmpty(knownLatest) && knownLatest != AppConfig.Version;
            }

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
            if (!AppConfig.IsDebugBuild) {
                if (!string.IsNullOrEmpty(knownLatest) && knownLatest != AppConfig.Version) {
                    AppConfig.IsOutdatedVersion = true;
                    if (isManualCheck) Logger.Info("New version is available: " + knownLatest);
                } else if (isManualCheck) {
                    Logger.Info("We are still up to date");
                } else {
                    _ = Task.Delay(UpdateCheckIntervalMillis).ContinueWith(t => QueueAutoUpdateCheck());
                }
            }

            await CheckCommits(isManualCheck);
        } catch (Exception e) {
            Logger.Warn("Update check failed: " + e.Message);
        } finally {
            UpdateCheckInProgress = false;
        }
    }

    private static async Task CheckCommits(bool isManualCheck)
    {
        var config = AppConfig.Instance;
        var revision = AppConfig.RevisionHash;

        var knownLatestCommitSha = AppConfig.Settings.Changelogs.Commits.FirstOrDefault()?.Sha;

        try {
            var commits = await github.FetchCommits();
            if (commits != null) {
                // use the main thread just in case to prevent potential multithreading issues
                MainLoop.Instance.InvokeFromUIThread(() => {
                    AppConfig.Settings.Changelogs.Commits = commits;
                    AppConfig.Settings.Save();

                    if (AppConfig.IsDebugBuild) {
                        var activeCommits = config.JsonSettings.Changelogs.FindCurrentAndNewCommits();
                        if (string.IsNullOrEmpty(revision)) {
                            if (isManualCheck) Logger.Info("Latest commit: " + activeCommits.First().Commit.Message);
                            return;
                        }
                        var currentLatest = activeCommits.FirstOrDefault()?.Sha;

                        if (activeCommits.FirstOrDefault()?.Sha?.StartsWith(revision) == true) {
                            if (isManualCheck) {
                                Logger.Info("We are probably still up to date");
                            } else {
                                _ = Task.Delay(UpdateCheckIntervalMillis).ContinueWith(t => QueueAutoUpdateCheck());
                            }
                        } else if (!string.IsNullOrEmpty(knownLatestCommitSha) && !string.IsNullOrEmpty(currentLatest) && knownLatestCommitSha != currentLatest && !currentLatest.StartsWith(revision)) {
                            AppConfig.IsOutdatedVersion = true;
                            if (isManualCheck) Logger.Info("New commit detected: " + activeCommits[0].Commit.Message);
                        } else {
                            if (isManualCheck) Logger.Info("We are probably still up to date");
                        }
                    }
                });
            }

        } catch (Exception e) {
            Logger.Warn("Commit lookup failed: " + e.Message);
        }
    }
}