using ReeLib;

namespace ContentPatcher;

public class MotFileLoader : DefaultFileLoader<MotFile>
{
    public MotFileLoader() : base(KnownFileFormats.Motion) { SaveRawStream = true; }
}
