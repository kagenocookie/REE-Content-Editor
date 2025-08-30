using System.Numerics;
using ContentPatcher;
using ReeLib;
using ReeLib.via;

namespace ContentPatcher;

public static partial class RszFieldCache
{
    /// <summary>
    /// via.Transform
    /// </summary>
    [RszAccessor("via.Transform")]
    public static class Transform
    {
        public static readonly RszFieldAccessorFixedIndex<Vector3> LocalPosition = Index<Vector3>(0, "LocalPosition").Type(RszFieldType.Vec3);
        public static readonly RszFieldAccessorFixedIndex<Quaternion> LocalRotation = Index<Quaternion>(1, "LocalRotation").Type(RszFieldType.Quaternion);
        public static readonly RszFieldAccessorFixedIndex<Vector3> LocalScale = Index<Vector3>(2, "LocalScale").Type(RszFieldType.Vec3);
    }
}
