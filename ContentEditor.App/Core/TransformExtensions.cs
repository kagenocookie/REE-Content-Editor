using System.Numerics;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

public static class TransformExtensions
{
    public static Quaternion<float> ToSilkNet(this Quaternion quat) => new Quaternion<float>(quat.X, quat.Y, quat.Z, quat.W);
    public static Vector3D<float> ToSilkNet(this Vector3 vec) => new Vector3D<float>(vec.X, vec.Y, vec.Z);

    public static Matrix4X4<float> ToGeneric(this mat4 mat) => new Matrix4X4<float>(
        mat.m00, mat.m01, mat.m02, mat.m03,
        mat.m10, mat.m11, mat.m12, mat.m13,
        mat.m20, mat.m21, mat.m22, mat.m23,
        mat.m30, mat.m31, mat.m32, mat.m33
    );

    public static Quaternion<float> ToSilkNetQuaternion(this Vector4 vec) => new Quaternion<float>(vec.X, vec.Y, vec.Z, vec.W);
    public static Vector3D<float> ToSilkNetVec3(this Vector4 vec) => new Vector3D<float>(vec.X, vec.Y, vec.Z);

    public static AABB ToWorldBounds(this AABB local, Matrix4x4 world)
    {
        var pt = Vector3.Transform(local.minpos, world);
        var aabb = new AABB(pt, pt)
            .Extend(Vector3.Transform(local.maxpos, world))
            .Extend(Vector3.Transform(new Vector3(local.minpos.X, local.minpos.Y, local.maxpos.Y), world))
            .Extend(Vector3.Transform(new Vector3(local.minpos.X, local.maxpos.Y, local.minpos.Y), world))
            .Extend(Vector3.Transform(new Vector3(local.minpos.X, local.maxpos.Y, local.maxpos.Y), world))
            .Extend(Vector3.Transform(new Vector3(local.maxpos.X, local.minpos.Y, local.maxpos.Y), world))
            .Extend(Vector3.Transform(new Vector3(local.maxpos.X, local.maxpos.Y, local.minpos.Y), world))
            .Extend(Vector3.Transform(new Vector3(local.maxpos.X, local.maxpos.Y, local.maxpos.Y), world));

        return aabb;
    }

    public static AABB ToWorldBounds(this AABB local, Matrix4X4<float> world) => ToWorldBounds(local, world.ToSystem());

    public static Matrix4x4 ToMatrix(this in ReeLib.via.Transform xform)
    {
        return Matrix4x4.CreateScale(xform.scale) * Matrix4x4.CreateFromQuaternion(xform.rot) * Matrix4x4.CreateTranslation(xform.pos);
    }

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

    public static Vector3 ProjectOnPlane(this Vector3 vector, Vector3 planeNormal)
    {
        float len = planeNormal.LengthSquared();
        if (len < 0.0001f) {
            return vector;
        }

        float dot = Vector3.Dot(vector, planeNormal);
        return new Vector3(
            vector.X - planeNormal.X * dot / len,
            vector.Y - planeNormal.Y * dot / len,
            vector.Z - planeNormal.Z * dot / len);
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
