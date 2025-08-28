using ReeLib;
using ReeLib.via;

namespace ContentEditor.App;

/// <summary>
/// Container for game-agonstic RSZ field lookup conditions.
/// </summary>
public static partial class RszFieldCache
{
    /// <summary>
    /// via.physics.Colliders
    /// </summary>
    public static class Colliders
    {
        /// <summary>
        /// via.physics.Collider[]
        /// </summary>
        public static readonly RszFieldAccessorFirst<List<object>> ColliderList = new RszFieldAccessorFirst<List<object>>(
            fi => fi.array && fi.type is not RszFieldType.String and not RszFieldType.Resource,
            "Colliders"
        ).Object("via.physics.Collider[]");
    }

    /// <summary>
    /// via.physics.Collider
    /// </summary>
    public static class Collider
    {
        /// <summary>
        /// via.physics.Shape
        /// </summary>
        public static readonly RszFieldAccessorFirstFallbacks<RszInstance> Shape = new RszFieldAccessorFirstFallbacks<RszInstance>([
            (field) => field.original_type == "via.physics.Shape",
            (field) => field.type == RszFieldType.Object
        ]).Object("via.physics.Shape");
    }

    /// <summary>
    /// via.physics.SphereShape, via.physics.ContinuousSphereShape
    /// </summary>
    public static class SphereShape
    {
        /// <summary>
        /// via.physics.Sphere
        /// </summary>
        public static readonly RszFieldAccessorFirst<Sphere> Sphere = new RszFieldAccessorFirst<Sphere>(
            (field) => field.type is RszFieldType.Sphere or RszFieldType.Vec4
        ).Type(RszFieldType.Sphere);
    }

    /// <summary>
    /// via.physics.CapsuleShape, via.physics.ContinuousCapsuleShape
    /// </summary>
    public static class CapsuleShape
    {
        /// <summary>
        /// via.physics.Capsule
        /// </summary>
        public static readonly RszFieldAccessorFirstFallbacks<Capsule> Capsule = new RszFieldAccessorFirstFallbacks<Capsule>([
            (field) => field.type is RszFieldType.Capsule,
            (field) => field.type is RszFieldType.Data && field.size == 48
        ]).Type(RszFieldType.Capsule);
    }

    /// <summary>
    /// via.physics.CylinderShape
    /// </summary>
    public static class CylinderShape
    {
        /// <summary>
        /// via.physics.Capsule
        /// </summary>
        public static readonly RszFieldAccessorFirstFallbacks<Cylinder> Cylinder = new RszFieldAccessorFirstFallbacks<Cylinder>([
            (field) => field.type is RszFieldType.Cylinder,
            (field) => field.type is RszFieldType.Data && field.size == 48
        ]).Type(RszFieldType.Cylinder);
    }

    /// <summary>
    /// via.physics.BoxShape
    /// </summary>
    public static class BoxShape
    {
        public static readonly RszFieldAccessorFirst<OBB> Box = new RszFieldAccessorFirst<OBB>(
            (field) => field.type is RszFieldType.OBB
        ).Type(RszFieldType.OBB);
    }

    /// <summary>
    /// via.physics.AabbShape
    /// </summary>
    public static class AabbShape
    {
        // note: need to go by last one here because the newer games have an AABB field[0] in every shape type

        public static readonly RszFieldAccessorLastCallbacks<AABB> Aabb = new RszFieldAccessorLastCallbacks<AABB>([
            (field) => field.type is RszFieldType.AABB,
            (field) => field.size == 32 && !field.array
        ]).Type(RszFieldType.AABB);
    }

    /// <summary>
    /// via.physics.MeshShape
    /// </summary>
    public static class MeshShape
    {
        public static readonly RszFieldAccessorFirst<string> Mesh = new RszFieldAccessorFirst<string>(
            (field) => field.type is RszFieldType.String or RszFieldType.Resource
        ).Resource("via.render.MeshResourceHolder");
    }

    /// <summary>
    /// via.physics.StaticCompoundShape
    /// </summary>
    public static class StaticCompoundShape
    {
        public static readonly RszFieldAccessorFirst<string> Shapes = new RszFieldAccessorFirst<string>(
            (field) => field.array
        ).Object("via.physics.StaticCompoundShape.Instance");

        /// <summary>
        /// via.physics.StaticCompoundShape.Instance
        /// </summary>
        public static class Instance
        {
            public static readonly RszFieldAccessorFirst<string> Shape = new RszFieldAccessorFirst<string>(
                (field) => field.size == 4
            ).Object("via.physics.Shape");
        }
    }

    /// <summary>
    /// via.physics.HeightFieldShape
    /// </summary>
    public static class HeightFieldShape
    {
        public static readonly RszFieldAccessorFirst<string> HeightField = new RszFieldAccessorFirst<string>(
            (field) => field.type is RszFieldType.Resource or RszFieldType.String
        ).Resource("via.physics.CollisionHeightFieldResourceHolder");
    }
}
