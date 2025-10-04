using System.Globalization;
using System.Numerics;
using Assimp;
using ContentEditor;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mesh;
using ReeLib.Mot;
using ReeLib.Motlist;
using ReeLib.via;

namespace ContentEditor.App.FileLoaders;

public partial class AssimpMeshResource : IResourceFile
{
    private static readonly Matrix4x4 Matrix_To_Gltf = Matrix4x4.CreateScale(1, 1, 1); // RE uses -Z forward, gltf wants +Z forward
    private static readonly Matrix4x4 Matrix_From_Gltf = Matrix4x4.CreateScale(1, 1, 1); // inverse of Matrix_To_Gltf is identical, just leaving it separate for clarity

    private static Matrix4x4 ConvertMatrixToGltf(Matrix4x4 mat)
    {
        mat = Matrix4x4.Multiply(Matrix_To_Gltf, mat);
        return Matrix4x4.Transpose(mat);
        // return Matrix4x4.Transpose(Matrix4x4.Multiply(mat, Matrix_To_Gltf));
        // return Matrix4x4.Multiply(Matrix_To_Gltf, Matrix4x4.Transpose(mat));
        // return Matrix4x4.Transpose(mat);
    }

    private static Matrix4x4 ConvertMatrixFromGltf(Matrix4x4 mat)
    {
        return Matrix4x4.Multiply(Matrix_From_Gltf, Matrix4x4.Transpose(mat));
        // return Matrix4x4.Transpose(mat);
    }

    private static Vector3 ConvertVector3ToGltf(Vector3 vec)
    {
        return Vector3.Transform(vec, Matrix_To_Gltf);
    }

    private static Vector3 ConvertVector3FromGltf(Vector3 vec)
    {
        return Vector3.Transform(vec, Matrix_From_Gltf);
    }
    private static Vector3 ConvertVector3NormalToGltf(Vector3 vec)
    {
        return Vector3.TransformNormal(vec, Matrix_To_Gltf);
    }
    private static Vector3 ConvertVector3NormalFromGltf(Vector3 vec)
    {
        return Vector3.TransformNormal(vec, Matrix_From_Gltf);
    }
    private static Quaternion ConvertQuaternionToGltf(Quaternion quat)
    {
        return quat;
        // return new Quaternion(quat.W, quat.X, quat.Y, quat.Z);
    }

    private static Quaternion ConvertQuaternionFromGltf(Quaternion quat)
    {
        return quat;
        // return new Quaternion(quat.Y, quat.Z, quat.W, quat.X);
    }
}
