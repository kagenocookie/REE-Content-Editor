using ReeLib;

namespace ContentPatcher;

public static partial class RszFieldCache
{
    /// <summary>
    /// via.motion.Chain
    /// </summary>
    [RszAccessor("via.motion.Chain", [nameof(GameName.re7)], GamesExclude = true)]
    public static class Chain
    {
        public static readonly RszFieldAccessorFirstFallbacks<string> ChainAsset =
            First<string>([
                f => f.name == "ChainAsset",
                f => f.type is RszFieldType.Resource or RszFieldType.String
            ])
            .Rename();
    }

    /// <summary>
    /// via.character.CollisionShapePreset
    /// </summary>
    [RszAccessor("via.character.CollisionShapePreset", [nameof(PreDD2)], GamesExclude = true)]
    public static class CollisionShapePreset
    {
        public static readonly RszFieldAccessorFirstFallbacks<List<object>> ShapePresetInfos =
            First<List<object>>([
                f => f.name == "CollisionShapePresetInfos",
                f => f.array
            ])
            .Object("via.character.CollisionShapePresetInfo")
            .Rename();
    }

    /// <summary>
    /// via.character.CollisionShapePresetInfo
    /// </summary>
    [RszAccessor("via.character.CollisionShapePresetInfo", [nameof(PreDD2)], GamesExclude = true)]
    public static class CollisionShapePresetInfo
    {
        public static readonly RszFieldAccessorFirst<bool> Enabled =
            First<bool>(f => f.type is RszFieldType.Bool or RszFieldType.U8 or RszFieldType.S8)
            .Type(RszFieldType.Bool)
            .Rename();

        public static readonly RszFieldAccessorFirst<string> Resource =
            First<string>(f => f.type is RszFieldType.String or RszFieldType.Resource)
            .Resource("via.character.CollisionShapePresetResourceHolder")
            .Rename();
    }
}
