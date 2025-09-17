using ReeLib;

namespace ContentPatcher;

public class MotListFileLoader : DefaultFileLoader<MotlistFile>
{
    public MotListFileLoader() : base(KnownFileFormats.MotionList) { }
}

