using ReeLib;

namespace ContentPatcher;

public class CcbkFileLoader : DefaultFileLoader<CcbkFile>
{
    public CcbkFileLoader() : base(KnownFileFormats.CharacterColliderBank) { }
}
