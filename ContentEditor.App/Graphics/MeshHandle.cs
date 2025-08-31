using ReeLib.via;

namespace ContentEditor.App.Graphics;

/// <summary>
/// Reference to a mesh + material combination.
/// This is separate from the raw mesh data so we can apply different materials to the same mesh without loading the mesh geometry twice.
/// </summary>
public sealed class MeshHandle
{
    internal MeshResourceHandle Handle { get; }
    internal MaterialGroup Material { get; private set; } = new();

    /// <summary>
    /// Remapping table for submesh index to material, so we can have material groups with different material ordering correctly apply to the same mesh resource.
    /// </summary>
    internal List<int> MaterialIndicesRemap { get; } = new();

    public IEnumerable<Mesh> Meshes => Handle.Meshes.AsReadOnly();
    public AABB BoundingBox => Handle.BoundingBox;

    internal MeshHandle(MeshResourceHandle mesh)
    {
        Handle = mesh;
    }

    public Mesh GetMesh(int index) => Handle.Meshes[index];
    public Material GetMaterial(int meshIndex)
    {
        var remappedIndex = meshIndex;
        if (meshIndex >= 0 && meshIndex < MaterialIndicesRemap.Count) {
            remappedIndex = MaterialIndicesRemap[meshIndex];
        }

        if (remappedIndex >= 0 && remappedIndex < Material.Materials.Count) {
            return Material.Materials[remappedIndex];
        }

        return Material.Materials[0];
    }

    public void SetMaterial(int meshIndex, string materialName)
    {
        var matIndex = Material.GetMaterialIndex(materialName);
        MaterialIndicesRemap[meshIndex] = matIndex;
    }

    public override string ToString() => $"[Mesh handle {Handle}]";

    internal void SetMaterials(MaterialGroup material, IEnumerable<int> meshRemapIndices)
    {
        Material = material;
        UpdateMaterials(meshRemapIndices);
    }

    internal void UpdateMaterials(IEnumerable<int> meshRemapIndices)
    {
        MaterialIndicesRemap.Clear();
        MaterialIndicesRemap.AddRange(meshRemapIndices);
    }
}

/// <summary>
/// The raw internal mesh representation.
/// </summary>
public sealed class MeshResourceHandle : IDisposable
{
    public int HandleID { get; }
    internal List<Mesh> Meshes { get; } = new();

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
