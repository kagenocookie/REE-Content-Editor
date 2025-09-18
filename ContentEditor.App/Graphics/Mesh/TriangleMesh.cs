using System.Numerics;
using System.Runtime.InteropServices;
using ReeLib.via;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class TriangleMesh : Mesh
{
    public TriangleMesh(GL gl, float[] vertexData, int[] indices) : base(gl)
    {
        VertexData = vertexData;
        Indices = indices;
        UpdateBuffers();
    }

    public TriangleMesh(GL gl, float[] vertexData, int[] indices, AABB bounds, bool includeTangents = false) : this(gl, vertexData, indices)
    {
        BoundingBox = bounds;
        Flags = (includeTangents ? MeshFlags.HasTangents : MeshFlags.None);
    }

    public TriangleMesh(GL gl, Assimp.Mesh sourceMesh) : base(gl)
    {
        Flags = (sourceMesh.HasTangentBasis ? MeshFlags.HasTangents : MeshFlags.None)
            | (sourceMesh.HasBones ? MeshFlags.HasBones : MeshFlags.None);

        Indices = sourceMesh.GetIndices().ToArray();

        var attrs = AttributeCount;
        VertexData = new float[Indices.Length * attrs];
        var uv0 = sourceMesh.TextureCoordinateChannels[0];
        var hasNormals = sourceMesh.HasNormals;
        var tangentsOffset = TangentAttributeOffset;
        var boneIndsOffset = BonesAttributeOffset;
        for (int index = 0; index < Indices.Length; ++index) {
            var vert = (int)Indices[index];
            VertexData[index * attrs + 0] = sourceMesh.Vertices[vert].X;
            VertexData[index * attrs + 1] = sourceMesh.Vertices[vert].Y;
            VertexData[index * attrs + 2] = sourceMesh.Vertices[vert].Z;
            VertexData[index * attrs + 3] = uv0[vert].X;
            VertexData[index * attrs + 4] = uv0[vert].Y;
            VertexData[index * attrs + 5] = !hasNormals ? 0 : sourceMesh.Normals[vert].X;
            VertexData[index * attrs + 6] = !hasNormals ? 0 : sourceMesh.Normals[vert].Y;
            VertexData[index * attrs + 7] = !hasNormals ? 1 : sourceMesh.Normals[vert].Z;
            VertexData[index * attrs + 8] = (float)index;
            if (HasTangents) {
                VertexData[index * attrs + tangentsOffset + 0] = sourceMesh.Tangents[vert].X;
                VertexData[index * attrs + tangentsOffset + 1] = sourceMesh.Tangents[vert].Y;
                VertexData[index * attrs + tangentsOffset + 2] = sourceMesh.Tangents[vert].Z;
            }
        }

        if (sourceMesh.HasBones) {
            // NOTE: consider importing bone data from .mesh format directly instead of using assimp as an intermediate, this feels horrible
            for (int boneIndex = 0; boneIndex < sourceMesh.Bones.Count; boneIndex++) {
                var bone = sourceMesh.Bones[boneIndex];

                foreach (var vw in bone.VertexWeights) {
                    var vert = vw.VertexID * attrs + boneIndsOffset;
                    ref var weights = ref MemoryMarshal.Cast<float, Vector4D<float>>(VertexData.AsSpan(vert + 4, 4))[0];
                    if (weights.X == 0) {
                        weights.X = vw.Weight;
                        VertexData[vert + 0] = BitConverter.Int32BitsToSingle(boneIndex);
                    } else if (weights.Y == 0) {
                        weights.Y = vw.Weight;
                        VertexData[vert + 1] = BitConverter.Int32BitsToSingle(boneIndex);
                    } else if (weights.Z == 0) {
                        weights.Z = vw.Weight;
                        VertexData[vert + 2] = BitConverter.Int32BitsToSingle(boneIndex);
                    } else if (weights[3] == 0) {
                        VertexData[vert + 3] = BitConverter.Int32BitsToSingle(boneIndex);
                        // normalize weights - if the mesh has more than 4 weights per bone, ignore any extra bones
                        // this simplifies the shader code and is usually visually neglible
                        // the weights aren't guaranteed to be in order here so we may have non-negligible errors in some cases, but we'll see
                        // for fully accurate animations, blender or ingame exists
                        weights.W = vw.Weight;
                        weights /= (weights.X + weights.Y + weights.Z + weights.W);
                    }
                }
            }
        }

        BoundingBox = new AABB(sourceMesh.BoundingBox.Min, sourceMesh.BoundingBox.Max);
        UpdateBuffers();
    }

    public override string ToString() => $"{VAO} {VBO} indices: {Indices.Length}";
}