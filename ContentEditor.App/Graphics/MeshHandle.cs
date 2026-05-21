using System.Runtime.CompilerServices;
using ReeLib.Mesh;
using ReeLib.via;

namespace ContentEditor.App.Graphics;

/// <summary>
/// Reference to a mesh + mdf2 combination.
/// This is separate from the raw mesh data so we can apply different materials to the same mesh without loading the mesh geometry twice.
/// </summary>
public class MeshHandle
{
    internal MeshResourceHandle Handle { get; }
    internal MaterialGroup Material { get; private set; } = new();

    public MeshBoneHierarchy? Bones => Handle.Bones;

    public bool HasArmature => Handle.Meshes.Any(m => m.HasBones);

    public List<(int subMeshIndex, int matIndex)> EnabledSubmeshIndices { get; } = new();

    public IEnumerable<Mesh> Meshes => Handle.Meshes.AsReadOnly();
    public AABB BoundingBox => Handle.BoundingBox;
    private readonly HashSet<int> DisabledParts = new(0);
    public bool IsEmpty => Handle.Meshes.Count == 0;

    public IEnumerable<(Mesh, Material)> EnabledSubmeshes {
        get {
            foreach (var (mi, mat) in EnabledSubmeshIndices) {
                yield return (Handle.Meshes[mi], Material.Materials[mat]);
            }
        }
    }

    internal MeshHandle(MeshResourceHandle mesh)
    {
        Handle = mesh;
    }

    public Mesh GetMesh(int index) => Handle.Meshes[index];
    public Material GetMaterial(int index) => Material.Materials[index];

    public Material? GetMaterialForMesh(int meshIndex)
    {
        PrepareSubmeshParts();
        foreach (var part in EnabledSubmeshIndices) {
            if (part.subMeshIndex == meshIndex) {
                return Material.Materials[part.matIndex];
            }
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GetMeshPartEnabled(int index) => !DisabledParts.Contains(index);
    public void SetMeshPartEnabled(int index, bool enabled)
    {
        if (enabled) DisabledParts.Remove(index);
        else DisabledParts.Add(index);
    }
    public void SetAllPartsEnabled(bool enabled)
    {
        if (enabled) {
            DisabledParts.Clear();
        } else {
            for (int i = 0; i < 255; i++) DisabledParts.Add(i);
        }
    }
    public void SetPartsEnabled(IEnumerable<bool> enabled)
    {
        DisabledParts.Clear();
        int i = 0;
        foreach (var enable in enabled) {
            if (!enable) DisabledParts.Add(i);
            i++;
        }
    }

    /// <summary>
    /// For dynamic meshes, this will make the mesh list and / or geometry update.
    /// </summary>
    public virtual void Update() { }

    public void PrepareSubmeshParts()
    {
        if (Material.ContentHash != lastMaterialHash) {
            UpdateParts();
            lastMaterialHash = Material.ContentHash;
        }
    }

    private uint lastMaterialHash = uint.MaxValue;

    private void UpdateParts()
    {
        EnabledSubmeshIndices.Clear();
        for (int i = 0; i < Handle.Meshes.Count; i++) {
            var mesh = Handle.Meshes[i];
            if (DisabledParts.Contains(mesh.MeshGroup)) continue;
            var matIndex = Material.GetMaterialIndex(mesh.MaterialNameHash);
            if (matIndex == -1) {
                continue;
            }

            EnabledSubmeshIndices.Add((i, matIndex));
        }
    }

    public override string ToString() => $"[Mesh handle {Handle}]";

    internal void SetMaterials(MaterialGroup material)
    {
        Material = material;
    }

    public virtual void BindForRender(Material material) { }
}
