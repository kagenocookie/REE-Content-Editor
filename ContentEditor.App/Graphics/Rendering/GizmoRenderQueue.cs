using System.Numerics;
using System.Runtime.InteropServices;
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
            gl.DepthFunc(item.allowEqualDepth ? DepthFunction.Lequal : DepthFunction.Less);
            if (lastShaderId != item.material.Shader.ID) {
                item.material.Shader.Use();
                context.BindMaterial(item.material);
                lastShaderId = item.material.Shader.ID;
                lastMaterialHash = item.material.Hash;
            } else if (lastMaterialHash != item.material.Hash) {
                context.BindMaterial(item.material);
                lastMaterialHash = item.material.Hash;
            }
            if (lastMeshId != item.mesh.ID) {
                item.mesh.Bind();
                lastMeshId = item.mesh.ID;
            }

            item.material.BindModel(item.matrix);
            item.meshHandle?.BindForRender(item.material);

            gl.PointSize(item.pointSize);
            gl.DrawArrays(item.primitiveType ?? item.mesh.MeshType, 0, (uint)item.mesh.Indices.Length);
        }

        gl.DepthFunc(DepthFunction.Greater);
        foreach (ref readonly var item in itemspan) {
            if (item.obscuredMaterial == null) continue;
            if (lastShaderId != item.obscuredMaterial.Shader.ID) {
                item.obscuredMaterial.Shader.Use();
                context.BindMaterial(item.obscuredMaterial);
                lastShaderId = item.obscuredMaterial.Shader.ID;
                lastMaterialHash = item.obscuredMaterial.Hash;
            } else if (lastMaterialHash != item.obscuredMaterial.Hash) {
                context.BindMaterial(item.obscuredMaterial);
                lastMaterialHash = item.obscuredMaterial.Hash;
            }
            if (lastMeshId != item.mesh.ID) {
                item.mesh.Bind();
                lastMeshId = item.mesh.ID;
            }

            item.obscuredMaterial.BindModel(item.matrix);

            gl.PointSize(item.pointSize);
            gl.DrawArrays(item.primitiveType ?? item.mesh.MeshType, 0, (uint)item.mesh.Indices.Length);
        }
        gl.PointSize(1.0f);
        gl.DepthFunc(DepthFunction.Less);

        Items.Clear();
    }
}

public readonly struct GizmoRenderBatchItem : RenderQueueItem
{
    public readonly Material material;
    public readonly Mesh mesh;
    public readonly Matrix4x4 matrix;
    public readonly Material? obscuredMaterial;
    public readonly MeshHandle? meshHandle;
    public readonly bool allowEqualDepth;
    public readonly PrimitiveType? primitiveType;
    public readonly float pointSize;

    public readonly ulong SortingKey => unchecked((ulong)material.Shader.ID << 48) | ((ulong)material.Hash << 24) | (mesh.ID & 0xffffff);

    public GizmoRenderBatchItem(Material material, Mesh mesh, Matrix4x4 matrix, Material? obscuredMaterial = null, MeshHandle? meshHandle = null, bool allowEqualDepth = false, PrimitiveType? primitiveType = null, float pointSize = 1.0f) : this()
    {
        this.material = material;
        this.mesh = mesh;
        this.matrix = matrix;
        this.obscuredMaterial = obscuredMaterial;
        this.meshHandle = meshHandle;
        this.allowEqualDepth = allowEqualDepth;
        this.primitiveType = primitiveType;
        this.pointSize = pointSize;
    }
    public GizmoRenderBatchItem(MeshHandle handle, int meshAndMatIndex, Matrix4x4 matrix, int obscuredMatIndex = -1) : this()
    {
        this.material = handle.Material.Materials[meshAndMatIndex];
        this.mesh = handle.Handle.Meshes[meshAndMatIndex];
        this.matrix = matrix;
        this.obscuredMaterial = obscuredMatIndex == -1 ? null : handle.Material.Materials[meshAndMatIndex];
        this.meshHandle = handle;
        this.pointSize = 1.0f;
    }
}
