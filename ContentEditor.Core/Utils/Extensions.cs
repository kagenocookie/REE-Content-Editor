using System.Numerics;

namespace ContentEditor.Core;

public static class Extensions
{
    public static void RemoveAtAfter<T>(this List<T> list, int startingIndex)
    {
        for (int i = list.Count - 1; i >= startingIndex; --i) {
            list.RemoveAt(i);
        }
    }

    public static MemoryStream ToMemoryStream(this Stream stream, bool disposeStream = true, bool forceCopy = false)
    {
        if (stream is MemoryStream resultStream && !forceCopy) return resultStream;

        resultStream = new MemoryStream((int)stream.Length);
        stream.CopyTo(resultStream);
        if (disposeStream) stream.Dispose();
        resultStream.Seek(0, SeekOrigin.Begin);
        return resultStream;
    }

    public static uint ToArgb(this Vector4 vec) => (uint)((byte)(vec.X * 255) + ((byte)(vec.Y * 255) << 8) + ((byte)(vec.Z * 255) << 16) + ((byte)(vec.W * 255) << 24));
}