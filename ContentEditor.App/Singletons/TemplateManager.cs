using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App;

public class TemplateManager : Singleton<TemplateManager>
{
    private readonly Dictionary<GameIdentifier, TemplateData> _templates = new();

    public static string GetUserTemplatesFolder(GameIdentifier game, bool create = false)
    {
        var path = Path.Combine(AppConfig.Instance.GetGameUserPath(game), "templates");
        if (!Directory.Exists(path)) {
            Directory.CreateDirectory(path);
        }
        return path;
    }

    public TemplateData ReloadTemplates(GameIdentifier game)
    {
        var data = new TemplateData();
        var coreTemplateDir = Path.Combine(AppConfig.Instance.ConfigBasePath, game.name, "templates");
        var userTemplateDir = GetUserTemplatesFolder(game);
        if (Directory.Exists(coreTemplateDir)) {
            var paths = Directory.EnumerateFiles(coreTemplateDir, "*.json");
            ReadAllTemplates(data.CoreTemplates, paths);
        }
        if (Directory.Exists(userTemplateDir)) {
            var paths = Directory.EnumerateFiles(userTemplateDir, "*.json");
            ReadAllTemplates(data.UserTemplates, paths);
        }

        foreach (var (t, v) in data.CoreTemplates) {
            data.Templates.Add(t, v.ToList());
        }

        foreach (var (t, v) in data.UserTemplates) {
            var listcopy = v.ToList();
            if (!data.Templates.TryAdd(t, listcopy)) {
                data.Templates[t].AddRange(v);
            }
        }

        _templates[game] = data;
        return data;
    }

    private static void ReadAllTemplates(Dictionary<string, List<TemplateItem>> targetDict, IEnumerable<string> templatePaths)
    {
        foreach (var path in templatePaths) {
            Dictionary<string, List<TemplateItem>> items;
            try {
                using var fs = File.OpenRead(path);
                items = JsonSerializer.Deserialize<Dictionary<string, List<TemplateItem>>>(fs, JsonConfig.jsonOptions)!;
                if (items == null) continue;
            } catch (Exception e) {
                Logger.Error($"Could not read template file {path}: {e.Message}");
                continue;
            }

            foreach (var (type, itemdata) in items) {
                if (!targetDict.TryGetValue(type, out var list)) {
                    targetDict[type] = list = new List<TemplateItem>();
                }

                foreach (var item in itemdata) {
                    if (list.Any(e => e.Name == item.Name)) {
                        Logger.Warn($"Found duplicate name template {item.Name} of type {type}");
                        continue;
                    }

                    item.StorageFilepath = path.NormalizeFilepath();
                    list.Add(item);
                }
            }
        }
    }

    public TemplateItem AddTemplate(GameIdentifier game, string objectType, string name, JsonObject template)
    {
        var item = new TemplateItem() { Name = name, Data = template };
        AddTemplate(game, objectType, item);
        return item;
    }

    public void AddTemplate(GameIdentifier game, string objectType, TemplateItem item)
    {
        var data = GetTemplateData(game);
        if (!data.Templates.TryGetValue(objectType, out var allList)) {
            data.Templates[objectType] = allList = new();
        }
        _cachedGuiLists.Clear();
        if (!data.UserTemplates.TryGetValue(objectType, out var list)) {
            data.UserTemplates[objectType] = list = new();
            list.Add(item);
            allList.Add(item);
            SaveUserTemplates(game, objectType);
            return;
        }
    }

    private void SaveUserTemplates(GameIdentifier game, string objectType)
    {
        var data = GetTemplateData(game);
        if (!data.UserTemplates.TryGetValue(objectType, out var list)) {
            return;
        }

        var saveFolder = GetUserTemplatesFolder(game, true);
        var savePath = Path.Combine(saveFolder, objectType + ".json").NormalizeFilepath();
        var targetFileTemplates = list.Where(e => e.StorageFilepath == null || e.StorageFilepath == savePath).ToList();
        Dictionary<string, List<TemplateItem>> storeData;

        if (File.Exists(savePath)) {
            // a preset file is allowed to contain multiple object types, so instead of re-checking every in memory
            // template, just re-read what's in there already and replace just our current object type's list
            try {
                using var prevFs = File.OpenRead(savePath);
                storeData = JsonSerializer.Deserialize<Dictionary<string, List<TemplateItem>>>(prevFs, JsonConfig.jsonOptions)!;
            } catch (Exception e) {
                Logger.Error($"Templates not saved. Could not confirm existing template data from file {savePath}: {e.Message}");
                return;
            }
        } else {
            storeData = new Dictionary<string, List<TemplateItem>>();
        }

        storeData[objectType] = targetFileTemplates;
        using var fs = File.Create(savePath);
        JsonSerializer.Serialize(fs, storeData, JsonConfig.jsonOptions);
        foreach (var a in list) {
            a.StorageFilepath ??= savePath;
        }
    }

    public bool TemplateExists(GameIdentifier game, string objectType, string name)
    {
        var data = GetTemplateData(game);
        if (!data.Templates.TryGetValue(objectType, out var list)) {
            return false;
        }

        return list.Any(e => e.Name == name);
    }

    public TemplateData GetTemplateData(GameIdentifier game)
    {
        if (_templates.TryGetValue(game, out var workspace)) {
            return workspace;
        }
        return ReloadTemplates(game);
    }

    public List<TemplateItem> GetTemplates(GameIdentifier game, string objectType, bool includeDefault, bool includeCustom)
    {
        var data = GetTemplateData(game);

        if (includeDefault && includeCustom) {
            return data.Templates.GetValueOrDefault(objectType) ?? [];
        } else if (includeDefault) {
            return data.CoreTemplates.GetValueOrDefault(objectType) ?? [];
        } else if (includeCustom) {
            return data.UserTemplates.GetValueOrDefault(objectType) ?? [];
        } else {
            return [];
        }
    }

    public (string[] labels, TemplateItem?[] options) GetTemplatesForGui(GameIdentifier game, string objectType, bool includeDefault, bool includeCustom)
    {
        var list = GetTemplates(game, objectType, includeDefault, includeCustom);
        if (!_cachedGuiLists.TryGetValue(list, out var cc)) {
            _cachedGuiLists[list] = cc = (
                list.Select(item => item.Name).Prepend("<blank>").ToArray(),
                list.Prepend(null).ToArray()
            );
        }
        return cc;
    }

    private Dictionary<List<TemplateItem>, (string[], TemplateItem?[])> _cachedGuiLists = new();
}

public class TemplateData
{
    public Dictionary<string, List<TemplateItem>> Templates = new();
    public Dictionary<string, List<TemplateItem>> CoreTemplates = new();
    public Dictionary<string, List<TemplateItem>> UserTemplates = new();
}

public class TemplateItem
{
    public string[]? Tags { get; set; }
    public string Name { get; set; } = "";
    public JsonObject Data { get; set; } = new JsonObject();

    [JsonIgnore]
    public string? StorageFilepath { get; set; }
}