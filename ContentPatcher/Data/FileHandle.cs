using ContentEditor.Core;
using ContentEditor.Editor;
using ReeLib;

namespace ContentPatcher;

public sealed class FileHandle(string path, Stream stream, FileHandleType handleType, IFileLoader loader) : IDisposable
{
    public string Filepath { get; } = path;
    public string? NativePath { get; init; }
    public string? InternalPath => NativePath == null ? null : PathUtils.GetInternalFromNativePath(NativePath);
    public FileHandleType HandleType { get; } = handleType;
    public IFileLoader Loader { get; set; } = loader;
    public IResourceFilePatcher? DiffHandler { get; set; }
    public Stream Stream { get; set; } = stream;
    public IResourceFile Resource { get; internal set; } = null!;
    public List<IFileHandleReferenceHolder> References { get; set; } = new();
    private bool _modified;
    public bool Modified
    {
        get => _modified;
        set {
            if (value != _modified) {
                _modified = value;
                ModifiedChanged?.Invoke(value);
            }
        }
    }

    public ReadOnlySpan<char> Filename => Path.GetFileName(Filepath.AsSpan());
    public REFileFormat Format => PathUtils.ParseFileFormat(NativePath ?? Filepath);

    public event Action? Saved;
    public event Action? Reverted;
    public event Action<bool>? ModifiedChanged;

    public T GetResource<T>() where T : IResourceFile => (T)Resource;
    public T GetContent<T>() where T : BaseFile => (Resource as BaseFileResource<T>)?.File
        ?? (Loader as IFileHandleContentProvider<T>)?.GetFile(this)
        ?? throw new NotImplementedException();

    public static FileHandle CreateEmbedded(IFileLoader loader, IResourceFile file)
    {
        return new FileHandle(string.Empty, new MemoryStream(0), FileHandleType.Embedded, loader);
    }

    public void Revert(ContentWorkspace workspace)
    {
        Load(workspace);
        Reverted?.Invoke();
    }

    public void Load(ContentWorkspace workspace)
    {
        Loader.Load(workspace, this);
        Modified = false;
    }

    public void Save(ContentWorkspace workspace, string? newFilepath = null)
    {
        // TODO verify if we even have a proper disk filepath
        Loader.Save(workspace, this, newFilepath ?? Filepath);
        if (newFilepath == null) {
            Modified = false;
            Saved?.Invoke();
        }
    }

    public bool IsInBundle(ContentWorkspace workspace, Bundle bundle)
    {
        return Filepath.StartsWith(workspace.BundleManager.GetBundleFolder(bundle));
    }

    public override string ToString() => $"{Path.GetFileName(Filepath)} {{ {Loader.GetType().Name} }} [{Filepath}]";

    public void Dispose()
    {
        Stream.Dispose();
        (Resource as IDisposable)?.Dispose();
    }

    internal static FileHandle FromDiskFilePath(string filepath, IFileLoader loader)
    {
        var stream = File.OpenRead(filepath);
        return new FileHandle(filepath, stream, FileHandleType.DiskFile, loader);
    }
}

public enum FileHandleType
{
    DiskFile,
    InMemoryFile,
    Embedded,
}

public interface IFileHandleReferenceHolder
{
    bool IsClosable { get; }
    IRectWindow? Parent { get; }

    void Close();
}

public interface IFocusableFileHandleReferenceHolder : IFileHandleReferenceHolder
{
    void Focus();
}