using ReeLib;

namespace ContentPatcher;

public class HfFileLoader : DefaultFileLoader<HFFile>
{
    public HfFileLoader() : base(KnownFileFormats.HeightField) { }
}
