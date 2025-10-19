using System.Numerics;
using ContentPatcher;
using ReeLib;
using ReeLib.via;

namespace ContentPatcher;

public static partial class RszFieldCache
{
    /// <summary>
    /// via.dynamics.CompositeRigidBodyMeshSet.MeshInfo
    /// </summary>
    public static class CompositeRigidBodyMeshSet
    {
        [RszAccessor("via.dynamics.CompositeRigidBodyMeshSet.MeshInfo", [nameof(PreDD2)], GamesExclude = true)]
        public static class MeshInfo
        {
            public static readonly RszFieldAccessorFirst<Vector3> Position = First<Vector3>(f => f.type is RszFieldType.Vec3 || f.size == 16)
                .Type(RszFieldType.Vec3)
                .Rename()
                .Optional();
            public static readonly RszFieldAccessorFirst<string> Resource = First<string>(f => f.type is RszFieldType.String or RszFieldType.Resource)
                .Resource("via.dynamics.DynamicsBaseResourceHolder")
                .Rename()
                .Optional();
        }
    }
}
