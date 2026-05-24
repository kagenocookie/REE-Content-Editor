using ReeLib;

namespace ContentPatcher;

public class MotTreeFileLoader : DefaultFileLoader<MotTreeFile>
{
    public MotTreeFileLoader() : base(KnownFileFormats.MotionTree) { }
}
