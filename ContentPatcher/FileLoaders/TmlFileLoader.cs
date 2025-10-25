using ReeLib;

namespace ContentPatcher;

public class TmlFileLoader : DefaultFileLoader<TmlFile>
{
    public TmlFileLoader() : base(KnownFileFormats.Timeline) { }
}

