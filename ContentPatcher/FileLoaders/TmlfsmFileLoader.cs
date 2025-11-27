using ReeLib;

namespace ContentPatcher;

public class Tmlfsm2FileLoader : DefaultFileLoader<Tmlfsm2File>
{
    public Tmlfsm2FileLoader() : base(KnownFileFormats.TimelineFsm2) { }
}
