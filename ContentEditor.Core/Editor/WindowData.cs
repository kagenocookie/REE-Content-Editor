using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContentEditor.Core;
using ContentEditor.Editor;

namespace ContentEditor;

public class WindowData
{
    public WindowData()
    {
        this.ParentWindow = null!;
    }

    public int ID { get; set; }
    public string Name { get; set; } = string.Empty;
    public Vector2 Size { get; set; }
    public Vector2 Position { get; set; }
    public bool IsPersistent { get; set; }
    public IRectWindow ParentWindow { get; set; }
    public Dictionary<string, JsonElement>? PersistentData { get; set; }
    public Dictionary<string, WindowData>? Subwindows { get; set; }

    [JsonIgnore]
    public UIContext Context { get; set; } = null!;
    [JsonIgnore]
    public IWindowHandler? Handler { get; set; }

    public T GetOrAddPersistentData<T>(string key, T defaultValue) where T : struct
    {
        if (PersistentData == null) PersistentData = new();
        if (!PersistentData.TryGetValue(key, out var value) || value.ValueKind == JsonValueKind.Null) {
            PersistentData[key] = value = JsonSerializer.SerializeToElement(defaultValue, JsonConfig.jsonOptions);
        }

        return value.Deserialize<T?>(JsonConfig.jsonOptions) ?? defaultValue;
    }

    public T GetOrAddPersistentClass<T>(string key) where T : new()
    {
        if (PersistentData == null) PersistentData = new();
        if (!PersistentData.TryGetValue(key, out var value) || value.ValueKind == JsonValueKind.Null) {
            PersistentData[key] = value = JsonSerializer.SerializeToElement<T>(new (), JsonConfig.jsonOptions);
        }

        return value.Deserialize<T?>(JsonConfig.jsonOptions) ?? new ();
    }

    public T GetPersistentData<T>(string key)
    {
        if (PersistentData == null) PersistentData = new();
        if (!PersistentData.TryGetValue(key, out var value) || value.ValueKind == JsonValueKind.Null) {
            return default!;
        }

        return value.Deserialize<T?>(JsonConfig.jsonOptions) ?? default!;
    }

    public WindowData GetOrAddSubwindow(string key)
    {
        if (Subwindows == null) Subwindows = new();
        if (Context == null) throw new Exception();
        if (!Subwindows.TryGetValue(key, out var data)) {
            Subwindows[key] = data = new WindowData() { ParentWindow = ParentWindow };
            data.Context = Context.GetChild(key) ?? Context.AddChild(key, data);
        }

        return data;
    }

    public void SetPersistentData<T>(string key, T value)
    {
        if (PersistentData == null) PersistentData = new();

        PersistentData[key] = JsonSerializer.SerializeToElement(value, JsonConfig.jsonOptions);
    }

    public override string ToString() => $"{Name}##{ID}";
}
