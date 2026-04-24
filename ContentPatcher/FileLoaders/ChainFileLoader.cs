using ReeLib;

namespace ContentPatcher;

public class ChainFileLoader : DefaultFileLoader<ChainFile>
{
    public ChainFileLoader() : base(KnownFileFormats.Chain) { }

    public override IResourceFile? CreateNewFile(ContentWorkspace workspace, FileHandle handle)
    {
        var chain = new ChainFile(new FileHandler(handle.Stream, handle.Filepath));
        chain.Header.version = handle.Format.version;
        chain.Settings.Add(new ReeLib.Chain.ChainSetting());
        chain.Write();
        return new BaseFileResource<ChainFile>(chain);
    }
}
