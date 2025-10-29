using ReeLib;

namespace ContentPatcher;

public class CocoFileLoader : DefaultFileLoader<CocoFile>
{
    public CocoFileLoader() : base(KnownFileFormats.CompositeCollision) { }
}
