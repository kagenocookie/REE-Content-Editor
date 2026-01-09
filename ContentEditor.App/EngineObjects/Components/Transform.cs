using System.Numerics;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.via;
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

    public Vector3 LocalForward => Vector3.Transform(-Vector3.UnitZ, LocalRotation);

    public Vector3 Position => WorldTransform.Row4.ToSystem().ToVec3();
    public Quaternion Rotation => Quaternion.CreateFromRotationMatrix(WorldTransform.ToSystem());

    public Vector3 Forward => Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, Rotation));
    public Vector3 Right => Vector3.Normalize(Vector3.Transform(Vector3.UnitX, Rotation));
    public Vector3 Up => Vector3.Normalize(Vector3.Transform(Vector3.UnitY, Rotation));

    public Vector3D<float> SilkLocalPosition => ((Vector3)Data.Values[0]).ToGeneric();
    public Quaternion<float> SilkLocalRotation => ((Quaternion)Data.Values[1]).ToGeneric();
    public Vector3D<float> SilkLocalScale => ((Vector3)Data.Values[2]).ToGeneric();

    private Matrix4X4<float> _cachedWorldTransform = Matrix4X4<float>.Identity;
    private bool _worldTransformValid;
    public bool IsWorldTransformUpToDate => _worldTransformValid;
    public ref readonly Matrix4X4<float> WorldTransform
    {
        get {
            if (_worldTransformValid) return ref _cachedWorldTransform;

            if (GameObject.Parent != null) {
                var absoluteScale = RszFieldCache.Transform.AbsoluteScaling.Get(Data);
                if (absoluteScale) {
                    // if true, parent scale does not affect self scale (position/rotation still affected) (see DD2 Env_1557 ladder)
                    _cachedWorldTransform = ComputeLocalTransformMatrix() * GameObject.Parent.WorldTransform;
                    Matrix4X4.Decompose(_cachedWorldTransform, out _, out var rot, out var pos);
                    _cachedWorldTransform = Matrix4X4.CreateScale<float>(SilkLocalScale) * Matrix4X4.CreateFromQuaternion(rot) * Matrix4X4.CreateTranslation(pos);
                } else {
                    _cachedWorldTransform = ComputeLocalTransformMatrix() * GameObject.Parent.WorldTransform;
                }
            } else if (GameObject.Folder != null) {
                _cachedWorldTransform = ComputeLocalTransformMatrix() * Matrix4X4.CreateTranslation<float>(GameObject.Folder.OffsetSilk);
            } else {
                _cachedWorldTransform = ComputeLocalTransformMatrix();
            }
            _worldTransformValid = true;
            foreach (var comp in GameObject.Components) {
                (comp as RenderableComponent)?.RecomputeWorldAABB();
            }
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
        return GetMatrixFromTransforms(SilkLocalPosition, SilkLocalRotation, SilkLocalScale);
    }

    public static Matrix4X4<float> GetMatrixFromTransforms(Vector3D<float> pos, Quaternion<float> rot, Vector3D<float> scale)
    {
        return Matrix4X4.CreateScale<float>(scale) * Matrix4X4.CreateFromQuaternion(rot) * Matrix4X4.CreateTranslation(pos);
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

    public void ResetLocalTransform()
    {
        LocalPosition = new Vector3();
        LocalRotation = Quaternion.Identity;
    }

    public void CopyFrom(Transform transform)
    {
        LocalPosition = transform.LocalPosition;
        LocalRotation = transform.LocalRotation;
        LocalScale = transform.LocalScale;
    }
}
