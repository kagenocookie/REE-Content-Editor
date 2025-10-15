using System.Runtime.InteropServices;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class GizmoRenderQueue(GL gl) : RenderQueue<GizmoRenderBatchItem>
{
    public unsafe override void Render(RenderContext context)
    {
        var itemspan = CollectionsMarshal.AsSpan(Items);
        // gizmos are probably a bit more gpu aware and not as many of them, so just let them draw in whatever order

        uint lastShaderId = uint.MaxValue;
        uint lastMaterialHash = uint.MaxValue;
        uint lastMeshId = uint.MaxValue;
        foreach (ref readonly var item in itemspan) {
            if (lastShaderId != item.material.Shader.ID) {
                item.material.Shader.Use();
                item.material.Bind();
                lastShaderId = item.material.Shader.ID;
                lastMaterialHash = item.material.Hash;
            } else if (lastMaterialHash != item.material.Hash) {
                item.material.Bind();
                lastMaterialHash = item.material.Hash;
            }
            if (lastMeshId != item.mesh.ID) {
                item.mesh.Bind();
                lastMeshId = item.mesh.ID;
            }

            item.material.BindModel(item.matrix);

            gl.DrawArrays(item.mesh.MeshType, 0, (uint)item.mesh.Indices.Length);
            if (item.obscuredMaterial != null) {
                // re-draw with flipped depth func to provide darker "obscured" display
                item.obscuredMaterial.Bind();
                lastMaterialHash = item.obscuredMaterial.Hash;
                gl.DepthFunc(DepthFunction.Greater);
                gl.DrawArrays(item.mesh.MeshType, 0, (uint)item.mesh.Indices.Length);
                gl.DepthFunc(DepthFunction.Less);
            }
        }
        Items.Clear();
    }
}

public readonly struct GizmoRenderBatchItem : RenderQueueItem
{
    public readonly Material material;
    public readonly Mesh mesh;
    public readonly Matrix4X4<float> matrix;
    public readonly Material obscuredMaterial;

    public readonly ulong SortingKey => unchecked((ulong)material.Shader.ID << 48) | ((ulong)material.Hash << 24) | (mesh.ID & 0xffffff);

    public GizmoRenderBatchItem(Material material, Mesh mesh, Matrix4X4<float> matrix, Material obscuredMaterial) : this()
    {
        this.material = material;
        this.mesh = mesh;
        this.matrix = matrix;
        this.obscuredMaterial = obscuredMaterial;
    }
}
