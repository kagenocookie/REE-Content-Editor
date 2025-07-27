using System.Text.Json.Nodes;
using ReeLib;
using ReeLib.Msg;

namespace ContentPatcher;

public class TexFileLoader : DefaultFileLoader<TexFile>
{
    public TexFileLoader() : base(KnownFileFormats.Texture) { }
}
