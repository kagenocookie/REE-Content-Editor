using System.Drawing;
using System.Runtime.InteropServices;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public interface RenderQueueItem
{
    ulong SortingKey { get; }
}

public readonly struct SimpleRenderBatchItem : RenderQueueItem
{
    public readonly Material material;
    public readonly Mesh mesh;
    public readonly MeshHandle meshHandle;
    public readonly Matrix4X4<float> matrix;

    public readonly ulong SortingKey => unchecked((ulong)material.Shader.ID << 48) | ((ulong)material.Hash << 24) | (mesh.ID & 0xffffff);

    public SimpleRenderBatchItem(Material material, Mesh mesh, Matrix4X4<float> matrix, MeshHandle handle) : this()
    {
        this.material = material;
        this.mesh = mesh;
        this.matrix = matrix;
        meshHandle = handle;
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

public abstract class RenderQueue<T> where T : struct, RenderQueueItem
{
    protected readonly List<T> Items = new();

    public void Add(T item)
    {
        Items.Add(item);
    }

    public abstract void Render(RenderContext context);

#region RADIX SORT
    protected int[]? indices;

    private const int RADIX_BITS_PER_PASS = 8;
    private const int RADIX = 1 << RADIX_BITS_PER_PASS; // 256
    private const int MASK = RADIX - 1;

    private ulong[]? tempKeys1;
    private ulong[]? tempKeys2;
    private int[]? tempIndices2;
    private readonly int[] radixCounts = new int[RADIX];

    protected void RadixSort(Span<T> items)
    {
        int count = items.Length;
        if (count == 0) return;

        if (tempKeys1 == null || tempKeys1.Length < count)
        {
            tempKeys1 = new ulong[count];
            indices = new int[count];
            tempKeys2 = new ulong[count];
            tempIndices2 = new int[count];
        }

        for (int i = 0; i < count; i++)
        {
            ref readonly var item = ref items[i];
            tempKeys1[i] = item.SortingKey;
            indices![i] = i;
        }

        for (int shift = 0; shift < 64; shift += RADIX_BITS_PER_PASS)
        {
            Array.Fill(radixCounts, 0);

            for (int i = 0; i < count; i++)
            {
                int bucket = (int)((tempKeys1![i] >> shift) & MASK);
                radixCounts[bucket]++;
            }

            var sum = 0;
            for (int i = 0; i < RADIX; i++)
            {
                var c = radixCounts[i];
                radixCounts[i] = sum;
                sum += c;
            }

            for (int i = 0; i < count; i++)
            {
                var key = tempKeys1![i];
                var bucket = (int)((key >> shift) & MASK);
                var dest = radixCounts[bucket]++;
                tempKeys2![dest] = key;
                tempIndices2![dest] = indices![i];
            }

            (tempKeys1, tempKeys2) = (tempKeys2!, tempKeys1);
            (indices, tempIndices2) = (tempIndices2!, indices);
        }
    }
#endregion
}

public class SimpleRenderQueue(GL gl) : RenderQueue<SimpleRenderBatchItem>
{
    public unsafe override void Render(RenderContext context)
    {
        var itemspan = CollectionsMarshal.AsSpan(Items);
        var count = itemspan.Length;
        RadixSort(itemspan);

        uint lastShaderId = uint.MaxValue;
        uint lastMaterialHash = uint.MaxValue;
        uint lastMeshId = uint.MaxValue;
        for (int i = 0; i < count; ++i) {
            ref readonly var item = ref itemspan[indices![i]];
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

public class RenderBatch(GL gl)
{
    public SimpleRenderQueue Simple { get; } = new(gl);
    // public InstancedRenderQueue Instanced { get; } = new(); // TODO
    public GizmoRenderQueue Gizmo { get; } = new(gl);
}
