using System.Numerics;
using ReeLib;

namespace ContentPatcher;

public static partial class RszFieldCache
{
    public static class DD2
    {
        /// <summary>
        /// app.ItemCommonParam
        /// </summary>
        [RszAccessor("app.ItemCommonParam", nameof(GameIdentifier.dd2))]
        public static class ItemCommonParam
        {
            public static readonly RszFieldAccessorName<int> _Id = Name<int>();
        }
        /// <summary>
        /// app.ItemArmorParam
        /// </summary>
        [RszAccessor("app.ItemArmorParam", nameof(GameIdentifier.dd2))]
        public static class ItemArmorParam
        {
            public static readonly RszFieldAccessorName<int> _EquipCategory = Name<int>();
            public static readonly RszFieldAccessorName<short> _StyleNo = Name<short>();
        }
        /// <summary>
        /// app.PartSwapper
        /// </summary>
        [RszAccessor("app.PartSwapper", nameof(GameIdentifier.dd2))]
        public static class PartSwapper
        {
            public static readonly RszFieldAccessorName<RszInstance> _Meta = Name<RszInstance>();
        }

        /// <summary>
        /// app.WeaponCatalogData.Data
        /// </summary>
        [RszAccessor("app.WeaponCatalogData.Data", nameof(GameIdentifier.dd2))]
        public static class WeaponCatalogData
        {
            public static readonly RszFieldAccessorName<uint> ID = Name<uint>();
            public static readonly RszFieldAccessorName<RszInstance> Prefab = Name<RszInstance>();
        }

        /// <summary>
        /// app.WeaponSetting.OffsetSetting
        /// </summary>
        [RszAccessor("app.WeaponSetting.OffsetSetting", nameof(GameIdentifier.dd2))]
        public static class WeaponSetting_OffsetSetting
        {
            public static readonly RszFieldAccessorName<List<object>> DrawSetting = Name<List<object>>();
            public static readonly RszFieldAccessorName<List<object>> SheatheSetting = Name<List<object>>();
        }

        /// <summary>
        /// app.WeaponSetting.Offset
        /// </summary>
        [RszAccessor("app.WeaponSetting.Offset", nameof(GameIdentifier.dd2))]
        public static class WeaponSetting_Offset
        {
            public static readonly RszFieldAccessorName<bool> IsLeftSetting = Name<bool>();
            public static readonly RszFieldAccessorName<string> ParentJointName = Name<string>();
            public static readonly RszFieldAccessorName<Vector3> LocalPosition = Name<Vector3>();
            public static readonly RszFieldAccessorName<Quaternion> LocalRotation = Name<Quaternion>();
            public static readonly RszFieldAccessorName<float> Scale = Name<float>();
        }
    }
}
