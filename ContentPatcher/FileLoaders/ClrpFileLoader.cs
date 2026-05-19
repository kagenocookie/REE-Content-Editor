using ReeLib;

namespace ContentPatcher;

public class ClrpFileLoader : DefaultFileLoader<ClrpFile>
{
    public ClrpFileLoader() : base(KnownFileFormats.ClothResetPose) { }
}
