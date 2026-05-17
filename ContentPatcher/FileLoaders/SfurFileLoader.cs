using ReeLib;

namespace ContentPatcher;

public class SfurFileLoader : DefaultFileLoader<SfurFile>
{
    public SfurFileLoader() : base(KnownFileFormats.ShellFur) { }
}
