using System.Numerics;
using ContentEditor.Core;
using ReeLib;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

public enum CameraProjection
{
    Perspective,
    Orthographic
}

[RszComponentClass("via.Camera")]
public sealed class Camera : Component, IConstructorComponent, IFixedClassnameComponent
{
    static string IFixedClassnameComponent.Classname => "via.Camera";

    public float FieldOfView { get; set; } = 80.0f * MathF.PI / 180.0f;
    public float OrthoSize { get; set; } = 0.5f;
    public float NearPlane { get; set; } = 0.1f;
    public float FarPlane { get; set; } = 8096.0f;

    public Matrix4X4<float> ViewMatrix { get; private set; } = Matrix4X4<float>.Identity;
    public Matrix4X4<float> ProjectionMatrix { get; private set; } = Matrix4X4<float>.Identity;
    public Matrix4X4<float> ViewProjectionMatrix { get; private set; } = Matrix4X4<float>.Identity;
    public CameraProjection ProjectionMode { get; set; }
    private Frustum _frustum;

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

    /// <summary>
    /// Transforms a world space position into viewport screen space. Returns Vector2(float.MaxValue) if the point is behind the camera or outside of the viewport when limitToViewport == true.
    /// </summary>
    public Vector2 WorldToViewportXYPosition(Vector3 worldPosition, bool limitToViewport = true)
    {
        if (Scene == null) return new();

        var size = Scene!.RenderContext.RenderOutputSize;
        var vec = Project(ViewProjectionMatrix, size, worldPosition);
        if (limitToViewport) {
            if (vec.W < 0) return new Vector2(float.MaxValue);
            if (vec.X < 0 || vec.Y < 0 || vec.X > size.X || vec.Y > size.Y) return new Vector2(float.MaxValue);
        }
        return new Vector2(vec.X, vec.Y);
    }

    /// <summary>
    /// Transforms a world space position into viewport screen space. Returns Vector2(float.MaxValue) if the point is behind the camera or outside of the viewport when limitToViewport == true.
    /// </summary>
    public Vector2 WorldToScreenPosition(Vector3 worldPosition, bool limitToViewport = true)
    {
        if (Scene == null) return new();

        var viewportPosition = WorldToViewportXYPosition(worldPosition, limitToViewport);
        var offset = Scene.MouseHandler?.viewportOffset ?? default;
        return viewportPosition + offset;
    }

    /// <summary>
    /// Transforms a screen space position into world space.
    /// </summary>
    public Vector3 ScreenToWorldPosition(Vector3 screenPos)
        => ViewportToWorldPosition(screenPos - (Scene?.MouseHandler?.viewportOffset.ToVec3() ?? new()));

    /// <summary>
    /// Transforms a screen space position into world space.
    /// </summary>
    public Vector3 ViewportToWorldPosition(Vector3 viewportPos)
    {
        if (Scene == null) return new();

        var size = Scene.RenderContext.RenderOutputSize;
        var vec = Unproject(ViewProjectionMatrix, size, viewportPos);
        return vec;
    }

    /// <summary>
    /// Transforms a screen space position into world space, ensuring that the resulting value is at the same distance from the screen as referencePosition.
    /// </summary>
    public Vector3 ScreenToWorldPositionReproject(Vector2 screenPos, Vector3 referencePosition)
    {
        if (Scene == null) return new();

        var offset = (Scene.MouseHandler?.viewportOffset ?? new());
        var viewportPos = screenPos - offset;
        var size = Scene.RenderContext.RenderOutputSize;
        // recalculate the reference Z / depth so we get accurate distance
        var orgDepth = Project(ViewProjectionMatrix, size, referencePosition).Z;
        var pos = Unproject(ViewProjectionMatrix, size, new Vector3(viewportPos.X, viewportPos.Y, orgDepth));
        return pos;
    }

    private static Vector4 Project(in Matrix4X4<float> viewProjection, Vector2 viewportSize, Vector3 position)
    {
        Vector4 vec = new Vector4(position, 1);
        vec = Vector4.Transform(vec, viewProjection.ToSystem());

        if (vec.W > float.Epsilon || vec.W < float.Epsilon)
        {
            vec.X /= vec.W;
            vec.Y /= vec.W;
            vec.Z /= vec.W;
        }
        return new Vector4(
            (vec.X + 1) *  0.5f * viewportSize.X,
            (vec.Y - 1) * -0.5f * viewportSize.Y,
            vec.Z,
            vec.W
        );
    }

