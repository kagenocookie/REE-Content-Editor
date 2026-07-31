using ReeLib;

namespace ContentPatcher;

public class BaseFileResource<T>(T file) : IReeLibResourceFile where T : BaseFile
{
    public T File { get; } = file;

    public FileHandler FileHandler {
        get => File.FileHandler;
        set => File.FileHandler = value;
    }

    public void WriteTo(string filepath)
    {
        File.WriteTo(filepath);
    }
}

public interface IReeLibResourceFile : IResourceFile
{
    FileHandler FileHandler { get; set; }
}
