using System.Runtime.CompilerServices;
using ReeLib.Mesh;
using ReeLib.via;

namespace ContentEditor.App.Graphics;

/// <summary>
/// Reference to a mesh + material combination.
/// This is separate from the raw mesh data so we can apply different materials to the same mesh without loading the mesh geometry twice.
/// </summary>
public class MeshHandle
{
    internal MeshResourceHandle Handle { get; }
    internal MaterialGroup Material { get; private set; } = new();

    public MeshBoneHierarchy? Bones => Handle.Bones;

    public bool HasArmature => Handle.Meshes.Any(m => m.HasBones);

    /// <summary>
    /// Remapping table for submesh index to material, so we can have material groups with different material ordering correctly apply to the same mesh resource.
    /// </summary>
    internal List<int> MaterialIndicesRemap { get; } = new();

    public IEnumerable<Mesh> Meshes => Handle.Meshes.AsReadOnly();
    public AABB BoundingBox => Handle.BoundingBox;
    private readonly HashSet<int> DisabledParts = new(0);
    public bool IsEmpty => Handle.Meshes.Count == 0;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GetMeshPartEnabled(int index) => !DisabledParts.Contains(index);
    public void SetMeshPartEnabled(int index, bool enabled)
    {
        if (enabled) DisabledParts.Remove(index);
        else DisabledParts.Add(index);
    }

    /// <summary>
    /// For dynamic meshes, this will make the mesh list and / or geometry update.
    /// </summary>
    public virtual void Update() { }

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

    internal void SetMaterials(MaterialGroup material)
    {
        Material = material;
        MaterialIndicesRemap.Clear();
        MaterialIndicesRemap.AddRange(Enumerable.Range(0, material.Materials.Count));
    }

    internal void UpdateMaterials(IEnumerable<int> meshRemapIndices)
    {
        MaterialIndicesRemap.Clear();
        MaterialIndicesRemap.AddRange(meshRemapIndices);
    }

    public virtual void BindForRender(Material material) { }
}
