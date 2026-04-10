using System.Diagnostics;
using System.Text.Json;
using ContentPatcher;
using ReeLib;

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
}

public class MmtrTemplateDB
{
    public string GameDataHash { get; set; } = "";
    public Dictionary<string, MmtrTemplate> Templates { get; set; } = new();
}

public class MmtrTemplate
{
    public List<MmtrTemplateParameter> Parameters { get; set; } = new();
    public List<string> TextureNames { get; set; } = new();
    public Dictionary<string, string> TextureDefaults { get; set; } = new();
}

public record MmtrTemplateParameter(string Name, int Components);

