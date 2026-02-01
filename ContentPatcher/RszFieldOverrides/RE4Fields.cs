using System.Numerics;
using ContentPatcher;
using ReeLib;
using ReeLib.via;

namespace ContentPatcher;

public static partial class RszFieldCache
{
    public static class RE4
    {
        [RszAccessor("chainsaw.Ch1b5z1SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1b7z0SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1c0SpawnParamCommon", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1c0z0SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1c0z0SpawnParam_AO", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1c0z0SpawnParam_AO_G", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1c0z0SpawnParamMercenaries", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1c0z2SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1c0z2SpawnParamMercenaries", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1c0z2SpawnParam_AO", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1c8z0SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1c8z0SpawnParamMercenaries", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1d1z1SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1d1z1SpawnParamMercenaries", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1d2z0SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1d4z0SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1d6z0SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1e0z0SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1e0z0SpawnParamMercenaries", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1f1z0SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1f4z1SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1f5z1SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1f6z0SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1f7z0SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1f8z0SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch4fez0SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch7k0z0SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch8g3z0SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch8gaz0SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch8g2z0SpawnParam", nameof(GameIdentifier.re4))]
        public static class EnemySpawnParam
        {
            public static readonly RszFieldAccessorName<uint> _MontageID = Name<uint>();
        }

        [RszAccessor("chainsaw.Ch1c0z1SpawnParam", nameof(GameIdentifier.re4))]
        [RszAccessor("chainsaw.Ch1c0z1SpawnParamMercenaries", nameof(GameIdentifier.re4))]
        public static class Ch1c0z1SpawnParam
        {
            public static readonly RszFieldAccessorName<uint> _Ch1c0z1MontageID = Name<uint>();
        }

        [RszAccessor("chainsaw.Ch1c0z2SpawnParam", nameof(GameIdentifier.re4))]
        public static class Ch1c0z2SpawnParam
        {
            public static readonly RszFieldAccessorName<uint> _Ch1c0z2MontageID = Name<uint>();
        }

        [RszAccessor("chainsaw.CostumePresetCatalogUserData", nameof(GameIdentifier.re4))]
        public static class CostumePresetCatalogUserData
        {
            public static readonly RszFieldAccessorFirst<List<object>> _DataTable = Array();

            [RszAccessor("chainsaw.CostumePresetCatalogUserData.Data", nameof(GameIdentifier.re4))]
            public static class Data
            {
                public static readonly RszFieldAccessorName<int> _KindID = Name<int>();
                public static readonly RszFieldAccessorName<RszInstance> _CostumePresetUserData = Name<RszInstance>();
            }
        }

        [RszAccessor("chainsaw.CostumePresetUserData", nameof(GameIdentifier.re4))]
        public static class CostumePresetUserData
        {
            /// <summary>
            /// chainsaw.CostumePresetUserData.Data[]
            /// </summary>
            public static readonly RszFieldAccessorFirst<List<object>> _DataTable = Array();

            [RszAccessor("chainsaw.CostumePresetUserData.Data", nameof(GameIdentifier.re4))]
            public static class Data
            {
                public static readonly RszFieldAccessorFixedIndex<uint> _ID = Index<uint>(0);
                /// <summary>
                /// chainsaw.CostumePresetUserData.Data.PrefabData[]
                /// </summary>
                public static readonly RszFieldAccessorFirst<List<object>> _PrefabTable = Array();

                [RszAccessor("chainsaw.CostumePresetUserData.Data.PrefabData", nameof(GameIdentifier.re4))]
                public static class PrefabData
                {
                    public static readonly RszFieldAccessorFirst<RszInstance> _Prefab = First<RszInstance>(f => f.original_type == "via.Prefab");
                    public static readonly RszFieldAccessorFirst<List<object>> _InitialPartsOffList = Array();
                }
            }
        }
    }
}
