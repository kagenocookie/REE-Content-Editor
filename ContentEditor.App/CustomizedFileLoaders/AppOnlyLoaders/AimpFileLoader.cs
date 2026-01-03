using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.FileLoaders;

public class AimpFileLoader : DefaultFileLoader<AimpFile>
{
    public AimpFileLoader() : base(KnownFileFormats.AIMap) { ClearStreamOnSave = true; }
}
