using ReeLib;

namespace ContentPatcher;

public class EemFileLoader : DefaultFileLoader<EemFile>
{
    public EemFileLoader() : base(KnownFileFormats.EffectEmitMask) { }
}
