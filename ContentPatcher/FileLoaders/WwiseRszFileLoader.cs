using ReeLib;

namespace ContentPatcher;

public class WwiseRszFileLoader : DefaultFileLoader<RSZFile>
{
    public WwiseRszFileLoader() : base(KnownFileFormats.WwiseAudioRSZ) { }

    public override bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var file = handle.GetFile<RSZFile>();
        file.RebuildInstanceInfo();
        return base.Save(workspace, handle, outputPath);
    }
}

