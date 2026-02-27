using System.Text.Json.Nodes;
using ContentEditor;
using ReeLib;

namespace ContentPatcher;

public class MotpackFileLoader : DefaultFileLoader<MotpackFile>
{
    public MotpackFileLoader() : base(KnownFileFormats.MotionPack) { }

    public override bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var file = GetFile(handle);
        if (outputPath == handle.Filepath) {
            // force a clean save
            file.FileHandler.Stream.SetLength(0);
        }
        return base.Save(workspace, handle, outputPath);
    }
}
