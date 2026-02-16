using System.Numerics;
using ContentPatcher;
using ReeLib;

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

    public Vector3 Position {
        get => WorldTransform.Translation;
    }

    public Quaternion Rotation {
        get => Matrix4x4.Decompose(WorldTransform, out _, out var rot, out _) ? rot : Quaternion.Identity;
    }

    public Vector3 Scale {
        get => Matrix4x4.Decompose(WorldTransform, out var scale, out _, out _) ? scale : Vector3.One;
    }

    public Vector3 Forward => Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, Rotation));
    public Vector3 Right => Vector3.Normalize(Vector3.Transform(Vector3.UnitX, Rotation));
    public Vector3 Up => Vector3.Normalize(Vector3.Transform(Vector3.UnitY, Rotation));

    private Matrix4x4 _cachedWorldTransform = Matrix4x4.Identity;
    private bool _worldTransformValid;
    public bool IsWorldTransformUpToDate => _worldTransformValid;
    public ref readonly Matrix4x4 WorldTransform
    {
        get {
            if (_worldTransformValid) return ref _cachedWorldTransform;

            if (GameObject.Parent != null) {
                var absoluteScale = RszFieldCache.Transform.AbsoluteScaling.Get(Data);
                if (absoluteScale) {
                    // if true, parent scale does not affect self scale (position/rotation still affected) (see DD2 Env_1557 ladder)
                    _cachedWorldTransform = ComputeLocalTransformMatrix() * GameObject.Parent.WorldTransform;
                    Matrix4x4.Decompose(_cachedWorldTransform, out _, out var rot, out var pos);
                    _cachedWorldTransform = Matrix4x4.CreateScale(LocalScale) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
                } else {
                    _cachedWorldTransform = ComputeLocalTransformMatrix() * GameObject.Parent.WorldTransform;
                }
            } else if (GameObject.Folder != null) {
                _cachedWorldTransform = ComputeLocalTransformMatrix() * Matrix4x4.CreateTranslation(GameObject.Folder.Offset.AsVector3);
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

    internal void InvalidateTransformSelf()
    {
        _worldTransformValid = false;
    }

    public Matrix4x4 ComputeLocalTransformMatrix()
    {
        return GetMatrixFromTransforms(LocalPosition, LocalRotation, LocalScale);
    }

    public static Matrix4x4 GetMatrixFromTransforms(Vector3 pos, Quaternion rot, Vector3 scale)
    {
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
    }

    public void Translate(Vector3 offset)
    {
        LocalPosition += offset;
    }

    public void TranslateForwardAligned(Vector3 offset)
    {
        LocalPosition += Vector3.Transform(offset, LocalRotation);
    }

    public void ComponentInit()
    {
        LocalRotation = Quaternion.Identity;
        LocalScale = Vector3.One;
    }

    public void Rotate(Quaternion quaternion)
    {
        LocalRotation = LocalRotation * quaternion;
    }

    public void LookAt(Vector3 target)
    {
        LookAt(target, new Vector3(0, 1, 0));
    }

    public void LookAt(Vector3 target, Vector3 up)
    {
        LocalRotation = LocalPosition.CreateLookAtQuaternion(target, up);
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

    public void SetGlobalTransform(Matrix4x4 matrix)
    {
        Matrix4x4.Decompose(matrix, out var scale, out var rot, out var pos);
        SetGlobalTransform(pos, rot, scale);
    }

    public void SetGlobalTransform(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        if (GameObject.Parent == null) {
            LocalRotation = rotation;
            LocalScale = scale;
            LocalPosition = position;
        } else {
            var parent = GameObject.Parent.Transform;
            var parentScale = parent.Scale;
            var invParentRot = Quaternion.Inverse(parent.Rotation);

            var relativePos = position - parent.Position;
            relativePos = Vector3.Transform(relativePos, invParentRot);
            relativePos /= parentScale;

            LocalPosition = relativePos;
            LocalRotation = invParentRot * rotation;
            LocalScale = scale / parentScale;
        }
    }
}
