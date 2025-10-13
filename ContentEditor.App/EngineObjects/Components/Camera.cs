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

    public void LookAt(Folder target, bool resetPosition)
    {
        LookAt(target.GetWorldSpaceBounds(), resetPosition);
    }
    public void LookAt(GameObject target, bool resetPosition)
    {
        LookAt(target.GetWorldSpaceBounds(), resetPosition);
    }

    public void LookAt(AABB bounds, bool resetPosition)
    {
        Vector3 offset;
        var targetCenter = bounds.Center;
        if (bounds.IsEmpty) {
            offset = new Vector3(3, 3, 3);
        } else {
            offset = Vector3.One * (bounds.Size.Length() * 0.35f);
        }

        if (!resetPosition) {
            var optimalDistance = offset.Length();
            var selfpos = Transform.Position;
            if (selfpos == targetCenter) {
                offset = Vector3.Normalize(offset) * optimalDistance;
            } else {
                offset = Vector3.Normalize(selfpos - targetCenter) * optimalDistance;
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
