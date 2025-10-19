using System.Numerics;
using ContentPatcher;
using ReeLib;
using ReeLib.via;

namespace ContentPatcher;

public static partial class RszFieldCache
{
    private static readonly string[] PreDD2 = [
        nameof(GameName.re7), nameof(GameName.re2), nameof(GameName.re3), nameof(GameName.re2rt),
        nameof(GameName.re3rt), nameof(GameName.re7rt), nameof(GameName.re8), nameof(GameName.re4),
        nameof(GameName.dmc5), nameof(GameName.mhrise), nameof(GameName.sf6)
    ];

    /// <summary>
    /// via.Folder
    /// </summary>
    [RszAccessor("via.Folder")]
    public static class Folder
    {
        public static readonly RszFieldAccessorFixedIndex<string> Name = Index<string>(0).Type(RszFieldType.String);
        public static readonly RszFieldAccessorFixedIndex<string> Tags = Index<string>(1).Type(RszFieldType.String);
        public static readonly RszFieldAccessorFixedIndex<bool> Draw = Index<bool>(2).Type(RszFieldType.Bool);
        public static readonly RszFieldAccessorFixedIndex<bool> Update = Index<bool>(3).Type(RszFieldType.Bool);
        public static readonly RszFieldAccessorFixedIndex<bool> Standby = Index<bool>(4).Type(RszFieldType.Bool);
        public static readonly RszFieldAccessorFixedIndex<string> ScenePath = Index<string>(5).Type(RszFieldType.String);
        public static readonly RszFieldAccessorFirstFallbacks<Position> UniversalOffset = First<Position>([
            fi => fi.type == RszFieldType.Position,
            fi => fi.size == 24,
        ]).Type(RszFieldType.Position).Optional();
    }

    /// <summary>
    /// via.Prefab
    /// </summary>
    [RszAccessor("via.Prefab")]
    public static class Prefab
    {
        public static readonly RszFieldAccessorFixedIndex<bool> Standby = Index<bool>(0).Type(RszFieldType.Bool);
        public static readonly RszFieldAccessorFixedIndex<string> Path = Index<string>(1).Type(RszFieldType.String);
    }

    /// <summary>
    /// via.GameObject
    /// </summary>
    [RszAccessor("via.GameObject")]
    public static class GameObject
    {
        public static readonly RszFieldAccessorFixedIndex<string> Name = Index<string>(0).Type(RszFieldType.String);
        public static readonly RszFieldAccessorFixedIndex<string> Tags = Index<string>(1).Type(RszFieldType.String);
        public static readonly RszFieldAccessorFixedIndex<bool> Draw = Index<bool>(2).Type(RszFieldType.Bool);
        public static readonly RszFieldAccessorFixedIndex<bool> Update = Index<bool>(3).Type(RszFieldType.Bool);
        public static readonly RszFieldAccessorFieldList<float> TimeScale = FromList<float>(
            list => list.Any(fi => fi.index == 4) ? 4 : -1
        ).Type(RszFieldType.F32).Optional();
    }

    /// <summary>
    /// via.AnimationCurve
    /// </summary>
    [RszAccessor("via.AnimationCurve")]
    public static class AnimationCurve
    {
        public static readonly RszFieldAccessorFirst<ReeLib.via.KeyFrame> Keys = First<ReeLib.via.KeyFrame>(f => f.array).Type(RszFieldType.KeyFrame).Rename();
    }

}
