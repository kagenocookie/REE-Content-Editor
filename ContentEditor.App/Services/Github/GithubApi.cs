using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContentEditor.App.Github;

public class GithubApi
{
    private const string RepoPath = "kagenocookie/REE-Content-Editor";
    public const string MainRepositoryUrl = $"https://github.com/{RepoPath}";
    public const string WikiUrl = $"https://github.com/{RepoPath}/wiki";
    public const string LatestReleaseUrl = $"https://github.com/{RepoPath}/releases/latest";

    public async Task<GithubReleaseInfo?> FetchLatestRelease()
    {
        var (http, request) = CreateRequest($"https://api.github.com/repos/{RepoPath}/releases/latest");

        var response = await http.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.OK) {
            var content = await response.Content.ReadAsStringAsync();
            try {
                var data = JsonSerializer.Deserialize<GithubReleaseInfo>(content);
                return data;
            } catch (Exception e) {
                Logger.Debug("Failed to fetch release info: " + e.Message);
            }
        }
        return null;
    }

    public async Task<List<GithubCommit>?> FetchCommits()
    {
        var (http, request) = CreateRequest($"https://api.github.com/repos/{RepoPath}/commits");

        var response = await http.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.OK) {
            var content = await response.Content.ReadAsStringAsync();
            try {
                var data = JsonSerializer.Deserialize<List<GithubCommit>>(content);
                return data;
            } catch (Exception e) {
                Logger.Debug("Failed to fetch commit info: " + e.Message);
            }
        }
        return null;
    }


    public async Task<GithubReleaseInfo?> FetchWorkflowRuns()
    {
        var (http, request) = CreateRequest($"https://api.github.com/repos/{RepoPath}/actions/workflows");
        throw new NotImplementedException();
    }

    private static (HttpClient, HttpRequestMessage) CreateRequest(string url)
    {
        var http = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("User-Agent", $"REE-Content-Editor/{AppConfig.Version}");
        return (http, request);
    }
}
public sealed class GithubReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }

    [JsonIgnore] public DateTime ReleaseDate => PublishedAt == DateTime.MinValue ? CreatedAt : PublishedAt;

    [JsonPropertyName("assets")]
    public List<GithubReleaseAsset> Assets { get; set; } = new();
}

public sealed class GithubReleaseAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

public sealed class GithubCommit
{
    [JsonPropertyName("sha")]
    public string? Sha { get; set; }

    [JsonPropertyName("commit")]
    public GithubCommitInfo Commit { get; set; } = new();
}

public sealed class GithubCommitInfo
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("author")]
    public GithubCommitAuthor? Author { get; set; }
}

public sealed class GithubCommitAuthor
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }
}
