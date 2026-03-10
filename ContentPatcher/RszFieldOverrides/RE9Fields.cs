using ReeLib;

namespace ContentPatcher;

public static partial class RszFieldCache
{
    public static class RE9
    {
        [RszAccessor("app.Cp_B000SpawnParam", nameof(GameIdentifier.re9))]
        public static class EnemySpawnParam
        {
            public static readonly RszFieldAccessorName<string> _EquipedWeapon = Name<string>();

            public static readonly RszFieldAccessorName<RszInstance> _CharacterSpawnSettings = Name<RszInstance>().Optional();
        }

        [RszAccessor("app.CharacterSpawnSetting", nameof(GameIdentifier.re9))]
        public static class CharacterSpawnSetting
        {
            public static readonly RszFieldAccessorName<int> _MontageModelID = Name<int>();
            public static readonly RszFieldAccessorName<int> _MontagePresetID = Name<int>();
            public static readonly RszFieldAccessorName<bool> _RandomMontagePreset = Name<bool>();
        }

        [RszAccessor("app.CharacterMontagePresetCatalogUserData.Entry", nameof(GameIdentifier.re9))]
        public static class CharacterMontagePresetCatalogUserData
        {
            public static readonly RszFieldAccessorFirst<List<object>> _Presets = Array();

            [RszAccessor("app.CharacterMontagePresetCatalogUserData.Entry", nameof(GameIdentifier.re9))]
            public static class Entry
            {
                public static readonly RszFieldAccessorName<RszInstance> _Preset = Name<RszInstance>();
            }
        }

        [RszAccessor("app.MontageData", nameof(GameIdentifier.re9))]
        public static class MontageData
        {
            public static readonly RszFieldAccessorName<int> _ModelID = Name<int>();
            public static readonly RszFieldAccessorName<int> _ID = Name<int>();
            public static readonly RszFieldAccessorFirst<List<object>> _PartsList = Array();

            [RszAccessor("app.MontageData.MontageParts", nameof(GameIdentifier.re9))]
            public static class Parts
            {
                public static readonly RszFieldAccessorName<string> _PartsName = Name<string>();
                public static readonly RszFieldAccessorName<RszInstance> _PrefabResource = Name<RszInstance>();
            }
        }

        [RszAccessor("app.ItemCatalogUserData.ItemData", nameof(GameIdentifier.re9))]
        public static class ItemData
        {
            public static readonly RszFieldAccessorName<RszInstance> _LayouterPrefab = Name<RszInstance>();
        }
    }
}
