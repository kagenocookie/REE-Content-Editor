using System.Numerics;
using System.Runtime.CompilerServices;
using ContentEditor.Core;
using ReeLib;
using ReeLib.Pfb;

namespace ContentEditor.App;

public class Transform : Component
{
    public Vector3 LocalPosition => ((Vector4)Data.Values[0]).ToVec3();
    public Quaternion LocalRotation => ((Vector4)Data.Values[1]).ToQuaternion();
    public Vector3 LocalScale => ((Vector4)Data.Values[2]).ToVec3();

    public Transform(GameObject gameObject, RszInstance data) : base(gameObject, data)
    {
    }

    public Transform(GameObject gameObject, Workspace data) : base(gameObject, RszInstance.CreateInstance(data.RszParser, data.Classes.Transform))
    {
    }
}
