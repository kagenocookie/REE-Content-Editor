using ReeLib;

namespace ContentPatcher;

public class GcfFileLoader : DefaultFileLoader<GcfFile>
{
    public GcfFileLoader() : base(KnownFileFormats.GUIConfig) { }
}
