using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public sealed class InstancedRenderQueue(GL gl) : RenderQueue<InstancedRenderBatchItem>, IDisposable
{
    private const int MaxBatchSize = 2048;
    private const int MatrixSize = 16 * sizeof(float);
    private const int GpuBufferMatrixCount = 16384;
    private const int GpuBufferSize = GpuBufferMatrixCount * MatrixSize;

    private readonly Matrix4X4<float>[] matrixBatch = new Matrix4X4<float>[MaxBatchSize];

    private int batchInstanceCount = 0;
    private int curBufferId;
    private uint[] TransformBuffers = [];
    private Mesh? lastMesh;

    private unsafe void CreateAdditionalVBO()
    {
        var newIndex = TransformBuffers.Length;
        Array.Resize(ref TransformBuffers, newIndex + 1);
        var newBuffer = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, newBuffer);
        gl.BufferData(BufferTargetARB.ArrayBuffer, GpuBufferSize, null, BufferUsageARB.DynamicDraw);
        TransformBuffers[newIndex] = newBuffer;
    }

    public unsafe override void Render(RenderContext context)
    {
        var itemspan = CollectionsMarshal.AsSpan(Items);
        var count = itemspan.Length;
        RadixSort(itemspan);
        if (TransformBuffers.Length == 0) {
            CreateAdditionalVBO();
        }

        batchInstanceCount = 0;
        curBufferId = 0;
        int bufferIndexOffset = 0;

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, TransformBuffers[curBufferId]);

        uint lastShaderId = uint.MaxValue;
        uint lastMaterialHash = uint.MaxValue;
        for (int i = 0; i < count; ++i) {
            ref readonly var item = ref itemspan[sortedIndices![i]];
            if (lastMesh != item.mesh) {
                RenderBatch(ref bufferIndexOffset);
                item.mesh.VAO.Bind();
                item.mesh.ApplyInstancing(TransformBuffers[curBufferId], (uint)(bufferIndexOffset * MatrixSize));
                lastMesh = item.mesh;
            }

            if (lastShaderId != item.material.Shader.ID) {
                RenderBatch(ref bufferIndexOffset);
                item.material.Shader.Use();
                item.material.Bind();
                lastShaderId = item.material.Shader.ID;
                lastMaterialHash = item.material.Hash;
            } else if (lastMaterialHash != item.material.Hash) {
                RenderBatch(ref bufferIndexOffset);
                item.material.Bind();
                lastMaterialHash = item.material.Hash;
            }

            foreach (ref readonly var mat in CollectionsMarshal.AsSpan(item.matrices)) {
                if (bufferIndexOffset + batchInstanceCount >= GpuBufferMatrixCount) {
                    RenderBatch(ref bufferIndexOffset);
                    curBufferId++;
                    if (curBufferId >= TransformBuffers.Length) {
                        CreateAdditionalVBO();
                    }
                    gl.BindBuffer(BufferTargetARB.ArrayBuffer, TransformBuffers[curBufferId]);
                    item.mesh.ApplyInstancing(TransformBuffers[curBufferId], (uint)(bufferIndexOffset * MatrixSize));
                    bufferIndexOffset = 0;
                    batchInstanceCount = 0;
                } else if (batchInstanceCount >= MaxBatchSize) {
                    RenderBatch(ref bufferIndexOffset);
                }
                matrixBatch[batchInstanceCount++] = mat;
                // matrixBatch[batchInstanceCount++] = Matrix4X4.Transpose(mat);
            }
        }
        RenderBatch(ref bufferIndexOffset);
        Items.Clear();
    }

    private unsafe void RenderBatch(ref int bufferIndexOffset)
    {
        if (batchInstanceCount == 0) return;

        var bufferStart = bufferIndexOffset * MatrixSize;
        var bufferSize = (uint)batchInstanceCount * MatrixSize;

        gl.BufferSubData(BufferTargetARB.ArrayBuffer, bufferStart, bufferSize, Unsafe.AsPointer(ref matrixBatch[0]));
        // lastMesh!.VAO.UpdateInstanceAttributes(TransformBuffers[curBufferId], (uint)bufferStart);
        gl.DrawArraysInstancedBaseInstance(lastMesh!.MeshType, 0, (uint)lastMesh.Indices.Length, (uint)batchInstanceCount, (uint)0);
        // gl.DrawArraysInstanced(lastMesh!.MeshType, 0, (uint)lastMesh.Indices.Length, (uint)batchInstanceCount);
        bufferIndexOffset += batchInstanceCount;
        batchInstanceCount = 0;
    }

    public void Dispose()
    {
        foreach (var buf in TransformBuffers) {
            gl.DeleteBuffer(buf);
        }
    }
}

public readonly struct InstancedRenderBatchItem : RenderQueueItem
{
    public readonly Material material;
    public readonly Mesh mesh;
    public readonly List<Matrix4X4<float>> matrices;

    public readonly ulong SortingKey => unchecked((ulong)material.Shader.ID << 48) | ((ulong)material.Hash << 24) | (mesh.ID & 0xffffff);

    public InstancedRenderBatchItem(Material material, Mesh mesh, List<Matrix4X4<float>> matrices) : this()
    {
        this.material = material;
        this.mesh = mesh;
        this.matrices = matrices;
    }
}
