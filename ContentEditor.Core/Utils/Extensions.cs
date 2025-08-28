using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;

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

    public static bool TryDeserializeJsonFile<T>(this string jsonFilepath, [MaybeNullWhen(false)] out T result, out string? error, JsonSerializerOptions? options = null)
    {
        if (!File.Exists(jsonFilepath)) {
            result = default;
            error = "File not found";
            return false;
        }

        using var fs = File.OpenRead(jsonFilepath);
        try {
            result = JsonSerializer.Deserialize<T>(fs, options);
            error = null;
            return result != null;
        } catch (Exception e) {
            result = default;
            error = e.Message;
            return false;
        }
    }
    public static bool TryDeserializeJson<T>(this string json, [MaybeNullWhen(false)] out T result, out string? error, JsonSerializerOptions? options = null)
    {
        try {
            var obj = JsonSerializer.Deserialize<T>(json, options);
            result = obj;
            error = null;
            return obj != null;
        } catch (Exception e) {
            result = default;
            error = e.Message;
            return false;
        }
    }

    public static uint ToArgb(this Vector4 vec) => (uint)((byte)(vec.X * 255) + ((byte)(vec.Y * 255) << 8) + ((byte)(vec.Z * 255) << 16) + ((byte)(vec.W * 255) << 24));
    public static Vector2 ToVec2(this Vector4 vec) => new Vector2(vec.X, vec.Y);
    public static Vector3 ToVec3(this Vector4 vec) => new Vector3(vec.X, vec.Y, vec.Z);
    public static Vector4 ToVec4(this Vector2 vec) => new Vector4(vec.X, vec.Y, 0, 0);
    public static Vector4 ToVec4(this Vector3 vec) => new Vector4(vec.X, vec.Y, vec.Z, 0);
    public static Quaternion ToQuaternion(this Vector4 vec) => Unsafe.As<Vector4, Quaternion>(ref vec);
    public static Vector4 ToVector4(this Quaternion vec) => Unsafe.As<Quaternion, Vector4>(ref vec);
}