using ReeLib;

namespace ContentPatcher;

public class MotbankFileLoader : DefaultFileLoader<MotbankFile>
{
    public MotbankFileLoader() : base(KnownFileFormats.MotionBank) { }
}
