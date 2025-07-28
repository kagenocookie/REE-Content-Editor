using ReeLib;

namespace ContentPatcher;

public class UvarFileLoader : DefaultFileLoader<UVarFile>
{
    public UvarFileLoader() : base(KnownFileFormats.UserVariables) { }
}
