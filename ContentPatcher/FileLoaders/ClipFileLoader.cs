using ReeLib;

namespace ContentPatcher;

public class ClipFileLoader : DefaultFileMultiLoader<ClipFile>
{
    public ClipFileLoader() : base(KnownFileFormats.Timeline, KnownFileFormats.Clip, KnownFileFormats.UserCurve) { }
}
