using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using ReeLib;
using ReeLib.Aimp;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class LineMesh : Mesh
{
    public LineMesh(GL gl, params Vector3[] points) : base(gl)
    {
        MeshType = PrimitiveType.Lines;
        layout = MeshLayout.PositionOnly;
        VertexData = new float[points.Length * layout.VertexSize];
        var pointData = MemoryMarshal.Cast<float, Vector3>(VertexData.AsSpan());
        Indices = new int[points.Length];
        BoundingBox = AABB.MaxMin;
        for (int index = 0; index < points.Length; ++index) {
            var point = points[index];
            pointData[index] = point;
            Indices[index] = index;
            BoundingBox = BoundingBox.Extend(point);
        }
        UpdateBuffers();
    }

    public LineMesh(GL gl, MeshFile sourceMesh, ReeLib.Mesh.Submesh submesh) : base(gl)
    {
        MeshType = PrimitiveType.Lines;
        layout = MeshLayout.PositionOnly;
        PrepareMeshVertexBufferData(sourceMesh, submesh);
        UpdateBuffers();
    }

    public LineMesh(Mesh sourceTriangleMesh)
    {
        Debug.Assert(sourceTriangleMesh.MeshType == PrimitiveType.Triangles);
        MeshType = PrimitiveType.Lines;
        BoundingBox = sourceTriangleMesh.BoundingBox;
        var vertCount = sourceTriangleMesh.Indices.Length;
        var sourceData = sourceTriangleMesh.VertexData;
        Indices = new int[vertCount * 4 / 3]; // each triangle counts as 4 indices ABBC
        var sourceAttrCount = sourceTriangleMesh.layout.VertexSize;
        if (sourceTriangleMesh.HasColor) {
            layout = MeshLayout.ColoredPositions;
            VertexData = new float[Indices.Length * layout.VertexSize];
            var sourceColorAttrOffset = sourceTriangleMesh.layout.AttributeIndexOffsets[MeshLayout.Index_Color];
            var pointData = MemoryMarshal.Cast<float, Vector4>(VertexData.AsSpan());
            var myIndices = 0;
            for (int i = 0; i < vertCount; ++i) {
                pointData[myIndices++] = new Vector4(
                    sourceData[i * sourceAttrCount + 0],
                    sourceData[i * sourceAttrCount + 1],
                    sourceData[i * sourceAttrCount + 2],
                    sourceData[i * sourceAttrCount + sourceColorAttrOffset]
                );
                i++;
                pointData[myIndices++] = new Vector4(
                    sourceData[i * sourceAttrCount + 0],
                    sourceData[i * sourceAttrCount + 1],
                    sourceData[i * sourceAttrCount + 2],
                    sourceData[i * sourceAttrCount + sourceColorAttrOffset]
                );
                pointData[myIndices++] = new Vector4(
                    sourceData[i * sourceAttrCount + 0],
                    sourceData[i * sourceAttrCount + 1],
                    sourceData[i * sourceAttrCount + 2],
                    sourceData[i * sourceAttrCount + sourceColorAttrOffset]
                );
                i++;
                pointData[myIndices++] = new Vector4(
                    sourceData[i * sourceAttrCount + 0],
                    sourceData[i * sourceAttrCount + 1],
                    sourceData[i * sourceAttrCount + 2],
                    sourceData[i * sourceAttrCount + sourceColorAttrOffset]
                );
            }
        } else {
            layout = MeshLayout.PositionOnly;
            VertexData = new float[Indices.Length * layout.VertexSize];
            var pointData = MemoryMarshal.Cast<float, Vector3>(VertexData.AsSpan());
            var myIndices = 0;
            for (int i = 0; i < vertCount; ++i) {
                pointData[myIndices++] = new Vector3(
                    sourceData[i * sourceAttrCount + 0],
                    sourceData[i * sourceAttrCount + 1],
                    sourceData[i * sourceAttrCount + 2]
                );
                i++;
                pointData[myIndices++] = new Vector3(
                    sourceData[i * sourceAttrCount + 0],
                    sourceData[i * sourceAttrCount + 1],
                    sourceData[i * sourceAttrCount + 2]
                );
                pointData[myIndices++] = new Vector3(
                    sourceData[i * sourceAttrCount + 0],
                    sourceData[i * sourceAttrCount + 1],
                    sourceData[i * sourceAttrCount + 2]
                );
                i++;
                pointData[myIndices++] = new Vector3(
                    sourceData[i * sourceAttrCount + 0],
                    sourceData[i * sourceAttrCount + 1],
                    sourceData[i * sourceAttrCount + 2]
                );
            }
        }
    }

    public LineMesh(AimpFile file, ContentGroupContainer container, ContentGroupTriangle data)
    {
        MeshType = PrimitiveType.Lines;
        layout = MeshLayout.ColoredPositions;
        Indices = new int[data.NodeCount * 4]; // ABBC
        VertexData = new float[Indices.Length * layout.VertexSize];
        var pointData = MemoryMarshal.Cast<float, Vector4>(VertexData.AsSpan());

        BoundingBox = container.bounds;
        var nodes = container.NodeInfo.Nodes;
        var verts = container.Vertices;
        var triangles = data.Nodes;

        var index = 0;
        for (int i = 0; i < data.NodeCount; ++i) {
            var tri = triangles[i];
            var node = nodes[i];
            var a = verts[tri.index1].Vector3;
            var b = verts[tri.index2].Vector3;
            var c = verts[tri.index3].Vector3;
            var color = BitConverter.Int32BitsToSingle((int)node.GetColor(file).rgba);
            pointData[index++] = new Vector4(a, color);
            pointData[index++] = new Vector4(b, color);
            pointData[index++] = new Vector4(b, color);
            pointData[index++] = new Vector4(c, color);
        }
    }

    public LineMesh(AimpFile file, ContentGroupContainer container, ContentGroupMapBoundary data, int nodeOffset)
    {
        MeshType = PrimitiveType.Lines;
        layout = MeshLayout.ColoredPositions;
        var polygons = data.Nodes;
        var nodes = container.NodeInfo.Nodes;
        var verts = container.Vertices;
        BoundingBox = container.bounds;

        Indices = new int[polygons.Count * 16];
        VertexData = new float[Indices.Length * layout.VertexSize];

        var pointData = MemoryMarshal.Cast<float, Vector4>(VertexData.AsSpan());

        var index = 0;
        Span<Vector3> pts = stackalloc Vector3[8];
        for (int i = 0; i < data.NodeCount; ++i) {
            var node = nodes[nodeOffset + i];
            var poly = polygons[i];
            var color = BitConverter.Int32BitsToSingle((int)node.GetColor(file).rgba);
            for (int k = 0; k < 8; ++k) pts[k] = verts[poly.indices[k]].Vector3;

            pointData[index++] = new Vector4(pts[0], color);
            pointData[index++] = new Vector4(pts[1], color);
            pointData[index++] = new Vector4(pts[1], color);
            pointData[index++] = new Vector4(pts[2], color);
            pointData[index++] = new Vector4(pts[2], color);
            pointData[index++] = new Vector4(pts[3], color);
            pointData[index++] = new Vector4(pts[3], color);
            pointData[index++] = new Vector4(pts[0], color);

            pointData[index++] = new Vector4(pts[4 + 0], color);
            pointData[index++] = new Vector4(pts[4 + 1], color);
            pointData[index++] = new Vector4(pts[4 + 1], color);
            pointData[index++] = new Vector4(pts[4 + 2], color);
            pointData[index++] = new Vector4(pts[4 + 2], color);
            pointData[index++] = new Vector4(pts[4 + 3], color);
            pointData[index++] = new Vector4(pts[4 + 3], color);
            pointData[index++] = new Vector4(pts[4 + 0], color);
        }
    }

    public LineMesh(AimpFile file, ContentGroupContainer container)
    {
        MeshType = PrimitiveType.Lines;
        layout = MeshLayout.ColoredPositions;
        var nodes = container.NodeInfo.Nodes;
        var linkCount = nodes.Sum(n => n.Links.Count);
        var effectiveNodeIndices = container.NodeInfo.EffectiveNodeIndices;

        Indices = new int[linkCount * 2];
        VertexData = new float[Indices.Length * layout.VertexSize];

        var pointData = MemoryMarshal.Cast<float, Vector4>(VertexData.AsSpan());

        int index = 0;
        for (int i = 0; i < nodes.Count; i++) {
            var nodeInfo = nodes[i];
            foreach (var link in nodeInfo.Links) {
                var n1 = nodes[effectiveNodeIndices[link.sourceNodeIndex]];
                var n2 = nodes[effectiveNodeIndices[link.targetNodeIndex]];

                var p1 = container.NodeOrigins[n1.index];
                var p2 = container.NodeOrigins[n2.index];

                // ignore triangle based links here - the web of links becomes an unsightly mess
                // and yes, we've allocated more data than we needed in that case, shouldn't be an issue
                if (container.contents[n1.groupIndex] is ContentGroupTriangle or ContentGroupPolygon) continue;

                pointData[index] = new Vector4(p1, BitConverter.Int32BitsToSingle((int)n1.GetColor(file).rgba));
                Indices[index] = index;
                index++;
                pointData[index] = new Vector4(p2, BitConverter.Int32BitsToSingle((int)n2.GetColor(file).rgba));
                Indices[index] = index;
                index++;
            }
        }
    }

    private void PrepareMeshVertexBufferData(MeshFile sourceMesh, ReeLib.Mesh.Submesh submesh)
    {
        // note: untested
        var integerIndices = sourceMesh.MeshData?.integerFaces ?? false;
        var lineVertCount = submesh.vertCount * 4 / 3;

        var indicesShort = !integerIndices ? submesh.Indices : default;
        var indicesInt = integerIndices ? submesh.IntegerIndices : default;
        var meshVerts = submesh.Positions;

        var triangles = submesh.indicesCount / 3;
        Indices = new int[triangles * 4]; // each triangle counts as 4 indices ABBC
        VertexData = new float[Indices.Length * 3];
        var pointData = MemoryMarshal.Cast<float, Vector3>(VertexData.AsSpan());
        BoundingBox = AABB.MaxMin;
        var index = 0;
        for (int faceIndex = 0; faceIndex < triangles; ++faceIndex) {
            var a = integerIndices ? indicesInt[faceIndex * 3 + 0] : indicesShort[faceIndex * 3 + 0];
            var b = integerIndices ? indicesInt[faceIndex * 3 + 1] : indicesShort[faceIndex * 3 + 1];
            var c = integerIndices ? indicesInt[faceIndex * 3 + 2] : indicesShort[faceIndex * 3 + 2];
            var pa = meshVerts[a];
            var pb = meshVerts[b];
            var pc = meshVerts[c];
            pointData[index] = pa;
            Indices[index++] = a;
            pointData[index] = pb;
            Indices[index++] = b;
            pointData[index] = pb;
            Indices[index++] = b;
            pointData[index] = pc;
            Indices[index++] = c;
            BoundingBox = BoundingBox.Extend(pa).Extend(pb).Extend(pc);
        }
    }

    public override string ToString() => $"{VAO} {VBO} indices: {Indices.Length}";
}