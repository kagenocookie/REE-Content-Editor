using System.Numerics;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

[RszComponentClass("via.Camera")]
public sealed class Camera : Component, IConstructorComponent, IFixedClassnameComponent
{
    static string IFixedClassnameComponent.Classname => "via.Camera";

    // TODO fetch from instance data
    public float FOV => 85f;

    public void ComponentInit()
    {
    }

    public void LookAt(GameObject target, bool resetPosition)
    {
        var renderable = target.Components.OfType<RenderableComponent>().FirstOrDefault();
        Vector3 offset;
        var targetCenter = target.Transform.Position;
        if (renderable != null) {
            var bounds = renderable.LocalBounds;
            if (bounds.minpos == bounds.maxpos || bounds.Size.LengthSquared() > 10000*10000) {
                bounds.minpos = new Vector3(-1, -1, -1);
                bounds.maxpos = new Vector3(1, 1, 1);
            }
            offset = Vector3.One * (bounds.Size.Length() * 0.35f);
            targetCenter = Vector3D.Transform(renderable.LocalBounds.Center.ToGeneric(), renderable.Transform.WorldTransform).ToSystem();
        } else {
            offset = new Vector3(3, 3, 3);
        }

        if (!resetPosition) {
            var optimalDistance = offset.Length();
            var selfpos = Transform.Position;
            if (selfpos == targetCenter) {
                offset = Vector3.Normalize(offset) * optimalDistance;
            } else{
                offset = Vector3.Normalize(targetCenter - selfpos) * optimalDistance;
            }
        } else {
            offset.X *= 0.4f;
        }

        Transform.LocalPosition = targetCenter + offset;
        Transform.LookAt(targetCenter);
    }

    public Camera(GameObject gameObject, RszInstance data) : base(gameObject, data)
    {
    }

}
