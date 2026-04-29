using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.FileLoaders;

public class GpucFileLoader : DefaultFileLoader<GpucFile>
{
    public GpucFileLoader() : base(KnownFileFormats.GpuCloth) { }
}
