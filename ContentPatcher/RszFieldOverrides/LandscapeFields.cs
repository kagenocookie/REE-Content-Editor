using ReeLib;

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

    /// <summary>
    /// via.landscape.Ground
    /// </summary>
    [RszAccessor("via.landscape.Ground")]
    public static class Ground
    {
        public static readonly RszFieldAccessorFirst<string> GroundResource = First<string>(
            f => f.type is RszFieldType.String or RszFieldType.Resource
        ).Resource("via.landscape.GroundResourceHolder").Rename();

        public static readonly RszFieldAccessorFieldList<string> GroundMaterialResource = FromList<string>(
            f => f.Where(ff => ff.field.type is RszFieldType.String or RszFieldType.Resource).Skip(1).FirstOrDefault().index
        ).Resource("via.landscape.GroundMaterialListResourceHolder").Rename();
    }
}
