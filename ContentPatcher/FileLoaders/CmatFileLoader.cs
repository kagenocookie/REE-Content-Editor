using ReeLib;

namespace ContentPatcher;

public class CmatFileLoader : DefaultFileLoader<CmatFile>
{
    public CmatFileLoader() : base(KnownFileFormats.CollisionMaterial) { }
}
