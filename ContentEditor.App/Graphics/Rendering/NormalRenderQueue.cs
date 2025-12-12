using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class NormalRenderQueue(GL gl) : RenderQueue<NormalRenderBatchItem>
{
    public override void Render(RenderContext context)
    {
        var itemspan = CollectionsMarshal.AsSpan(Items);
        var count = itemspan.Length;
        RadixSort(itemspan);

        uint lastShaderId = uint.MaxValue;
        uint lastMaterialHash = uint.MaxValue;
        uint lastMeshId = uint.MaxValue;
        for (int i = 0; i < count; ++i) {
            ref readonly var item = ref itemspan[sortedIndices![i]];
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
            // this could probably be handled better somehow (required for animations)
            item.meshHandle.BindForRender(item.material);

            gl.DrawArrays(item.mesh.MeshType, 0, (uint)item.mesh.Indices.Length);
        }
        Items.Clear();
    }
}

public readonly struct NormalRenderBatchItem : RenderQueueItem
{
    public readonly Material material;
    public readonly Mesh mesh;
    public readonly MeshHandle meshHandle;
    public readonly Matrix4x4 matrix;

    public readonly ulong SortingKey => unchecked((ulong)material.Shader.ID << 48) | ((ulong)material.Hash << 24) | (mesh.ID & 0xffffff);

    public NormalRenderBatchItem(Material material, Mesh mesh, Matrix4X4<float> matrix, MeshHandle handle) : this(material, mesh, matrix.ToSystem(), handle) {}
    public NormalRenderBatchItem(Material material, Mesh mesh, Matrix4x4 matrix, MeshHandle handle) : this()
    {
        this.material = material;
        this.mesh = mesh;
        this.matrix = matrix;
        meshHandle = handle;
    }
}
