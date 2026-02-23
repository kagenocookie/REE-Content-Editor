using System.Runtime.InteropServices;
using ReeLib;
using Silk.NET.Windowing;

namespace ContentEditor.App;

/// <param name="name">The display name for this file filter.</param>
/// <param name="extensions">The list of allowed file extensions. The `*.` prefix will be added automatically.</param>
public record struct FileFilter(string name, params string[] extensions)
{
    public FileFilter(KnownFileFormats format, string referencePath)
        : this(format.ToString(), PathUtils.GetFilenameExtensionWithSuffixes(referencePath).ToString())
    {
    }
}

public static class FileFilters
{
    public static readonly FileFilter[] AllFiles = [new FileFilter("All files", "*")];

    public static readonly FileFilter[] PakFile = [new FileFilter("PAK File", "pak")];
    public static readonly FileFilter[] JsonFile = [new FileFilter("JSON", "json")];
    public static readonly FileFilter[] CollectionJsonFile = [new FileFilter("Mesh Collection JSON", "collection.json")];
    public static readonly FileFilter[] ListFile = [new FileFilter("List file", "list"), new FileFilter("Any", "*")];
    public static readonly FileFilter[] Motlist = [new FileFilter("MOTLIST", "motlist")];
    public static readonly FileFilter[] GlbFile = [new FileFilter("GLB", "glb")];
    public static readonly FileFilter[] MeshFile = [new FileFilter("FBX", "fbx"), new FileFilter("GLTF", "gltf"), ..GlbFile];
    public static readonly FileFilter[] MeshFilesAll = [new FileFilter("Any mesh file", "fbx", "glb", "gltf"), ..MeshFile];

    public static readonly FileFilter[] CsvJsonFile = [new FileFilter("CSV", "csv"), new FileFilter("JSON", "json")];
    public static readonly FileFilter[] CsvJsonFileAll = [new FileFilter("Supported message formats", "csv", "json"), new FileFilter("CSV", "csv"), new FileFilter("JSON", "json")];
    public static readonly FileFilter[] DDSFile = [new FileFilter("DDS", "dds")];
    public static readonly FileFilter[] TextureFile = [new FileFilter("TGA", "tga"), new FileFilter("PNG", "png"), ..DDSFile];
    public static readonly FileFilter[] TextureFilesAll = [new FileFilter("Supported images", "tga", "png", "dds"), ..TextureFile];
    public static readonly FileFilter[] ImageFiles = [new FileFilter("Supported images", "jpg", "png", "jpeg", "bmp"), new FileFilter("PNG", "png"), new FileFilter("JPG", "jpg", "jpeg"), new FileFilter("BMP", "bmp")];

    public static readonly FileFilter[] ThemeFile = [new FileFilter("Theme File", "theme.txt")];
}

