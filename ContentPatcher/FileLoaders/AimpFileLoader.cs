using ReeLib;

namespace ContentPatcher;

public class AimpFileLoader : DefaultFileLoader<AimpFile>
{
    public AimpFileLoader() : base(KnownFileFormats.AIMap) { }
}
