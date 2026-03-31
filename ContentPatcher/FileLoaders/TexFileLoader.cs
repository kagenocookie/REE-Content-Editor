using ReeLib;

namespace ContentPatcher;

public class TexFileLoader : DefaultFileLoader<TexFile>
{
    public TexFileLoader() : base(KnownFileFormats.Texture) { SaveRawStream = true; }
}
