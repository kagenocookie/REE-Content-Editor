using System.Numerics;
using ContentPatcher;
using ReeLib;

namespace ContentPatcher;

public static partial class RszFieldCache
{
    /// <summary>
    /// via.render.Light
    /// </summary>
    [RszAccessor("via.render.Light")]
    public static class Light
    {
        public static readonly RszFieldAccessorName<float> Intensity = Name<float>().Optional();
        public static readonly RszFieldAccessorName<Vector3> Color = Name<Vector3>().Type(RszFieldType.Vec3).Optional();
        public static readonly RszFieldAccessorName<float> Temperature = Name<float>().Optional();
        public static readonly RszFieldAccessorName<float> BounceIntensity = Name<float>().Optional();
        public static readonly RszFieldAccessorName<float> SpecularScale = Name<float>().Optional();
        public static readonly RszFieldAccessorName<float> MinRoughness = Name<float>().Optional();
    }

    /// <summary>
    /// via.render.PointLight
    /// </summary>
    [RszAccessor("via.render.PointLight")]
    public static class PointLight
    {
        public static readonly RszFieldAccessorName<float> Radius = Name<float>().Optional();
        public static readonly RszFieldAccessorName<int> Unit = Name<int>().Enum(RszFieldType.S32, "via.render.LightPowerUnitType").Optional();
    }

    /// <summary>
    /// via.render.DirectionalLight
    /// </summary>
    [RszAccessor("via.render.DirectionalLight")]
    public static class DirectionalLight
    {
        public static readonly RszFieldAccessorName<Vector3> Direction = Name<Vector3>().Type(RszFieldType.Vec3).Optional();
    }

    /// <summary>
    /// via.render.SpotLight
    /// </summary>
    [RszAccessor("via.render.SpotLight")]
    public static class SpotLight
    {
        public static readonly RszFieldAccessorName<float> Radius = Name<float>().Optional();
        public static readonly RszFieldAccessorName<float> ReferenceEffectiveRange = Name<float>().Optional();
        public static readonly RszFieldAccessorName<int> Unit = Name<int>().Enum(RszFieldType.S32, "via.render.LightPowerUnitType").Optional();
        public static readonly RszFieldAccessorName<float> Cone = Name<float>().Optional();
        public static readonly RszFieldAccessorName<float> Spread = Name<float>().Optional();
        public static readonly RszFieldAccessorName<float> Falloff = Name<float>().Optional();
    }
}
