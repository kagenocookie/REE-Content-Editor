using ReeLib;

namespace ContentPatcher;

public class McolFileLoader : DefaultFileLoader<McolFile>
{
    public McolFileLoader() : base(KnownFileFormats.CollisionMesh) { }
}
