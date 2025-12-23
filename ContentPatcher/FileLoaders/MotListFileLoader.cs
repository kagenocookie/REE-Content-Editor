using System.Text.Json.Nodes;
using ContentEditor;
using ReeLib;

namespace ContentPatcher;

public class MotListFileLoader : DefaultFileLoader<MotlistFile>
{
    public MotListFileLoader() : base(KnownFileFormats.MotionList) { }

    public override bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var file = GetFile(handle);
        var dangling = file.FindDanglingMotFiles();
        if (dangling.Length > 0) {
            Logger.Warn("Found mot files without motion IDs. These will get lost after reopening the file unless you give them a motion ID from the Motions list:\n" + string.Join("\n", dangling));
        }
        if (outputPath == handle.Filepath) {
            // force a clean save
            file.FileHandler.Stream.SetLength(0);
        }
        return base.Save(workspace, handle, outputPath);
    }
}

public class MotListPatcher : IResourceFilePatcher
{
    private MotlistFile motlist = null!;
    private FileHandle fileHandle = null!;
    private ContentWorkspace workspace = null!;

    public IResourceFile LoadBase(ContentWorkspace workspace, FileHandle file)
    {
        motlist = file.GetFile<MotlistFile>();
        fileHandle = file;
        this.workspace = workspace;
        return file.Resource;
    }

    public void ApplyDiff(JsonNode diff) => ApplyDiff(fileHandle, diff);
    public void ApplyDiff(FileHandle targetFile, JsonNode diff)
    {
        var list = targetFile.GetFile<MotlistFile>();
        var obj = (JsonObject)diff;
        foreach (var (name, data) in obj) {
            if (data != null && MotionDataResource.TryDeserialize(data, out var motData, out var error)) {
                var prev = list.Find(name);
                var newMot = motData.ToMotFile();
                if (prev == null || newMot == null) {
                    Logger.Error($"Failed to evaluate motlist diff for motion {name} in list {list.Header.MotListName}");
                    continue;
                }

                list.ReplaceMotFile(prev, newMot);
            }
        }
    }

    public JsonNode? FindDiff(FileHandle file)
    {
        // TODO need some sort of explicit opt-in for which mots should get included in the diff
        // comparing purely binary data is gonna fail for arbitrary reasons like tiny offset differences or floats on different CPUs
        return null;
    }
}