    private static Vector3 Unproject(in Matrix4X4<float> viewProjection, Vector2 viewportSize, Vector3 position)
    {
        Vector4 vec;
        vec.X =  2.0f * position.X / (float)viewportSize.X - 1;
        vec.Y = -2.0f * position.Y / (float)viewportSize.Y + 1;
        vec.Z = position.Z;
        vec.W = 1.0f;

        Matrix4X4.Invert(viewProjection, out var inverted);
        vec = Vector4.Transform(vec, inverted.ToSystem());

        if (vec.W > float.Epsilon || vec.W < float.Epsilon)
        {
            vec.X /= vec.W;
            vec.Y /= vec.W;
            vec.Z /= vec.W;
        }
        return vec.ToVec3();
    }

    public void Update(Vector2 size)
    {
        if (Matrix4X4.Invert(GameObject.WorldTransform, out var inverted)) {
            ViewMatrix = inverted;
        } else {
            ViewMatrix = Matrix4X4<float>.Identity;
        }

        if (ProjectionMode == CameraProjection.Perspective) {
            ProjectionMatrix = Matrix4X4.CreatePerspectiveFieldOfView(FieldOfView, size.X / size.Y, NearPlane, FarPlane);
        } else {
            float halfW = OrthoSize * size.X / size.Y * 0.5f;
            float halfH = OrthoSize * 0.5f;
            ProjectionMatrix = Matrix4X4.CreateOrthographicOffCenter(-halfW, halfW, -halfH, halfH, NearPlane, FarPlane);
        }
        ViewProjectionMatrix = ViewMatrix * ProjectionMatrix;
        var p = ViewProjectionMatrix;
        _frustum = new Frustum() {
            plane0 = new ReeLib.via.Plane(p.M14 + p.M11, p.M24 + p.M21, p.M34 + p.M31, p.M44 + p.M41).Normalize(),
            plane1 = new ReeLib.via.Plane(p.M14 - p.M11, p.M24 - p.M21, p.M34 - p.M31, p.M44 - p.M41).Normalize(),
            plane2 = new ReeLib.via.Plane(p.M14 + p.M12, p.M24 + p.M22, p.M34 + p.M32, p.M44 + p.M42).Normalize(),
            plane3 = new ReeLib.via.Plane(p.M14 - p.M12, p.M24 - p.M22, p.M34 - p.M32, p.M44 - p.M42).Normalize(),
            plane4 = new ReeLib.via.Plane(p.M14 + p.M13, p.M24 + p.M23, p.M34 + p.M33, p.M44 + p.M43).Normalize(),
            plane5 = new ReeLib.via.Plane(p.M14 - p.M13, p.M24 - p.M23, p.M34 - p.M33, p.M44 - p.M43).Normalize(),
        };
    }

    /// <summary>
    /// Check frustum culling for the given component based on its world space bounds.
    /// </summary>
    public bool IsVisible(RenderableComponent comp)
    {
        // animated meshes would need either proper per-bone calculation or have the animator give us the animated bounds
        // just don't cull these at all for now
        if (!comp.IsStatic) return true;

        return IsVisible(comp.WorldSpaceBounds);
    }

    /// <summary>
    /// Check frustum culling for the given world space AABB bounds.
    /// </summary>
    public bool IsVisible(AABB b)
    {
        var size = b.Size;
        ref readonly var frustum = ref _frustum;

        var p0 = b.minpos;
        var p1 = new Vector3(b.minpos.X, b.minpos.Y, b.maxpos.Z);
        var p2 = new Vector3(b.minpos.X, b.maxpos.Y, b.minpos.Z);
        var p3 = new Vector3(b.minpos.X, b.maxpos.Y, b.maxpos.Z);
        var p4 = new Vector3(b.maxpos.X, b.minpos.Y, b.minpos.Z);
        var p5 = new Vector3(b.maxpos.X, b.minpos.Y, b.maxpos.Z);
        var p6 = new Vector3(b.maxpos.X, b.maxpos.Y, b.minpos.Z);
        var p7 = b.maxpos;

        for (int i = 0; i < 6; ++i) {
            var p = frustum[i];
            if (p.IsInFront(p0) || p.IsInFront(p1) || p.IsInFront(p2) || p.IsInFront(p3) || p.IsInFront(p4) || p.IsInFront(p5) || p.IsInFront(p6) || p.IsInFront(p7)) {
                continue;
            }

            return false;
        }

        return true;
    }

    public Camera(GameObject gameObject, RszInstance data) : base(gameObject, data)
    {
    }

}
