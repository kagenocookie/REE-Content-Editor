using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public interface RenderQueueItem
{
    ulong SortingKey { get; }
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
    protected int[]? sortedIndices;

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
            sortedIndices = new int[count];
            tempKeys2 = new ulong[count];
            tempIndices2 = new int[count];
        }

        for (int i = 0; i < count; i++)
        {
            ref readonly var item = ref items[i];
            tempKeys1[i] = item.SortingKey;
            sortedIndices![i] = i;
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
                tempIndices2![dest] = sortedIndices![i];
            }

            (tempKeys1, tempKeys2) = (tempKeys2!, tempKeys1);
            (sortedIndices, tempIndices2) = (tempIndices2!, sortedIndices);
        }
    }
#endregion
}

public sealed class RenderBatch(GL gl) : IDisposable
{
    public NormalRenderQueue Simple { get; } = new(gl);
    public InstancedRenderQueue Instanced { get; } = new(gl);
    public GizmoRenderQueue Gizmo { get; } = new(gl);

    public void Render(RenderContext context)
    {
        Instanced.Render(context);
        Simple.Render(context);
        Gizmo.Render(context);
    }

    public void Dispose()
    {
        Instanced.Dispose();
    }
}
