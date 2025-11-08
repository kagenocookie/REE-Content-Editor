using System.Numerics;
using ReeLib;

namespace ContentEditor.App.Graphics;

public class HeightmapMesh : Mesh
{
    internal HeightmapMesh() : base()
    {
        layout = MeshLayout.BasicTriangleMeshNoIndex;
    }

    public void Update(float[][] heights, Vector3 min, Vector3 max, Vector2 uvWrapCount)
    {
        var pointsX = heights[0].Length;
        var pointsZ = heights.Length;
        EnsureBufferSize(pointsX, pointsZ);

        int ind = 0;
        var stepX = (max.X - min.X) / (pointsX - 1);
        var rangeY = (max.Y - min.Y);
        var stepZ = (max.Z - min.Z) / (pointsZ - 1);

        for (int x = 1; x < pointsX; ++x) {
            for (int z = 1; z < pointsZ; ++z) {
                var x1 = x - 1;
                var z1 = z - 1;
                var hTL = heights[z1][x1];
                var hTR = heights[z1][x];
                var hBL = heights[z][x1];
                var hBR = heights[z][x];

                var ptTL = new Vector3(min.X + stepX * x1, min.Y + hTL * rangeY, min.Z + stepZ * z1);
                var ptTR = new Vector3(min.X + stepX * x,  min.Y + hTR * rangeY, min.Z + stepZ * z1);
                var ptBL = new Vector3(min.X + stepX * x1, min.Y + hBL * rangeY, min.Z + stepZ * z);
                var ptBR = new Vector3(min.X + stepX * x,  min.Y + hBR * rangeY, min.Z + stepZ * z);

                var uvX1 = x1 * uvWrapCount.X / (float)pointsX;
                var uvX2 = x * uvWrapCount.X / (float)pointsX;
                var uvY1 = z1 * uvWrapCount.Y / (float)pointsZ;
                var uvY2 = z * uvWrapCount.Y / (float)pointsZ;

                var L = Math.Max(0, x1 - 1);
                var T = Math.Max(0, z1 - 1);
                var R = Math.Min(pointsX - 1, x + 1);
                var B = Math.Min(pointsZ - 1, z + 1);

                // lord forgive me
                var norTL = SmoothNormal(hTL, heights[z1][L],  heights[T][x1],  heights[z1][x], heights[z][x1], stepX, stepZ, rangeY);
                var norBL = SmoothNormal(hTL, heights[z][L],   heights[z1][x1], heights[z][x],  heights[B][x1], stepX, stepZ, rangeY);
                var norTR = SmoothNormal(hTL, heights[z1][x1], heights[T][x],   heights[z1][R], heights[z][x],  stepX, stepZ, rangeY);
                var norBR = SmoothNormal(hTL, heights[z][x1],  heights[z1][x],  heights[z][R],  heights[B][x],  stepX, stepZ, rangeY);

                InsertQuad(ref ind, ptTL, ptTR, ptBR, ptBL, new Vector2(uvX1, uvY1), new Vector2(uvX2, uvY2), norTL, norTR, norBR, norBL);
            }
        }

        if (VBO != null) UpdateBuffers();
    }

    private static Vector3 SmoothNormal(float hcenter, float hleft, float htop, float hright, float hbot, float stepX, float stepZ, float rangeY)
    {
        // Is there better ways to do this? Probably. Does it work well enough? Yes.
        var p0 = new Vector3(0, hcenter * rangeY, 0);
        var ll = new Vector3(-stepX, hleft * rangeY, 0);
        var tt = new Vector3(0, htop * rangeY, -stepZ);
        var rr = new Vector3(stepX, hright * rangeY, 0);
        var bb = new Vector3(0, hbot * rangeY, stepZ);

        var nor1 = Vector3.Normalize(Vector3.Cross(ll - p0, p0 - (ll + tt) / 2));
        var nor2 = Vector3.Normalize(Vector3.Cross(tt - p0, p0 - (tt + rr) / 2));
        var nor3 = Vector3.Normalize(Vector3.Cross(rr - p0, p0 - (rr + bb) / 2));
        var nor4 = Vector3.Normalize(Vector3.Cross(bb - p0, p0 - (bb + ll) / 2));

        return Vector3.Normalize((nor1 + nor2 + nor3 + nor4) / 4);
    }

    private void InsertQuad(ref int index, Vector3 vec1, Vector3 vec2, Vector3 vec3, Vector3 vec4, Vector2 uvMin, Vector2 uvMax, Vector3 nor1, Vector3 nor2, Vector3 nor3, Vector3 nor4)
    {
        InsertVertex(ref index, vec1, nor1, new Vector2(uvMin.X, uvMin.Y));
        InsertVertex(ref index, vec2, nor2, new Vector2(uvMax.X, uvMin.Y));
        InsertVertex(ref index, vec3, nor3, new Vector2(uvMin.X, uvMax.Y));
        InsertVertex(ref index, vec1, nor1, new Vector2(uvMin.X, uvMin.Y));
        InsertVertex(ref index, vec3, nor3, new Vector2(uvMin.X, uvMax.Y));
        InsertVertex(ref index, vec4, nor4, new Vector2(uvMax.X, uvMax.Y));
    }

    private void InsertVertex(ref int index, Vector3 vec, Vector3 norm, Vector2 uv)
    {
        VertexData![index * layout.VertexSize + 0] = vec.X;
        VertexData[index * layout.VertexSize + 1] = vec.Y;
        VertexData[index * layout.VertexSize + 2] = vec.Z;
        VertexData[index * layout.VertexSize + 3] = uv.X;
        VertexData[index * layout.VertexSize + 4] = uv.Y;
        VertexData[index * layout.VertexSize + 5] = norm.X;
        VertexData[index * layout.VertexSize + 6] = norm.Y;
        VertexData[index * layout.VertexSize + 7] = norm.Z;
        Indices![index] = index;
        index++;
    }

    private void EnsureBufferSize(int pointsX, int pointsZ)
    {
        var totalPoints = pointsZ * pointsX;
        var totalVerts = totalPoints * 4;

        if (VertexData == null || VertexData.Length != totalVerts * layout.VertexSize) {
            VertexData = new float[totalVerts * 6 * layout.VertexSize];
            Indices = new int[totalVerts * 6];
        }
    }

    public override Mesh Clone()
    {
        var mm = new HeightmapMesh();
        CopyGeometryDataReuseArrays(mm);
        return mm;
    }
}
