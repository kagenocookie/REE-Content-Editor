using ReeLib;

namespace ContentPatcher;

public class Chain2FileLoader : DefaultFileLoader<Chain2File>
{
    public Chain2FileLoader() : base(KnownFileFormats.Chain2) { }
}
