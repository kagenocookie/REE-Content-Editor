using ReeLib;

namespace ContentPatcher;

public class ChainFileLoader : DefaultFileLoader<ChainFile>
{
    public ChainFileLoader() : base(KnownFileFormats.Chain) { }
}
