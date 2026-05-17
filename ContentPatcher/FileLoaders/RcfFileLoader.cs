using ReeLib;

namespace ContentPatcher;

public class RcfFileLoader : DefaultFileLoader<RcfFile>
{
    public RcfFileLoader() : base(KnownFileFormats.RebeConfig) { }
}
