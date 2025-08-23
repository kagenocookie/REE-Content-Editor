using System.Numerics;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
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

    public Camera(GameObject gameObject, RszInstance data) : base(gameObject, data)
    {
    }

}
