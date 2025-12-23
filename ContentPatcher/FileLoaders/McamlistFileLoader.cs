using System.Text.Json.Nodes;
using ContentEditor;
using ReeLib;

namespace ContentPatcher;

public class McamlistFileLoader : DefaultFileLoader<McamlistFile>
{
    public McamlistFileLoader() : base(KnownFileFormats.MotionCameraList) { }

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

public class MotcamFileLoader : DefaultFileLoader<MotcamFile>
{
    public MotcamFileLoader() : base(KnownFileFormats.MotionCamera) { }
}
