using ReeLib;

namespace ContentPatcher;

public class JmapFileLoader : DefaultFileLoader<JmapFile>
{
    public JmapFileLoader() : base(KnownFileFormats.JointMap) { }
}
