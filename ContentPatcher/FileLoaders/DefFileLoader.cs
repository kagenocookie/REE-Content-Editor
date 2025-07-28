using ReeLib;

namespace ContentPatcher;

public class DefFileLoader : DefaultFileLoader<DefFile>
{
    public DefFileLoader() : base(KnownFileFormats.DynamicsDefinition) { }
}

