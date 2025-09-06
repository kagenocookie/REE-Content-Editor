using System.Numerics;
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

    /// <summary>
    /// via.render.CompositeMesh
    /// </summary>
    [RszAccessor("via.render.CompositeMesh")]
    public static class CompositeMesh
    {
        /// <summary>
        /// Mesh resource path
        /// </summary>
        public static readonly RszFieldAccessorFirst<List<object>> InstanceGroups =
            First<List<object>>(f => f.array && f.type is RszFieldType.Object, "InstanceGroups")
            .Object("via.render.CompositeMeshInstanceGroup");

        [RszAccessor("via.render.CompositeMeshInstanceGroup")]
        public static class InstanceGroup
        {
            /// <summary>
            /// Mesh resource path
            /// </summary>
            public static readonly RszFieldAccessorFirst<string> Mesh =
                First<string>(f => f.type is RszFieldType.String or RszFieldType.Resource)
                .Resource("via.render.MeshResourceHolder");

            /// <summary>
            /// Material resource path
            /// </summary>
            public static readonly RszFieldAccessorFieldList<string> Material =
                FromList<string>(list => list.Where(fi => fi.field.type is RszFieldType.String or RszFieldType.Resource).Skip(1).First().index)
                .Resource("via.render.MeshMaterialResourceHolder");

            /// <summary>
            /// Instance transforms
            /// </summary>
            public static readonly RszFieldAccessorFirst<List<object>> Transforms =
                First<List<object>>(fi => fi.array && fi.type == RszFieldType.Object)
                .Object("via.render.CompositeMeshTransformController");
        }

        [RszAccessor("via.render.CompositeMeshTransformController")]
        public static class TransformController
        {
            public static readonly RszFieldAccessorFixedIndex<bool> Enabled = Index<bool>(0).Type(RszFieldType.Bool);
            public static readonly RszFieldAccessorFieldList<Vector3> LocalPosition = FromList<Vector3>(list => list.First(f => f.field.size > 4).index).Type(RszFieldType.Vec3);
            public static readonly RszFieldAccessorFixedFunc<Quaternion> LocalRotation = Func<Quaternion>(i => LocalPosition.GetIndex(i) + 1).Type(RszFieldType.Quaternion);
            public static readonly RszFieldAccessorFixedFunc<Vector3> LocalScale = Func<Vector3>(i => LocalPosition.GetIndex(i) + 2).Type(RszFieldType.Vec3);
        }
    }
}
