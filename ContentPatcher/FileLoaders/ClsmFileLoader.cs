using ReeLib;

namespace ContentPatcher;

public class ClsmFileLoader : DefaultFileLoader<ClsmFile>
{
    public ClsmFileLoader() : base(KnownFileFormats.CollisionSkinningMesh) { }
}
