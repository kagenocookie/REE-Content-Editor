namespace ContentEditor.Core;

using System.Text.Json.Serialization;

public class InjectionCache
{
    [JsonPropertyName("entries")]
    public Dictionary<string, Dictionary<long, InjectionCacheEntry>>? Entries { get; set; }
}

public class InjectionCacheEntry
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("time")]
    public long Time { get; set; }

    public override string ToString() => Status.ToString();
}