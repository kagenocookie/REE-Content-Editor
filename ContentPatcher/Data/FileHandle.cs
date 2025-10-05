using ContentEditor;
using ContentEditor.Core;
using ContentEditor.Editor;
using ReeLib;

namespace ContentPatcher;

public sealed class FileHandle(string path, Stream stream, FileHandleType handleType, IFileLoader loader) : IDisposable, IEquatable<FileHandle>
{
    public string Filepath { get; private set; } = path;
    public string? NativePath { get; init; }
    public string? InternalPath => NativePath == null ? null : PathUtils.GetInternalFromNativePath(NativePath);
    public FileHandleType HandleType { get; private set; } = handleType;
    public IFileLoader Loader { get; set; } = loader;
    public IResourceFilePatcher? DiffHandler { get; set; }
    public Stream Stream { get; set; } = stream;
    public IResourceFile Resource { get; internal set; } = null!;
    public List<IFileHandleReferenceHolder> References { get; set; } = new();
    public string? FileSource { get; set; }
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
    public T GetFile<T>() where T : BaseFile => (Resource as BaseFileResource<T>)?.File
        ?? (Loader as IFileHandleContentProvider<T>)?.GetFile(this)
        ?? throw new NotImplementedException();

    public T? GetCustomContent<T>() where T : class => (Loader as IFileHandleContentProvider<T>)?.GetFile(this);

    public static FileHandle CreateEmbedded(IFileLoader loader, IResourceFile file, string filepath = "", string? nativePath = null)
    {
        var handle = new FileHandle(filepath, new MemoryStream(0), FileHandleType.Embedded, loader) { NativePath = nativePath };
        handle.Resource = file;
        return handle;
    }

    public void Revert(ContentWorkspace workspace)
    {
        Load(workspace);
        Reverted?.Invoke();
    }

    public bool Load(ContentWorkspace workspace)
    {
        var resource = Loader.Load(workspace, this);
        if (resource == null) return false;

        Resource = resource;
        Modified = false;
        return true;
    }

    public bool Save(ContentWorkspace workspace, string? newFilepath = null)
    {
        if (newFilepath == null && !File.Exists(Filepath)) {
            if (HandleType == FileHandleType.Disk && Path.IsPathFullyQualified(Filepath)) {
                Logger.Warn(@$"""
                    The original file seems to have been deleted: {Filepath}.\nRecheck if this was intentional. Saving anyway...");
            } else {
                Logger.Warn(@$"""
                    Unable to save file because it doesn't have a disk file path associated to it: {Filepath}
                    Open its editor and manually save it somewhere (e.g. save as, save to bundle, ...).
                    You can re-open the file through the ""Open files"" menu");
                return false;
            }
        }
        try {
            var success = Loader.Save(workspace, this, newFilepath ?? Filepath);
            if (!success) return false;
            if (newFilepath == null) {
                if (HandleType == FileHandleType.Memory) {
                    // assumption: a disk file has an empty file source, since the FilePath is already the source
                    // a bundle file will always have the bundle name in the source
                    HandleType = FileSource != null && Path.IsPathFullyQualified(FileSource) ? FileHandleType.Disk : FileHandleType.Bundle;
                }
                if (HandleType == FileHandleType.Bundle) {
                    // ensure we also update the bundle diff
                    workspace.SaveBundleFileDiff(this);
                }
                Modified = false;
                Saved?.Invoke();
            }
            return true;
        } catch (Exception e) {
            Logger.Error(e, "Failed to save file " + Filepath);
            return false;
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
        return new FileHandle(filepath, stream, FileHandleType.Disk, loader);
    }

    bool IEquatable<FileHandle>.Equals(FileHandle? other) => ReferenceEquals(other, this);
}

public enum FileHandleType
{
    Disk,
    LooseFile,
    Bundle,
    Memory,
    Embedded,
}

public interface IFileHandleReferenceHolder
{
    bool CanClose { get; }
    IRectWindow? Parent { get; }

    void Close();
}

public interface IFocusableFileHandleReferenceHolder : IFileHandleReferenceHolder
{
    bool CanFocus { get; }
    void Focus();
}