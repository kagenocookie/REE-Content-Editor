using ReeLib;

namespace ContentPatcher;

public class RcolFileLoader : DefaultFileLoader<RcolFile>
{
    public RcolFileLoader() : base(KnownFileFormats.RequestSetCollider) { }
}
