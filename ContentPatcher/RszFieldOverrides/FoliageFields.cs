using System.Numerics;
using ContentPatcher;
using ReeLib;
using ReeLib.via;

namespace ContentPatcher;

public static partial class RszFieldCache
{
    /// <summary>
    /// via.landscape.Foliage
    /// </summary>
    [RszAccessor("via.landscape.Foliage")]
    public static class Foliage
    {
        public static readonly RszFieldAccessorFirst<string> FoliageResource = First<string>(
            f => f.type is RszFieldType.String or RszFieldType.Resource
        ).Resource("via.landscape.FoliageResourceHolder");
    }
}
