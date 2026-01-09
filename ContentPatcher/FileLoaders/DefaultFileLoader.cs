using ReeLib;

namespace ContentPatcher;

public class DefaultFileLoader<TFileType> : IFileLoader, IFileHandleContentProvider<TFileType> where TFileType : BaseFile
{
    private readonly KnownFileFormats supportedFormat;
    private readonly Func<IResourceFilePatcher>? diffHandler;
    private readonly Func<ContentWorkspace, FileHandler, TFileType> fileFactory;

    protected bool SaveRawStream { get; init; }
    protected bool ClearStreamOnSave { get; init; }

    public DefaultFileLoader(KnownFileFormats format, Func<IResourceFilePatcher>? diffHandler = null)
    {
        this.supportedFormat = format;
        this.diffHandler = diffHandler;
        fileFactory = GetFileConstructor();
    }

    public static Func<ContentWorkspace, FileHandler, TFileType> GetFileConstructor()
    {
        var cons = GetFileConstructor(typeof(TFileType));
        return (w, fh) => (TFileType)cons(w, fh);
    }

    public static Func<ContentWorkspace, FileHandler, BaseFile> GetFileConstructor(Type type)
    {
        var nonRszConstructor = type.GetConstructor([typeof(FileHandler)]);
        if (nonRszConstructor != null) {
            return (ContentWorkspace ws, FileHandler fh) => (BaseFile)nonRszConstructor.Invoke([fh])!;
        } else {
            var rszConstructor = type.GetConstructor([typeof(RszFileOption), typeof(FileHandler)]);
            if (rszConstructor != null) {
                return (ContentWorkspace ws, FileHandler fh) => (BaseFile)rszConstructor.Invoke([ws.Env.RszFileOption, fh])!;
            } else {
                throw new NotImplementedException("Unsupported ReeLib base file constructor");
            }
        }
    }

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        if (handle.Resource != null) {
            var resource = (BaseFileResource<TFileType>)handle.Resource;
            bool isEmptyOrInvalid = true;
            try {
                isEmptyOrInvalid = resource.File.FileHandler.FileSize() > 0;
            } catch {
                // ignore - stream was probably disposed at some point, try force reload
            }
            if (isEmptyOrInvalid && File.Exists(resource.File.FileHandler.FilePath)) {
                resource.File.FileHandler = new FileHandler(resource.File.FileHandler.FilePath);
            }
            return resource.File.Read() ? handle.Resource : null;
        }

        var file = fileFactory.Invoke(workspace, new FileHandler(handle.Stream, handle.Filepath));
        if (!file.Read()) return null;

        return new BaseFileResource<TFileType>(file);
    }

    public IResourceFile? CreateNewFile(ContentWorkspace workspace, FileHandle handle)
    {
        var file = GetFileConstructor().Invoke(workspace, new FileHandler(handle.Stream, handle.Filepath));
        file.Write();
        return new BaseFileResource<TFileType>(file);
    }

    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == supportedFormat;

    public TFileType GetFile(FileHandle handle) => ((BaseFileResource<TFileType>)handle.Resource).File;

    public virtual bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        if (SaveRawStream) {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var fs = File.Create(outputPath);
            if (handle.Stream.CanSeek) handle.Stream.Seek(0, SeekOrigin.Begin);
            handle.Stream.CopyTo(fs);
            return true;
        }
        var file = GetFile(handle);
        if (outputPath == handle.Filepath) {
            if (ClearStreamOnSave) {
                file.FileHandler.Stream.SetLength(0);
            }
            file.Save();
        } else {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            // write to a temp memory stream and not directly to disk to speed it up
            // WriteTo will then dump the memory stream to the outputPath
            var handler = new FileHandler(new MemoryStream(), outputPath);
            return file.WriteTo(handler);
        }
        return true;
    }

    public IResourceFilePatcher? CreateDiffHandler() => diffHandler?.Invoke();
}

public class DefaultFileMultiLoader<TFileType> : DefaultFileLoader<TFileType>, IFileLoader where TFileType : BaseFile
{
    public KnownFileFormats[] Formats { get; }

    public DefaultFileMultiLoader(params KnownFileFormats[] formats) : base(formats[0])
    {
        Formats = formats;
    }

    bool IFileLoader.CanHandleFile(string filepath, REFileFormat format) => Formats.Contains(format.format);
}
