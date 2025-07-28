using ReeLib;

namespace ContentPatcher;

public class CfilFileLoader : DefaultFileLoader<CfilFile>
{
    public CfilFileLoader() : base(KnownFileFormats.CollisionFilter) { }
}
