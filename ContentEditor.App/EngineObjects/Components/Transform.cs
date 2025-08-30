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

    public Transform(GameObject gameObject, RszInstance data) : base(gameObject, data)
    {
    }

    public Transform(GameObject gameObject, Workspace data) : base(gameObject, RszInstance.CreateInstance(data.RszParser, data.Classes.Transform))
    {
    }

    public Vector3 LocalPosition
    {
        get => ((Vector3)Data.Values[0]);
        set {
            Data.Values[0] = value;
            InvalidateTransform();
        }
    }

    public Quaternion LocalRotation
    {
        get => ((Quaternion)Data.Values[1]);
        set {
            Data.Values[1] = value;
            InvalidateTransform();
        }
    }

    public Vector3 LocalScale {
        get => ((Vector3)Data.Values[2]);
        set {
            Data.Values[2] = value;
            InvalidateTransform();
        }
    }

    public Vector3 Position => WorldTransform.Column4.ToSystem().ToVec3();

    public Vector3D<float> SilkLocalPosition => ((Vector3)Data.Values[0]).ToGeneric();
    public Quaternion<float> SilkLocalRotation => ((Quaternion)Data.Values[1]).ToGeneric();
    public Vector3D<float> SilkLocalScale => ((Vector3)Data.Values[2]).ToGeneric();

    public Vector3 LocalForward => Vector3.Transform(Vector3.UnitZ, LocalRotation);

    private Matrix4X4<float> _cachedWorldTransform = Matrix4X4<float>.Identity;
    private bool _worldTransformValid;
    public ref readonly Matrix4X4<float> WorldTransform
    {
        get {
            if (_worldTransformValid) return ref _cachedWorldTransform;

            if (GameObject.Parent != null) {
                _cachedWorldTransform = ComputeLocalTransformMatrix() * GameObject.Parent.WorldTransform;
            } else if (GameObject.Folder != null) {
                _cachedWorldTransform = ComputeLocalTransformMatrix() * Matrix4X4.CreateTranslation<float>(GameObject.Folder.OffsetSilk);
            } else {
                _cachedWorldTransform = ComputeLocalTransformMatrix();
            }
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
        Matrix4X4<float> mat = Matrix4X4.CreateScale<float>(SilkLocalScale);

        if (!quat.IsIdentity) {
            mat = mat * Matrix4X4.CreateFromQuaternion(quat);
        }
        return mat * Matrix4X4.CreateTranslation(LocalPosition.ToSilkNet());
    }

    public void Translate(Vector3 offset)
    {
        LocalPosition += offset;
    }

    public void TranslateForwardAligned(Vector3 offset)
    {
        LocalPosition += Vector3D.Transform(offset.ToGeneric(), SilkLocalRotation).ToSystem();
    }

    public void ComponentInit()
    {
        LocalRotation = Quaternion.Identity;
        LocalScale = Vector3.One;
    }

    public void Rotate(Quaternion<float> quaternion)
    {
        var newQuat = SilkLocalRotation * quaternion;
        LocalRotation = newQuat.ToSystem();
    }

    public void LookAt(Vector3 target)
    {
        LookAt(target, new Vector3(0, 1, 0));
    }

    public void LookAt(Vector3 target, Vector3 up)
    {
        LocalRotation = LocalPosition.CreateLookAtQuaternion(target, up).ToSystem();
    }
}

public static class TransformExtensions
{
    public static Quaternion<float> ToSilkNet(this Quaternion quat) => new Quaternion<float>(quat.X, quat.Y, quat.Z, quat.W);
    public static Vector3D<float> ToSilkNet(this Vector3 vec) => new Vector3D<float>(vec.X, vec.Y, vec.Z);

    public static Quaternion<float> ToSilkNetQuaternion(this Vector4 vec) => new Quaternion<float>(vec.X, vec.Y, vec.Z, vec.W);
    public static Vector3D<float> ToSilkNetVec3(this Vector4 vec) => new Vector3D<float>(vec.X, vec.Y, vec.Z);

    public static Quaternion<float> CreateLookAtQuaternion(this Vector3 from, Vector3 to, Vector3 up)
    {
        var fwd = Vector3.Normalize(from - to);
        var right = Vector3.Normalize(Vector3.Cross(up, fwd));
        var newUp = Vector3.Cross(fwd, right);
        var lookAt = new Matrix4X4<float>(
            right.X, right.Y, right.Z, 0,
            newUp.X, newUp.Y, newUp.Z, 0,
            fwd.X, fwd.Y, fwd.Z, 0,
            0, 0, 0, 1
        );
        return Quaternion<float>.CreateFromRotationMatrix(lookAt);
    }

    public static Quaternion<float> CreateFromToQuaternion(this Vector3 from, Vector3 to)
    {
        var vector = Vector3.Cross(from, to);
        var dot = Vector3.Dot(from, to);
        if (dot < -0.99999f) {
            return new Quaternion<float>(0, 1, 0, 0);
        } else {
            var num2 = MathF.Sqrt((1f + dot) * 2f);
            var num3 = 1f / num2;
            return new Quaternion<float>(vector.X * num3, vector.Y * num3, vector.Z * num3, num2 * 0.5f);
        }
    }

    /// <summary>
    /// Returns the euler angles of the given quaternion (order: pitch, yaw, roll) in radians.
    /// </summary>
    public static Vector3 ToEuler(this Quaternion<float> q)
    {
        var euler = new Vector3();

        // roll (Z)
        float sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
        float cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        euler.Z = MathF.Atan2(sinr_cosp, cosr_cosp);

        // pitch (X)
        float sinp = 2 * (q.W * q.X - q.Y * q.Z);
        if (MathF.Abs(sinp) >= 1)
            euler.X = MathF.CopySign(MathF.PI / 2, sinp); // clamp to 90°
        else
            euler.X = MathF.Asin(sinp);

        // yaw (Y)
        float siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
        float cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        euler.Y = MathF.Atan2(siny_cosp, cosy_cosp);

        return euler;
    }
}