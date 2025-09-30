using ReeLib;

namespace ContentPatcher;

public class MotListFileLoader : DefaultFileLoader<MotlistFile>
{
    public MotListFileLoader() : base(KnownFileFormats.MotionList) { }

    public override bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        // force a clean save
        GetFile(handle).FileHandler.Stream.SetLength(0);
        return base.Save(workspace, handle, outputPath);
    }
}

