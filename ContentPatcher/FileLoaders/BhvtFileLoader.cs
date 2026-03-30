using ReeLib;

namespace ContentPatcher;

public class BhvtFileLoader : DefaultFileMultiLoader<BhvtFile>
{
    public BhvtFileLoader() : base(KnownFileFormats.BehaviorTree, KnownFileFormats.Fsm2) { }
}
