using ReeLib.via;

namespace ContentEditor.App.Graphics;

/// <summary>
/// The raw internal mesh representation.
/// </summary>
public sealed class MeshResourceHandle : IDisposable
{
    public int HandleID { get; }
    internal List<Mesh> Meshes { get; } = new();

    public bool HasArmature => Meshes.Any(m => m.HasBones);

    internal void RemoveMeshes(int startingFromIndex = 0)
    {
        for (int i = Meshes.Count - 1; i >= startingFromIndex; i--) {
            Meshes[i].Dispose();
            Meshes.RemoveAt(i);
        }
    }

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

    public string GetMaterialName(int index) => materialNames.GetValueOrDefault(index) ?? "";
    internal void SetMaterialName(int index, string name) => materialNames[index] = name;

    public AABB BoundingBox => Meshes.Count == 0 ? default : Meshes.Aggregate(AABB.MaxMin, (bound, item) => item.BoundingBox.Extend(bound));

    public override string ToString() => $"[Mesh {HandleID} / {Meshes.Count} submeshes]";

    public void Dispose()
    {
        // mesh handle owns the meshes
        foreach (var mesh in Meshes) {
            mesh.Dispose();
        }
    }
}
