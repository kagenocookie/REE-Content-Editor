using ReeLib;

namespace ContentPatcher;

public class Chain2FileLoader : DefaultFileLoader<Chain2File>
{
    public Chain2FileLoader() : base(KnownFileFormats.Chain2) { }

    public override IResourceFile? CreateNewFile(ContentWorkspace workspace, FileHandle handle)
    {
        var chain = new Chain2File(new FileHandler(handle.Stream, handle.Filepath));
        chain.Header.version = handle.Format.version;
        chain.Settings.Add(new ReeLib.Chain2.Chain2Setting());
        chain.Write();
        return new BaseFileResource<Chain2File>(chain);
    }
}
