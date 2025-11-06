using ReeLib;

namespace ContentPatcher;

// TODO verify all games

public static partial class RszFieldCache
{
    /// <summary>
    /// via.navigation.AIMap
    /// </summary>
    [RszAccessor("via.navigation.AIMap")]
    public static class AIMap
    {
        public static readonly RszFieldAccessorFirst<List<object>> Maps = First<List<object>>(
            f => f.type is RszFieldType.Object
        ).Object("via.navigation.MapHandle").Rename();
    }

    /// <summary>
    /// via.navigation.AIMap
    /// </summary>
    [RszAccessor("via.navigation.AIMapSection")]
    public static class AIMapSection
    {
        public static readonly RszFieldAccessorFirst<List<object>> Maps = First<List<object>>(
            f => f.type is RszFieldType.Object
        ).Object("via.navigation.MapHandle").Rename();
    }

    /// <summary>
    /// via.navigation.AIMap
    /// </summary>
    [RszAccessor("via.navigation.MapHandle")]
    public static class MapHandle
    {
        public static readonly RszFieldAccessorFirst<string> Resource = First<string>(
            f => f.type is RszFieldType.String or RszFieldType.Resource
        ).Resource("via.navigation.AIMapBaseResourceHolder");
    }
}
