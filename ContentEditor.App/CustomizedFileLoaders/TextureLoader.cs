using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using ContentEditor;
using ContentEditor.Core;
using GDeflateNet;
using ReeLib;
using ReeLib.Msg;

namespace ContentPatcher;

public class TextureLoader : IFileLoader
{
    int IFileLoader.Priority => 30;

    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.Texture;
    public IResourceFilePatcher? CreateDiffHandler() => null;

    public unsafe IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        var tex = new TexFile(new FileHandler(handle.Stream, handle.Filepath));
        if (!tex.Read()) return null;
        if (tex.MustBeCompressed && tex.IsCompressed) {
            tex.DecompressGDeflate(static (level, compressedBytes, decompressedBytes) => {
                if (!GDeflateNet.GDeflate.Decompress(compressedBytes, decompressedBytes)) {
                    Logger.Error("Failed to gdeflate mip level " + level);
                    return level > 0;
                }
                return true;
            });
        }
        return new BaseFileResource<TexFile>(tex);
    }

    public IResourceFile? CreateNewFile(ContentWorkspace workspace, FileHandle handle) => null;

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var res = handle.GetResource<BaseFileResource<TexFile>>();
        var tex = res.File;
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var fs = File.Create(outputPath);
        var texStream = handle.Stream;
        if (tex.MustBeCompressed) {
            texStream = tex.CompressGDeflate(static (data, level, fileHandler) => {
                using var compressed = GDeflateNet.GDeflate.Compress(data, level, out var size);
                if (size == 0) return 0;

                fileHandler.WriteSpan(compressed.Memory.Span.Slice(0, size));
                return size;
            }, null);
            if (texStream == null) return false;
        }

        texStream.Seek(0, SeekOrigin.Begin);
        texStream.CopyTo(fs);
        return true;
    }
}
