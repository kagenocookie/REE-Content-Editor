using ReeLib;

namespace ContentPatcher;

public class WelFileLoader : DefaultFileLoader<WelFile>
{
    public WelFileLoader() : base(KnownFileFormats.EventList) { }
}
