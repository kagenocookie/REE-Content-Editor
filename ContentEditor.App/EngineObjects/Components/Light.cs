using System.Buffers;
using System.Numerics;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

public abstract class Light(GameObject gameObject, RszInstance data) : Component(gameObject, data), IGizmoComponent
{
    public bool IsEnabled => AppConfig.Instance.RenderLights.Get();

    protected Material? _defaultLightGizmoMaterial;
    protected Material? _defaultLightGizmoMaterialInner;

    protected Material CreateLightMaterial(RenderContext ctx) => _defaultLightGizmoMaterial = ctx.GetMaterialBuilder(BuiltInMaterials.MonoColor, "light")
        .Color("_MainColor", Colors.Lights)
        .Float("_FadeMaxDistance", 250)
        .Blend();

    protected Material CreateLightMaterialInner(RenderContext ctx) => _defaultLightGizmoMaterialInner = ctx.GetMaterialBuilder(BuiltInMaterials.MonoColor, "light_inner")
        .Color("_MainColor", Colors.Lights with { W = Colors.Lights.W * 0.5f })
        .Float("_FadeMaxDistance", 250)
        .Blend();

    public abstract GizmoContainer? Update(GizmoContainer? gizmo);

    internal override void OnActivate()
    {
        base.OnActivate();
        Scene!.Root.Gizmos.Add(this);
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        Scene!.Root.Gizmos.Remove(this);
    }

}

[RszComponentClass("via.render.SpotLight")]
public class SpotLight(GameObject gameObject, RszInstance data) : Light(gameObject, data)
{
    public override GizmoContainer? Update(GizmoContainer? gizmo)
    {
        gizmo ??= new GizmoContainer(Scene!, this);

        var fwd = Transform.Forward;
        var radius = RszFieldCache.SpotLight.Radius.GetOrDefault(Data, 0.03f);
        var range = RszFieldCache.SpotLight.ReferenceEffectiveRange.GetOrDefault(Data, 10);
        var cone = RszFieldCache.SpotLight.Cone.GetOrDefault(Data, 50);
        var spread = Math.Min(cone, RszFieldCache.SpotLight.Spread.GetOrDefault(Data, 50));
        // note: I assume spread and cone are for inner and outer cone respectively
        // inner being full intensity and then gradually 0 intensity until cone angle

        // note2: the angle/distance calculation is probably wrong, very visibly when cone approaches 90
        // we're offsetting a circle instead of making an arc

        if (Math.Abs(cone - 90f) < 0.1f) cone = 89.9f;

        var origin = Transform.Position;
        var endRadiusCone = MathF.Tan(cone / 180f * MathF.PI) * range;
        var endRadiusSpread = MathF.Tan(spread / 180f * MathF.PI) * range;

        gizmo.PushMaterial(_defaultLightGizmoMaterial ??= CreateLightMaterial(Scene!.RenderContext));
        gizmo.Add(new Cone(origin + fwd * radius, 0.001f, origin + fwd * range * Math.Sign(endRadiusCone), Math.Abs(endRadiusCone)));
        gizmo.PopMaterial();

        gizmo.PushMaterial(_defaultLightGizmoMaterialInner ??= CreateLightMaterialInner(Scene!.RenderContext));
        gizmo.Add(new Cone(origin + fwd * radius, 0.001f, origin + fwd * range * Math.Sign(endRadiusSpread), Math.Abs(endRadiusSpread)));
        gizmo.PopMaterial();
        return gizmo;
    }
}

[RszComponentClass("via.render.PointLight")]
public class PointLight(GameObject gameObject, RszInstance data) : Light(gameObject, data)
{
    public override GizmoContainer? Update(GizmoContainer? gizmo)
    {
        gizmo ??= new GizmoContainer(Scene!, this);
        gizmo.PushMaterial(_defaultLightGizmoMaterial ??= CreateLightMaterial(Scene!.RenderContext));
        var radius = RszFieldCache.PointLight.Radius.GetOrDefault(Data, 5);
        gizmo.Add(new Sphere(Transform.Position, radius));
        gizmo.PopMaterial();
        return gizmo;
    }
}

[RszComponentClass("via.render.DirectionalLight")]
public class DirectionalLight(GameObject gameObject, RszInstance data) : Light(gameObject, data)
{
    private static readonly Vector3 fallbackDir = Vector3.Normalize(new Vector3(0.3f, -0.5f, 0.3f));

    public override GizmoContainer? Update(GizmoContainer? gizmo)
    {
        gizmo ??= new GizmoContainer(Scene!, this);
        gizmo.PushMaterial(_defaultLightGizmoMaterial ??= CreateLightMaterial(Scene!.RenderContext), null, ShapeBuilder.GeometryType.Filled);

        var origin = Transform.Position;
        var direction = Vector3.Normalize(RszFieldCache.DirectionalLight.Direction.GetOrDefault(Data, fallbackDir));

        gizmo.Add(new Cylinder(origin, origin + direction * 10f, 0.025f));
        gizmo.Add(new Cone(origin + direction * 10f, 0.25f, origin + direction * 10.5f, 0.001f));
        gizmo.PopMaterial();
        return gizmo;
    }
}
