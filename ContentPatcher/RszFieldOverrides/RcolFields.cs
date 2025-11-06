using System.Numerics;
using ContentPatcher;
using ReeLib;

namespace ContentPatcher;

public static partial class RszFieldCache
{
    /// <summary>
    /// via.physics.RequestSetCollider
    /// </summary>
    [RszAccessor("via.physics.RequestSetCollider", [nameof(GameName.re7)], GamesExclude = true)]
    public static class RequestSetCollider
    {
        public static readonly RszFieldAccessorLast<List<object>> RequestSetGroups =
            // note: re7 has a plain resource list instead of the extra object inbetween, this doesn't work there
            Last<List<object>>(f => f.array && f.type is RszFieldType.Data or RszFieldType.S32 or RszFieldType.Object)
            .Object("via.physics.RequestSetCollider.RequestSetGroup")
            .Rename();

        public static readonly RszFieldAccessorFirst<string> ColliderBank =
            First<string>(f => !f.array && f.type is RszFieldType.String or RszFieldType.Resource)
            .Resource("via.physics.CharacterColliderBankResourceHolder")
            .Rename()
            .Optional();
    }

    /// <summary>
    /// via.physics.RequestSetCollider
    /// </summary>
    [RszAccessor("via.physics.RequestSetCollider", [nameof(GameName.re7)])]
    public static class RequestSetColliderRE7
    {
        public static readonly RszFieldAccessorFirst<string> RequestSetColliders =
            First<string>(f => f.array && f.type is RszFieldType.String or RszFieldType.Resource)
            .Resource("via.physics.RequestSetColliderResourceHolder")
            .Rename();
    }

    /// <summary>
    /// via.physics.RequestSetCollider.RequestSetGroup
    /// </summary>
    [RszAccessor("via.physics.RequestSetCollider.RequestSetGroup", [nameof(GameName.re7)], GamesExclude = true)]
    public static class RequestSetGroup
    {
        public static readonly RszFieldAccessorFirst<string> Resource =
            First<string>(f => !f.array && f.type is RszFieldType.String or RszFieldType.Resource)
            .Resource("via.physics.RequestSetColliderResourceHolder")
            .Rename();
    }
}
