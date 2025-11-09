using ReeLib;

namespace ContentPatcher;

public class UcurveFileLoader : DefaultFileLoader<UcurveFile>
{
    public UcurveFileLoader() : base(KnownFileFormats.UserCurve) { }
}

