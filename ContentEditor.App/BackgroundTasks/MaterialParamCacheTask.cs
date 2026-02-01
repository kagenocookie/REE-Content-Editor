using System.Diagnostics;
using System.Text.Json;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.BackgroundTasks;

public class MaterialParamCacheTask : IBackgroundTask
{
    public static string GetCachePath(GameIdentifier game) => Path.Combine(AppConfig.Instance.CacheFilepath.Get() ?? Path.Combine(AppConfig.AppDataPath, "cache"), game.name, "materials.json");

    public string Status { get; private set; }
    public bool IsCancelled { get; set; }
    public Workspace Workspace { get; }

    private int filesProcessed = 0;
    private int totalFiles = 0;

    public float Progress => totalFiles <= 0 ? -1 : (float)filesProcessed / totalFiles;

    public MaterialParamCacheTask(Workspace workspace)
    {
        Status = "Processing";
        Workspace = workspace;
    }

    public override string ToString() => $"Caching material parameter data";

    public unsafe void Execute(CancellationToken token = default)
    {
        totalFiles = Workspace.ListFile?.FilterAllFiles(".*\\.mdf2\\..*").Length ?? 1;
        filesProcessed = 0;
        var dict = new Dictionary<string, MmtrTemplate>();
        foreach (var (path, stream) in Workspace.GetFilesWithExtension("mdf2", token)) {
            filesProcessed++;
            var mdf = new MdfFile(new FileHandler(stream, path));
            if (!mdf.Read()) continue;
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

        var outputFilepath = GetCachePath(Workspace.Config.Game);
        var db = new MmtrTemplateDB() {
            Templates = dict,
            GameDataHash = AppUtils.GetGameVersionHash(Workspace.Config)
        };
        var outputDir = Path.GetDirectoryName(outputFilepath);
        if (string.IsNullOrEmpty(outputDir)) {
            Logger.Error("Failed to determine cache output path");
            return;
        }

        try {
            Directory.CreateDirectory(outputDir);
            using var fs = File.Create(outputFilepath);
            JsonSerializer.Serialize(fs, db);
            Logger.Info("Stored material parameter cache in " + outputFilepath);
        } catch (Exception e) {
            Logger.Error("Failed to save material parameter cache in path " + outputFilepath + ":\n" + e.Message);
        }
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

