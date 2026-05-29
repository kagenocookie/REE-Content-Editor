using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;

namespace ContentEditor.App.Translation;

public class GoogleTranslate
{
    public static async Task<string> Translate(string? sourceLang, string targetLang, string text)
    {
        // var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sourceLang ?? "auto"}&tl={targetLang}&dt=t&q={HttpUtility.UrlEncode(text)}";
        var url = $"https://clients5.google.com/translate_a/t?client=gtx&sl={sourceLang ?? "auto"}&tl={targetLang}&dt=t&q={HttpUtility.UrlEncode(text)}";

        // note: needs API key
        // var url = $"https://translation.googleapis.com/language/translate/v2?source={sourceLang ?? "auto"}&target={targetLang}&format=text&q={HttpUtility.UrlEncode(text)}";

        var (http, req) = CreateRequest(url);

        var response = await http.SendAsync(req);
        if (!response.IsSuccessStatusCode) {
            var responseBody = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
        }
        var responseData = await response.Content.ReadFromJsonAsync<JsonArray>();
        var sb = new StringBuilder();
        foreach (var item in responseData ?? []) {
            if (item == null) continue;
            if (item.GetValueKind() == JsonValueKind.Array && item.AsArray().Count > 0 && item[0]?.GetValueKind() == JsonValueKind.String) {
                sb.Append(item[0]!.GetValue<string>()).Append(' ');
            }
        }

        return sb.ToString().Trim();
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
