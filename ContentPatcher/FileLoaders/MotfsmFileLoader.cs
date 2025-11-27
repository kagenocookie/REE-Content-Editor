using ReeLib;

namespace ContentPatcher;

public class Motfsm2FileLoader : DefaultFileLoader<Motfsm2File>
{
    public Motfsm2FileLoader() : base(KnownFileFormats.MotionFsm2) { }
}
