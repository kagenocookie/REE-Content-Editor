using ContentPatcher;
using ReeLib;

namespace ContentPatcher;

public static partial class RszFieldCache
{
    /// <summary>
    /// via.render.Mesh
    /// </summary>
    [RszAccessor("via.render.Mesh")]
    public static class Mesh
    {
        /// <summary>
        /// Mesh resource path
        /// </summary>
        public static readonly RszFieldAccessorFirst<string> Resource =
            First<string>(f => f.type is RszFieldType.String or RszFieldType.Resource, "Mesh")
            .Resource("via.render.MeshResourceHolder");

        /// <summary>
        /// Material resource path
        /// </summary>
        public static readonly RszFieldAccessorFieldList<string> Material =
            FromList<string>(list => list.Where(fi => fi.field.type is RszFieldType.String or RszFieldType.Resource).Skip(1).First().index)
            .Resource("via.render.MeshMaterialResourceHolder");
    }
}
