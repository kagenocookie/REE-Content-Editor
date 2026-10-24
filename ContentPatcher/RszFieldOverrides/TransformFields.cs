using System.Numerics;
using ReeLib;

namespace ContentPatcher;

public static partial class RszFieldCache
{
    /// <summary>
    /// via.Transform
    /// </summary>
    [RszAccessor("via.Transform")]
    public static class Transform
    {
        public static readonly RszFieldAccessorFixedIndex<Vector3> Position = Index<Vector3>(0).Type(RszFieldType.Vec3).Rename();
        public static readonly RszFieldAccessorFixedIndex<Quaternion> Rotation = Index<Quaternion>(1).Type(RszFieldType.Quaternion).Rename();
        public static readonly RszFieldAccessorFixedIndex<Vector3> Scale = Index<Vector3>(2).Type(RszFieldType.Vec3).Rename();
        public static readonly RszFieldAccessorFixedIndex<string> ParentJoint = Index<string>(3).Type(RszFieldType.String).Rename();
        public static readonly RszFieldAccessorName<bool> AbsoluteScaling = Name<bool>(nameof(AbsoluteScaling)).Type(RszFieldType.Bool).Optional();
    }
}
