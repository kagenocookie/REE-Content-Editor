using System.Numerics;
using ReeLib.Common;
using ReeLib.Mesh;
using ReeLib.via;

namespace ContentEditor.App.Graphics;

/// <summary>
/// The raw internal mesh representation.
/// </summary>
public sealed class MeshResourceHandle : IDisposable
{
    public int HandleID { get; }
    internal List<Mesh> Meshes { get; } = new();

    public MeshBoneHierarchy? Bones { get; set; }

    public bool HasArmature => Bones != null;
    public bool Animatable => Bones != null && Meshes.Any(m => m.HasBones);

    public IEnumerable<Mesh> Submeshes => Meshes.AsReadOnly();
    private readonly Dictionary<int, string> materialNames = new();

    internal MeshResourceHandle(Mesh mesh)
    {
        Meshes.Add(mesh);
    }

    internal MeshResourceHandle(int handleId)
    {
        HandleID = handleId;
    }

    internal void RemoveMeshes(int startingFromIndex = 0)
    {
        for (int i = Meshes.Count - 1; i >= startingFromIndex; i--) {
            Meshes[i].Dispose();
            Meshes.RemoveAt(i);
        }
    }

    public string GetMaterialName(int index) => materialNames.GetValueOrDefault(index) ?? "";
    internal void SetMaterialName(int index, string name) => materialNames[index] = name;

    public AABB BoundingBox => Meshes.Count == 0 ? default : Meshes.Aggregate(AABB.MaxMin, (bound, item) => item.BoundingBox.Extend(bound));

    public override string ToString() => $"[Mesh {HandleID} / {Meshes.Count} submeshes]";

    public Vector3 GetIntersection(Ray ray, in Matrix4x4 worldMatrix)
    {
        var closestTriangleDistance = float.MaxValue;
        Matrix4x4.Invert(worldMatrix, out var invMat);
        var localRay = new Ray() {
            from = Vector3.Transform(ray.from, invMat),
            dir = Vector3.TransformNormal(ray.dir, invMat)
        };
        var closestVec = new Vector3(float.MinValue);
        foreach (var mesh in Meshes) {
            if (mesh.MeshType != Silk.NET.OpenGL.PrimitiveType.Triangles) continue;

            var layoutSize = mesh.layout.VertexSize;

            for (int i = layoutSize * 2; i < mesh.VertexData.Length; i += layoutSize) {
                var v1 = new Vector3(mesh.VertexData[i - layoutSize * 2 + 0], mesh.VertexData[i - layoutSize * 2 + 1], mesh.VertexData[i - layoutSize * 2 + 2]);
                var v2 = new Vector3(mesh.VertexData[i - layoutSize * 1 + 0], mesh.VertexData[i - layoutSize * 1 + 1], mesh.VertexData[i - layoutSize * 1 + 2]);
                var v3 = new Vector3(mesh.VertexData[i - layoutSize * 0 + 0], mesh.VertexData[i - layoutSize * 0 + 1], mesh.VertexData[i - layoutSize * 0 + 2]);

                if (MathHelpers.IntersectsTriangle(localRay, v1, v2, v3, out var intersection)) {
                    var worldVec = Vector3.Transform(intersection, worldMatrix);
                    var dist = Vector3.DistanceSquared(worldVec, ray.from);
                    if (dist < closestTriangleDistance) {
                        closestVec = worldVec;
                        closestTriangleDistance = dist;
                    }
                }
            }
        }

        return closestVec;
    }

    public void Dispose()
    {
        // mesh handle owns the meshes
        foreach (var mesh in Meshes) {
            mesh.Dispose();
        }
    }
}
