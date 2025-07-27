using ReeLib;

namespace ContentPatcher;

public class UnknownStreamFileLoader : IFileLoader
{
    private UnknownStreamFileLoader() { }

    public static readonly UnknownStreamFileLoader Instance = new();
    public bool CanHandleFile(string filepath, REFileFormat format)
    {
        return true;
    }

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public bool Load(ContentWorkspace workspace, FileHandle handle)
    {
        handle.Resource = new DummyFileResource(handle.Stream);
        return true;
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        using var ofs = File.Create(outputPath);
        handle.Stream.CopyTo(ofs);
        return true;
    }
}

public class DummyFileResource(Stream stream) : IResourceFile
{
    public Stream Stream { get; } = stream;

    public void WriteTo(string filepath)
    {
        using var outfs = File.Create(filepath);
        Stream.CopyTo(outfs);
    }
}
