using ReeLib;

namespace ContentPatcher;

public class FolFileLoader : DefaultFileLoader<FolFile>
{
    public FolFileLoader() : base(KnownFileFormats.Foliage) { }
}
