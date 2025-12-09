using ReeLib;

namespace ContentPatcher;

public class UcurveListFileLoader : DefaultFileLoader<UCurveListFile>
{
    public UcurveListFileLoader() : base(KnownFileFormats.UserCurveList) { }
}
