using ReeLib;

namespace ContentPatcher;

public class EfxFileLoader : DefaultFileLoader<EfxFile>
{
    public EfxFileLoader() : base(KnownFileFormats.Effect) { }
}
