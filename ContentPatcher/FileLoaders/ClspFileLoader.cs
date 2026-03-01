using ReeLib;

namespace ContentPatcher;

public class ClspFileLoader : DefaultFileLoader<ClspFile>
{
    public ClspFileLoader() : base(KnownFileFormats.CollisionShapePreset) { }
}
