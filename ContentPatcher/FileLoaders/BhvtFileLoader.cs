using System.Text.Json.Nodes;
using ReeLib;
using ReeLib.Msg;

namespace ContentPatcher;

public class BhvtFileLoader : DefaultFileMultiLoader<BhvtFile>
{
    public BhvtFileLoader() : base(KnownFileFormats.BehaviorTree, KnownFileFormats.Fsm2) { }
}
