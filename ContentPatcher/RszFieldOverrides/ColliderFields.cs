using ContentPatcher;
using ReeLib;
using ReeLib.via;

namespace ContentPatcher;

/// <summary>
/// Container for game-agonstic RSZ field lookup conditions.
/// </summary>
public static partial class RszFieldCache
{
    /// <summary>
    /// via.physics.Colliders
    /// </summary>
    [RszAccessor("via.physics.Colliders")]
    public static class Colliders
    {
        /// <summary>
        /// via.physics.Collider[]
        /// </summary>
        public static readonly RszFieldAccessorFirst<List<object>> ColliderList = First<List<object>>(
            fi => fi.array && fi.type is not RszFieldType.String and not RszFieldType.Resource,
            "Colliders"
        ).Object("via.physics.Collider");
    }

    /// <summary>
    /// via.physics.Collider
    /// </summary>
    [RszAccessor("via.physics.Collider")]
    public static class Collider
    {
        /// <summary>
        /// via.physics.Shape
        /// </summary>
        public static readonly RszFieldAccessorFirstFallbacks<RszInstance> Shape = First<RszInstance>([
            (field) => field.original_type == "via.physics.Shape",
            (field) => field.type == RszFieldType.Object,
            (field) => field.size == 4
        ]).Object("via.physics.Shape");

        public static readonly RszFieldAccessorFirstFallbacks<RszInstance> FilterInfo = First<RszInstance>([
            (field) => field.original_type == "via.physics.FilterInfo",
            (field) => field.original_type != "via.physics.Shape" && field.type == RszFieldType.Object
        ]).Object("via.physics.FilterInfo").Rename();

        public static readonly RszFieldAccessorFirstFallbacks<string> CollisionFilter = First<string>([
            (field) => field.original_type == "via.physics.CollisionFilterResourceHolder",
            (field) => field.type == RszFieldType.Resource,
            (field) => field.type == RszFieldType.String
        ]).Resource("via.physics.CollisionFilterResourceHolder").Rename();

        public static readonly RszFieldAccessorFirstFallbacks<string> CollisionMaterial = First<string>([
            (field) => field.original_type == "via.physics.CollisionMaterialResourceHolder",
            (field) => field.name != nameof(CollisionFilter) && field.type == RszFieldType.Resource,
            (field) => field.name != nameof(CollisionFilter) && field.type == RszFieldType.String
        ]).Resource("via.physics.CollisionMaterialResourceHolder").Rename();
    }

    /// <summary>
    /// via.physics.SphereShape, via.physics.ContinuousSphereShape
    /// </summary>
    [RszAccessor("via.physics.SphereShape")]
    [RszAccessor("via.physics.ContinuousSphereShape")]
    public static class SphereShape
    {
        /// <summary>
        /// via.physics.Sphere
        /// </summary>
        public static readonly RszFieldAccessorFirst<Sphere> Sphere =
            First<Sphere>((field) => field.type is RszFieldType.Sphere or RszFieldType.Vec4)
            .Type(RszFieldType.Sphere);
    }

    /// <summary>
    /// via.physics.CapsuleShape, via.physics.ContinuousCapsuleShape
    /// </summary>
    [RszAccessor("via.physics.CapsuleShape")]
    [RszAccessor("via.physics.ContinuousCapsuleShape")]
    public static class CapsuleShape
    {
        /// <summary>
        /// via.physics.Capsule
        /// </summary>
        public static readonly RszFieldAccessorFirstFallbacks<Capsule> Capsule = First<Capsule>([
            (field) => field.type is RszFieldType.Capsule,
            (field) => field.type is RszFieldType.Data && field.size == 48
        ]).Type(RszFieldType.Capsule);
    }

    /// <summary>
    /// via.physics.CylinderShape
    /// </summary>
    [RszAccessor("via.physics.CylinderShape")]
    public static class CylinderShape
    {
        /// <summary>
        /// via.physics.Capsule
        /// </summary>
        public static readonly RszFieldAccessorFirstFallbacks<Cylinder> Cylinder = First<Cylinder>([
            (field) => field.type is RszFieldType.Cylinder,
            (field) => field.type is RszFieldType.Data && field.size == 48
        ]).Type(RszFieldType.Cylinder);
    }

    /// <summary>
    /// via.physics.BoxShape
    /// </summary>
    [RszAccessor("via.physics.BoxShape")]
    public static class BoxShape
    {
        public static readonly RszFieldAccessorFirst<OBB> Box = First<OBB>(
            (field) => field.type is RszFieldType.OBB
        ).Type(RszFieldType.OBB);
    }

    /// <summary>
    /// via.physics.AabbShape
    /// </summary>
    [RszAccessor("via.physics.AabbShape")]
    public static class AabbShape
    {
        // note: need to go by last one here because the newer games have an AABB field[0] in every shape type

        public static readonly RszFieldAccessorLastFallbacks<AABB> Aabb = Last<AABB>([
            (field) => field.type is RszFieldType.AABB,
            (field) => field.size == 32 && !field.array
        ]).Type(RszFieldType.AABB);
    }

    /// <summary>
    /// via.physics.MeshShape
    /// </summary>
    [RszAccessor("via.physics.MeshShape")]
    public static class MeshShape
    {
        public static readonly RszFieldAccessorFirst<string> Mesh =
            First<string>((field) => field.type is RszFieldType.String or RszFieldType.Resource)
            .Resource("via.physics.CollisionMeshResourceHolder");

        public static readonly RszFieldAccessorFirst<mat4> TransformMatrix =
            First<mat4>((field) => field.type is RszFieldType.Mat4)
            .Type(RszFieldType.Mat4);
    }

    /// <summary>
    /// via.physics.StaticCompoundShape
    /// </summary>
    [RszAccessor("via.physics.StaticCompoundShape")]
    public static class StaticCompoundShape
    {
        public static readonly RszFieldAccessorFirst<List<object>> Shapes =
            First<List<object>>((field) => field.array)
            .Object("via.physics.StaticCompoundShape.Instance");

        /// <summary>
        /// via.physics.StaticCompoundShape.Instance
        /// </summary>
        [RszAccessor("via.physics.StaticCompoundShape.Instance")]
        public static class Instance
        {
            public static readonly RszFieldAccessorFirst<RszInstance> Shape =
                First<RszInstance>((field) => field.size == 4)
                .Object("via.physics.Shape");
        }
    }

    /// <summary>
    /// via.physics.HeightFieldShape
    /// </summary>
    [RszAccessor("via.physics.HeightFieldShape")]
    public static class HeightFieldShape
    {
        public static readonly RszFieldAccessorFirst<string> HeightField =
            First<string>((field) => field.type is RszFieldType.Resource or RszFieldType.String)
            .Resource("via.physics.CollisionHeightFieldResourceHolder");
    }
}
