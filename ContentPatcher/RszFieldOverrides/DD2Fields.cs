using System.Numerics;
using ContentPatcher;
using ReeLib;
using ReeLib.via;

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
    }
}
