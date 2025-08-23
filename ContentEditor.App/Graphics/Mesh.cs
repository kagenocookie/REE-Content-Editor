using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

#pragma warning disable CS8618

public class Mesh : IDisposable
{
    public Mesh(GL gl, float[] vertexData, uint[] indices, List<Texture> textures)
    {
        GL = gl;
        VertexData = vertexData;
        Indices = indices;
        SetupMesh();
    }

    public Mesh(GL gl, Assimp.Mesh sourceMesh)
    {
        GL = gl;
        VertexData = new float[sourceMesh.VertexCount * 5];
        var uv0 = sourceMesh.TextureCoordinateChannels[0];
        for (int i = 0; i < sourceMesh.Vertices.Count; ++i) {
            VertexData[i * 5 + 0] = sourceMesh.Vertices[i].X;
            VertexData[i * 5 + 1] = sourceMesh.Vertices[i].Y;
            VertexData[i * 5 + 2] = sourceMesh.Vertices[i].Z;
            VertexData[i * 5 + 3] = uv0[i].X;
            VertexData[i * 5 + 4] = uv0[i].Y;
        }

        Indices = sourceMesh.GetUnsignedIndices().ToArray();
        BoundingBox = new AABB(sourceMesh.BoundingBox.Min, sourceMesh.BoundingBox.Max);
        SetupMesh();
    }

    public float[] VertexData { get; private set; }
    public uint[] Indices { get; private set; }
    public VertexArrayObject<float, uint> VAO { get; set; }
    public BufferObject<float> VBO { get; set; }
    public BufferObject<uint> EBO { get; set; }
    public AABB BoundingBox { get; set; }
    public GL GL { get; }

    public unsafe void SetupMesh()
    {
        VBO = new BufferObject<float>(GL, VertexData, BufferTargetARB.ArrayBuffer);
        EBO = new BufferObject<uint>(GL, Indices, BufferTargetARB.ElementArrayBuffer);
        VAO = new VertexArrayObject<float, uint>(GL, VBO, EBO);
        // vertex position
        VAO.VertexAttributePointer(0, 3, VertexAttribPointerType.Float, 5, 0);
        // vertex UV0
        VAO.VertexAttributePointer(1, 2, VertexAttribPointerType.Float, 5, 3);
    }

    public void Bind()
    {
        VAO.Bind();
        VBO.Bind();
        EBO.Bind();
        // vertex position
        VAO.VertexAttributePointer(0, 3, VertexAttribPointerType.Float, 5, 0);
        // vertex UV0
        VAO.VertexAttributePointer(1, 2, VertexAttribPointerType.Float, 5, 3);
    }

    public void Dispose()
    {
        VAO.Dispose();
        VBO.Dispose();
        EBO.Dispose();
    }

    public override string ToString() => $"{VAO} {VBO} indices: {Indices.Length}";
}