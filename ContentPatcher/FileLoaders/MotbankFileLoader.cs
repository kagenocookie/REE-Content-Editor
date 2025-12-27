using ReeLib;

namespace ContentPatcher;

public class MotbankFileLoader : DefaultFileLoader<MotbankFile>
{
    public MotbankFileLoader() : base(KnownFileFormats.MotionBank) { }
}

public class McamBankFileLoader : DefaultFileLoader<McamBankFile>
{
    public McamBankFileLoader() : base(KnownFileFormats.MotionCameraBank) { }
}
