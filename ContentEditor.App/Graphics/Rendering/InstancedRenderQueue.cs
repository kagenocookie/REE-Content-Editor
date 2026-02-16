using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public sealed class InstancedRenderQueue(GL gl) : RenderQueue<InstancedRenderBatchItem>, IDisposable
{
    private const int MatrixSize = 16 * sizeof(float);
    private const int BufferMatrixCount = 16384;
    private const int BufferSize = BufferMatrixCount * MatrixSize;

    private readonly Matrix4x4[] matrixBatch = new Matrix4x4[BufferMatrixCount];

    private readonly List<DrawArraysIndirectCommand> drawCommands = new();

    private int batchInstanceCount = 0;
    private int curBufferId;
    private uint[] TransformBuffers = [];
    private Mesh? lastMesh;

    private record struct DrawArraysIndirectCommand(uint count, uint instanceCount, uint first, uint baseInstance);

    private unsafe void CreateAdditionalVBO()
    {
        var newIndex = TransformBuffers.Length;
        Array.Resize(ref TransformBuffers, newIndex + 1);
        var newBuffer = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, newBuffer);
        gl.BufferData(BufferTargetARB.ArrayBuffer, BufferSize, null, BufferUsageARB.DynamicDraw);
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

        // handle dumping all matrices first because doing that one by one is too slow
        for (int i = 0; i < count; ++i) {
            ref readonly var item = ref itemspan[sortedIndices![i]];
            foreach (ref readonly var mat in CollectionsMarshal.AsSpan(item.matrices)) {
                if (bufferIndexOffset + batchInstanceCount >= BufferMatrixCount) {
                    DumpBatchMatrixBuffer();
                    if (curBufferId >= TransformBuffers.Length) CreateAdditionalVBO();
                }
                matrixBatch[batchInstanceCount++] = mat;
            }
        }
        DumpBatchMatrixBuffer();
        curBufferId = 0;

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, TransformBuffers[curBufferId]);
        uint lastShaderId = uint.MaxValue;
        uint lastMaterialHash = uint.MaxValue;
        for (int i = 0; i < count; ++i) {
            ref readonly var item = ref itemspan[sortedIndices![i]];
            if (lastMesh != item.mesh) {
                RenderBatch(ref bufferIndexOffset);
                item.mesh.VAO.Bind();
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
                if (bufferIndexOffset + batchInstanceCount >= BufferMatrixCount) {
                    RenderBatch(ref bufferIndexOffset);
                    curBufferId++;
                    // note: the matrix pre-pass would've created all the buffers we need, we can just assume it's there now
                    gl.BindBuffer(BufferTargetARB.ArrayBuffer, TransformBuffers[curBufferId]);
                    bufferIndexOffset = 0;
                    batchInstanceCount = 0;
                }
                batchInstanceCount++;
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

        lastMesh!.UpdateInstancedMatrixBuffer(TransformBuffers[curBufferId], (uint)bufferStart);
        gl.DrawArraysInstanced(lastMesh!.MeshType, 0, (uint)lastMesh.Indices.Length, (uint)batchInstanceCount);
        bufferIndexOffset += batchInstanceCount;
        batchInstanceCount = 0;
    }

    private unsafe void DumpBatchMatrixBuffer()
    {
        if (batchInstanceCount == 0) return;

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, TransformBuffers[curBufferId++]);
        gl.BufferData(BufferTargetARB.ArrayBuffer, BufferSize, Unsafe.AsPointer(ref matrixBatch[0]), BufferUsageARB.DynamicDraw);
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
    public readonly List<Matrix4x4> matrices;

    public readonly ulong SortingKey => unchecked((ulong)material.Shader.ID << 48) | ((ulong)material.Hash << 24) | (mesh.ID & 0xffffff);

    public InstancedRenderBatchItem(Material material, Mesh mesh, List<Matrix4x4> matrices) : this()
    {
        this.material = material;
        this.mesh = mesh;
        this.matrices = matrices;
    }
}
