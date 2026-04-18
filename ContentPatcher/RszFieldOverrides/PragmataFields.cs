using ReeLib;

namespace ContentPatcher;

public static partial class RszFieldCache
{
    public static class Pragmata
    {
        [RszAccessor("app.ObjectDefine", nameof(GameIdentifier.pragmata))]
        public static class ObjectDefine
        {
            public static readonly RszFieldAccessorName<uint> Hash = Name<uint>().Type(RszFieldType.U32);
        }

        public static class EnemySpawnParam
        {
            /// <summary>
            /// app.ObjectDefine
            /// </summary>
            public static readonly RszFieldAccessorFixedIndex<RszInstance> ObjectID = Index<RszInstance>(2);
        }

        public static class PropSpawnParam
        {
            public static readonly RszFieldAccessorName<uint> _KindHash = Name<uint>().Type(RszFieldType.U32);
        }

        [RszAccessor("app.CharacterBodyCatalogUserData", nameof(GameIdentifier.pragmata))]
        public static class CharacterBodyCatalogUserData
        {
            /// <summary>
            /// app.CharacterBodyCatalogUserData.Data[]
            /// </summary>
            public static readonly RszFieldAccessorName<List<object>> _DataTable = Name<List<object>>();

            [RszAccessor("app.CharacterBodyCatalogUserData.Data", nameof(GameIdentifier.pragmata))]
            public static class Data
            {
                /// <summary>
                /// app.ObjectDefine
                /// </summary>
                public static readonly RszFieldAccessorName<RszInstance> _CharacterKind = Name<RszInstance>();

                /// <summary>
                /// via.Prefab
                /// </summary>
                public static readonly RszFieldAccessorName<RszInstance> _BodyPrefab = Name<RszInstance>();
            }
        }

        [RszAccessor("app.PropCatalogUserData.PropPrefabData", nameof(GameIdentifier.pragmata))]
        public static class PropCatalogUserData
        {
            /// <summary>
            /// app.PropCatalogUserData.PropPrefabData[]
            /// </summary>
            public static readonly RszFieldAccessorName<List<object>> _CatalogData = Name<List<object>>();

            [RszAccessor("app.PropCatalogUserData.PropPrefabData", nameof(GameIdentifier.pragmata))]
            public static class PropPrefabData
            {
                public static readonly RszFieldAccessorName<uint> _PropIDHash = Name<uint>();

                /// <summary>
                /// via.Prefab
                /// </summary>
                public static readonly RszFieldAccessorName<RszInstance> _Prefab = Name<RszInstance>();
            }
        }
    }
}
