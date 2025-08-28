using ReeLib;

namespace ContentPatcher;

public class ClipFileLoader : DefaultFileLoader<ClipFile>
{
    public ClipFileLoader() : base(KnownFileFormats.Clip) { SaveRawStream = true; }
}

