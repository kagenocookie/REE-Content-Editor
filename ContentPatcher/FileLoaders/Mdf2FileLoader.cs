using ReeLib;

namespace ContentPatcher;

public class Mdf2FileLoader : DefaultFileLoader<MdfFile>
{
    public Mdf2FileLoader() : base(KnownFileFormats.MeshMaterial) { SaveRawStream = true; }
}
