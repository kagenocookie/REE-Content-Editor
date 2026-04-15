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
}
