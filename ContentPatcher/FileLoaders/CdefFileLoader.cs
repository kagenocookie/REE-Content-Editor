using ReeLib;

namespace ContentPatcher;

public class CdefFileLoader : DefaultFileLoader<CdefFile>
{
    public CdefFileLoader() : base(KnownFileFormats.CollisionDefinition) { }
}
