using ReeLib;

namespace ContentPatcher;

public class ChfFileLoader : DefaultFileLoader<CHFFile>
{
    public ChfFileLoader() : base(KnownFileFormats.CollisionHeightField) { }
}
