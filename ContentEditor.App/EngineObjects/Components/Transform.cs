using System.Numerics;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using Silk.NET.Maths;

namespace ContentEditor.App;

[RszComponentClass("via.Transform")]
public sealed class Transform : Component, IConstructorComponent, IFixedClassnameComponent
{
    static string IFixedClassnameComponent.Classname => "via.Transform";

    public Vector3 LocalPosition
    {
        get => ((Vector4)Data.Values[0]).ToVec3();
        set {
            Data.Values[0] = new Vector4(value.X, value.Y, value.Z, 0);
            InvalidateTransform();
        }
    }

    public Quaternion LocalRotation
    {
        get => ((Vector4)Data.Values[1]).ToQuaternion();
        set {
            Data.Values[1] = new Vector4(value.X, value.Y, value.Z, value.W);
            InvalidateTransform();
        }
    }

    public Vector3 LocalScale {
        get => ((Vector4)Data.Values[2]).ToVec3();
        set {
            Data.Values[2] = new Vector4(value.X, value.Y, value.Z, 0);
            InvalidateTransform();
        }
    }

    public Vector3D<float> SilkLocalPosition => ((Vector4)Data.Values[0]).ToSilkNetVec3();
    public Quaternion<float> SilkLocalRotation => ((Vector4)Data.Values[1]).ToSilkNetQuaternion();
    public Vector3D<float> SilkLocalScale => ((Vector4)Data.Values[2]).ToSilkNetVec3();

    private Matrix4X4<float> _cachedWorldTransform = Matrix4X4<float>.Identity;
    private bool _worldTransformValid;
    public ref readonly Matrix4X4<float> WorldTransform
    {
        get {
            if (_worldTransformValid) return ref _cachedWorldTransform;
            Matrix4X4<float> parentMatrix = Matrix4X4<float>.Identity;
            if (GameObject.Parent != null) {
                parentMatrix = GameObject.WorldTransform;
            } else if (GameObject.Folder != null) {
                parentMatrix = Matrix4X4.CreateTranslation<float>(GameObject.Folder.OffsetSilk);
            }
            _cachedWorldTransform = parentMatrix * ComputeLocalTransformMatrix();
            _worldTransformValid = true;
            return ref _cachedWorldTransform;
        }
    }

    public void InvalidateTransform()
    {
        _worldTransformValid = false;
        foreach (var child in GameObject.Children) {
            child.Transform.InvalidateTransform();
        }
    }

    public Matrix4X4<float> ComputeLocalTransformMatrix()
    {
        var quat = SilkLocalRotation;
        Matrix4X4<float> mat = Matrix4X4.CreateTranslation<float>(LocalPosition.ToSilkNet());
        if (!quat.IsIdentity) {
            mat = Matrix4X4.Transform<float>(mat, LocalRotation.ToSilkNet());
        }
        var scale = SilkLocalScale;
        if (scale != Vector3D<float>.One) {
            mat = Matrix4X4.CreateScale<float>(scale) * mat;
        }
        return mat;
    }

    public void Translate(Vector3 offset)
    {
        LocalPosition += offset;
    }

    public void TranslateForwardAligned(Vector3 offset)
    {
        LocalPosition += Vector3D.Transform(offset.ToGeneric(), Quaternion<float>.Inverse(SilkLocalRotation)).ToSystem();
    }

    public void ComponentInit()
    {
        LocalRotation = Quaternion.Identity;
        LocalScale = Vector3.One;
    }

    public void Rotate(Quaternion<float> quaternion)
    {
        var newQuat = quaternion * SilkLocalRotation;
        LocalRotation = newQuat.ToSystem();
    }

    public Transform(GameObject gameObject, RszInstance data) : base(gameObject, data)
    {
    }

    public Transform(GameObject gameObject, Workspace data) : base(gameObject, RszInstance.CreateInstance(data.RszParser, data.Classes.Transform))
    {
    }
}

public static class TransformExtensions
{
    public static Quaternion<float> ToSilkNet(this Quaternion quat) => new Quaternion<float>(quat.X, quat.Y, quat.Z, quat.W);
    public static Vector3D<float> ToSilkNet(this Vector3 vec) => new Vector3D<float>(vec.X, vec.Y, vec.Z);

    public static Quaternion<float> ToSilkNetQuaternion(this Vector4 vec) => new Quaternion<float>(vec.X, vec.Y, vec.Z, vec.W);
    public static Vector3D<float> ToSilkNetVec3(this Vector4 vec) => new Vector3D<float>(vec.X, vec.Y, vec.Z);
}