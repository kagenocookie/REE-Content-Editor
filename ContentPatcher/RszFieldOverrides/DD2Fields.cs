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
        /// app.PartSwapMeshHolder
        /// </summary>
        [RszAccessor("app.PartSwapMeshHolder", nameof(GameIdentifier.dd2))]
        public static class PartSwapMeshHolder
        {
            public static readonly RszFieldAccessorName<string> _Mesh = Name<string>().Resource("via.render.MeshResourceHolder");
        }

        /// <summary>
        /// app.PartSwapMaterialHolder
        /// </summary>
        [RszAccessor("app.PartSwapMaterialHolder", nameof(GameIdentifier.dd2))]
        public static class PartSwapMaterialHolder
        {
            public static readonly RszFieldAccessorName<string> _Material = Name<string>().Resource("via.render.MeshMaterialResourceHolder");
        }

        /// <summary>
        /// app.BodyMeshSwapItem
        /// </summary>
        [RszAccessor("app.BodyMeshSwapItem", nameof(GameIdentifier.dd2))]
        public static class BodyMeshSwapItem
        {
            public static readonly RszFieldAccessorName<uint> _Species = Name<uint>();
            public static readonly RszFieldAccessorName<uint> _Gender = Name<uint>();
            public static readonly RszFieldAccessorName<uint> _MeshID = Name<uint>();
        }

        /// <summary>
        /// app.BodySkinSwapItem
        /// </summary>
        [RszAccessor("app.BodySkinSwapItem", nameof(GameIdentifier.dd2))]
        public static class BodySkinSwapItem
        {
            public static readonly RszFieldAccessorName<uint> _Species = Name<uint>();
            public static readonly RszFieldAccessorName<uint> _Gender = Name<uint>();
            public static readonly RszFieldAccessorName<uint> _SkinID = Name<uint>();
        }

        /// <summary>
        /// app.HeadMeshSwapItem
        /// </summary>
        [RszAccessor("app.HeadMeshSwapItem", nameof(GameIdentifier.dd2))]
        public static class HeadMeshSwapItem
        {
            public static readonly RszFieldAccessorName<uint> _Species = Name<uint>();
            public static readonly RszFieldAccessorName<uint> _Gender = Name<uint>();
            public static readonly RszFieldAccessorName<uint> _HeadStyle = Name<uint>();
            public static readonly RszFieldAccessorName<uint> _MeshID = Name<uint>();
        }

        /// <summary>
        /// app.HeadSkinSwapItem
        /// </summary>
        [RszAccessor("app.HeadSkinSwapItem", nameof(GameIdentifier.dd2))]
        public static class HeadSkinSwapItem
        {
            public static readonly RszFieldAccessorName<uint> _Species = Name<uint>();
            public static readonly RszFieldAccessorName<uint> _Gender = Name<uint>();
            public static readonly RszFieldAccessorName<uint> _SkinStyle = Name<uint>();
            public static readonly RszFieldAccessorName<uint> _SkinID = Name<uint>();
            public static readonly RszFieldAccessorName<uint> _AnimID = Name<uint>();
        }

        /// <summary>
        /// app.HairSwapItem
        /// </summary>
        [RszAccessor("app.HairSwapItem", nameof(GameIdentifier.dd2))]
        public static class HairSwapItem
        {
            public static readonly RszFieldAccessorName<uint> _MeshID = Name<uint>();
            public static readonly RszFieldAccessorName<uint> _SkinID = Name<uint>();
            public static readonly RszFieldAccessorName<bool> _UseChain = Name<bool>();
            public static readonly RszFieldAccessorName<uint> _SwapMeshID = Name<uint>();
            public static readonly RszFieldAccessorName<uint> _SwapSkinID = Name<uint>();
        }

        /// <summary>
        /// app.BeardSwapItem
        /// </summary>
        [RszAccessor("app.BeardSwapItem", nameof(GameIdentifier.dd2))]
        public static class BeardSwapItem
        {
            public static readonly RszFieldAccessorName<uint> _MeshID = Name<uint>();
            public static readonly RszFieldAccessorName<uint> _SkinID = Name<uint>();
            public static readonly RszFieldAccessorName<uint> _TextureID = Name<uint>();
        }

        /// <summary>
        /// app.PartSwapCatalog.Item
        /// </summary>
        [RszAccessor("app.PartSwapCatalog.Item", nameof(GameIdentifier.dd2))]
        public static class PartSwapCatalogItem
        {
            public static readonly RszFieldAccessorName<RszInstance> _Prefab = Name<RszInstance>();
            public static readonly RszFieldAccessorName<uint> _Hash = Name<uint>();
        }
    }
}
