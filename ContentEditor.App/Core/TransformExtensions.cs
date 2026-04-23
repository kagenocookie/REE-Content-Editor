using System.Numerics;
using ReeLib.via;

namespace ContentEditor.App;

public static class TransformExtensions
{
    public const float Rad2Deg = 180f / MathF.PI;
    public const float Deg2Rad = MathF.PI / 180f;

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

    public static Matrix4x4 ToMatrix(this in ReeLib.via.Transform xform)
    {
        return Matrix4x4.CreateScale(xform.scale) * Matrix4x4.CreateFromQuaternion(xform.rot) * Matrix4x4.CreateTranslation(xform.pos);
    }

    public static float AngleBetween(Vector3 v1, Vector3 v2)
    {
        var cross = Vector3.Cross(v1, v2);
        var dot = Vector3.Dot(v1, v2);
        return MathF.Atan2(cross.Length(), dot);
    }

    public static float SignedAngleBetween(Vector3 v1, Vector3 v2, Vector3 axis)
    {
        var cross = Vector3.Cross(v1, v2);
        var dot = Vector3.Dot(v1, v2);
        var angle = MathF.Atan2(cross.Length(), dot);
        return (Vector3.Dot(axis, cross) < 0) ? -angle : angle;
    }

    public static Quaternion CreateLookAtQuaternion(this Vector3 from, Vector3 to, Vector3 up)
    {
        var fwd = Vector3.Normalize(from - to);
        var right = Vector3.Normalize(Vector3.Cross(up, fwd));
        var newUp = Vector3.Cross(fwd, right);
        var lookAt = new Matrix4x4(
            right.X, right.Y, right.Z, 0,
            newUp.X, newUp.Y, newUp.Z, 0,
            fwd.X, fwd.Y, fwd.Z, 0,
            0, 0, 0, 1
        );
        return Quaternion.CreateFromRotationMatrix(lookAt);
    }

    public static Quaternion CreateFromToQuaternion(this Vector3 from, Vector3 to)
    {
        var vector = Vector3.Cross(from, to);
        var dot = Vector3.Dot(from, to);
        if (dot < -0.99999f) {
            return new Quaternion(1, 0, 0, 0);
        } else {
            var num2 = MathF.Sqrt((1f + dot) * 2f);
            var num3 = 1f / num2;
            return new Quaternion(vector.X * num3, vector.Y * num3, vector.Z * num3, num2 * 0.5f);
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
    /// Returns the euler angles of the given quaternion (order: pitch, yaw, roll) in degrees.
    /// </summary>
    public static Vector3 ToEuler(this Quaternion q)
    {
        // https://stackoverflow.com/a/12122899/4721768

        float sqw = q.W * q.W;
        float sqx = q.X * q.X;
        float sqy = q.Y * q.Y;
        float sqz = q.Z * q.Z;
        float unit = sqx + sqy + sqz + sqw; // if normalised is one, otherwise is correction factor
        float test = q.X * q.W - q.Y * q.Z;
        Vector3 v;

        if (test > 0.4995f * unit) { // singularity at north pole
            v.Y = 2f * MathF.Atan2(q.Y, q.X);
            v.X = MathF.PI / 2;
            v.Z = 0;
            return v * Rad2Deg;
        }
        if (test < -0.4995f * unit) { // singularity at south pole
            v.Y = -2f * MathF.Atan2(q.Y, q.X);
            v.X = -MathF.PI / 2;
            v.Z = 0;
            return v * Rad2Deg;
        }

        v.Y = (float)Math.Atan2(2f * q.W * q.Y + 2f * q.Z * q.X, 1 - 2f * (q.X * q.X + q.Y * q.Y));     // Yaw
        v.X = (float)Math.Asin(2f * (q.W * q.X - q.Y * q.Z));                             // Pitch
        v.Z = (float)Math.Atan2(2f * q.W * q.Z + 2f * q.X * q.Y, 1 - 2f * (q.Z * q.Z + q.X * q.X));      // Roll
        return v * Rad2Deg;

    }
}
