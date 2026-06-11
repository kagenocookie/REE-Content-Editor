using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContentEditor.App;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;

namespace ContentEditor.BackgroundTasks;

public class MaterialParamCacheTask(Workspace workspace) : FileCacheTaskBase(workspace)
{
    protected override string GetCacheFilePath(GameIdentifier game) => GetCachePath(game);
    public static string GetCachePath(GameIdentifier game) => Path.Combine(GetBaseCacheDir(game.name), "materials.json");

    public override string ToString() => $"Caching material parameter data";

    protected override string FilterPattern => ".*\\.mdf2\\..*";

    protected override string FileExtension => "mdf2";

    private Dictionary<string, MmtrTemplate> dict = new();

    protected override void HandleFile(string path, Stream stream)
    {
        var mdf = new MdfFile(new FileHandler(stream, path));
        if (!mdf.Read()) return;
        foreach (var mat in mdf.Materials) {
            if (string.IsNullOrEmpty(mat.Header.mmtrPath)) continue;

            if (!dict.TryGetValue(mat.Header.mmtrPath, out var template)) {
                dict[mat.Header.mmtrPath] = template = new MmtrTemplate();
                foreach (var p in mat.Parameters) {
                    template.Parameters.Add(new MmtrTemplateParameter(p.paramName, p.componentCount));
                }
                foreach (var t in mat.Textures) {
                    template.TextureNames.Add(t.texType);
                }
            } else {
                Debug.Assert(template.Parameters.Select(p => p.Name).Intersect(mat.Parameters.Select(p => p.paramName)).Count() == template.Parameters.Count);
                Debug.Assert(template.TextureNames.Intersect(mat.Textures.Select(p => p.texType)).Count() == template.TextureNames.Count);
            }

            foreach (var t in mat.Textures) {
                if (t.texPath?.Contains("/null", StringComparison.InvariantCultureIgnoreCase) == true) {
                    template.TextureDefaults.TryAdd(t.texType, t.texPath);
                }
            }
        }
    }

    protected override void Serialize(string outputFilepath)
    {
        var db = new MmtrTemplateDB() {
            Templates = dict,
            GameDataHash = AppUtils.GetGameVersionHash(Workspace.Config)
        };
        using var fs = File.Create(outputFilepath);
        JsonSerializer.Serialize(fs, db);
        Logger.Info("Stored material parameter cache in " + outputFilepath);
    }

    public static MmtrTemplateDB? TryDeserialize(ContentWorkspace workspace, ref bool hasAlreadyRequestedFile)
    {
        var cachePath = GetCachePath(workspace.Game);
        MmtrTemplateDB? result = null;
        if (!hasAlreadyRequestedFile) {
            hasAlreadyRequestedFile = true;
            if (!System.IO.File.Exists(cachePath)) {
                // OK
            } else if (!cachePath.TryDeserializeJsonFile<MmtrTemplateDB>(out var db, out var error)) {
                Logger.Warn("Could not load previous mmtr parameter cache from path " + cachePath + ":\n" + error);
            } else if (db.GameDataHash == workspace.VersionHash) {
                result = db;
            }

            if (result == null) {
                if (!MainLoop.Instance.BackgroundTasks.HasPendingTask<MaterialParamCacheTask>()) {
                    MainLoop.Instance.BackgroundTasks.Queue(new MaterialParamCacheTask(workspace.Env));
                }
            } else {
                // have the paths in lowercase to ensure we can match them up case insensitively
                foreach (var key in result.Templates.Keys.ToArray()) {
                    if (result.Templates.Remove(key, out var data)) {
                        result.Templates[key.ToLowerInvariant()] = data;
                    }
                }
            }
        } else {
            if (!MainLoop.Instance.BackgroundTasks.HasPendingTask<MaterialParamCacheTask>() &&
                System.IO.File.Exists(cachePath) &&
                cachePath.TryDeserializeJsonFile<MmtrTemplateDB>(out var db, out var error)) {
                result = db;
            }
        }
        return result;
    }
}

public class MmtrTemplateDB
{
    public string GameDataHash { get; set; } = "";
    public Dictionary<string, MmtrTemplate> Templates { get; set; } = new();
    private Dictionary<uint, string>? _asciiHashes;
    [JsonIgnore]
    public Dictionary<uint, string> AsciiHashes => _asciiHashes ??= CreateAsciiHashMap();

    private Dictionary<uint, string> CreateAsciiHashMap()
    {
        var dict = new Dictionary<uint, string>();
        foreach (var t in Templates) {
            foreach (var pp in t.Value.Parameters) {
                dict[MurMur3HashUtils.GetAsciiHash(pp.Name)] = pp.Name;
            }
            foreach (var name in t.Value.TextureNames) {
                dict[MurMur3HashUtils.GetAsciiHash(name)] = name;
            }
        }
        return dict;
    }
}

public class MmtrTemplate
{
    public List<MmtrTemplateParameter> Parameters { get; set; } = new();
    public List<string> TextureNames { get; set; } = new();
    public Dictionary<string, string> TextureDefaults { get; set; } = new();
}

public record MmtrTemplateParameter(string Name, int Components);

