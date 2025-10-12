using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.FileLoaders;

public class GrndFileLoader : DefaultFileLoader<GrndFile>
{
    public GrndFileLoader() : base(KnownFileFormats.Ground) { }
}
