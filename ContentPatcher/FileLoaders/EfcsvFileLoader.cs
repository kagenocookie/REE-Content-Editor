using ReeLib;

namespace ContentPatcher;

public class EfcsvFileLoader : DefaultFileLoader<EfcsvFile>
{
    public EfcsvFileLoader() : base(KnownFileFormats.EffectCsv) { }
}
