using System.Numerics;
using ContentPatcher;
using ReeLib;
using ReeLib.via;

namespace ContentPatcher;

public static partial class RszFieldCache
{
    public static class RE2
    {
        /// <summary>
        /// [RE2] app.ropeway.item.ItemPositions
        /// [RE3] offline.item.ItemPositions
        /// </summary>
        [RszAccessor("app.ropeway.item.ItemPositions", nameof(GameIdentifier.re2), nameof(GameIdentifier.re2rt))]
        [RszAccessor("offline.item.ItemPositions", nameof(GameIdentifier.re3), nameof(GameIdentifier.re3rt))]
        public static class ItemPositions
        {
            public static readonly RszFieldAccessorName<int> InitializeItemId = Name<int>();
            public static readonly RszFieldAccessorName<int> InitializeWeaponId = Name<int>();
            public static readonly RszFieldAccessorName<int> InitializeBulletId = Name<int>();
        }

        /// <summary>
        /// [RE2] app.ropeway.EnemyContextController
        /// [RE3] offline.EnemyContextController
        /// </summary>
        [RszAccessor("app.ropeway.EnemyContextController", nameof(GameIdentifier.re2), nameof(GameIdentifier.re2rt))]
        [RszAccessor("offline.EnemyContextController", nameof(GameIdentifier.re3), nameof(GameIdentifier.re3rt))]
        public static class EnemyContextController
        {
            public static readonly RszFieldAccessorName<int> InitialKind = Name<int>();
        }
    }
}
