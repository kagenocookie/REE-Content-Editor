using ReeLib;

namespace ContentPatcher;

public class DefaultFileLoader<TFileType> : IFileLoader, IFileHandleContentProvider<TFileType> where TFileType : BaseFile
{
    private readonly KnownFileFormats supportedFormat;
    private readonly Func<IResourceFilePatcher>? diffHandler;
    private readonly Func<ContentWorkspace, FileHandler, TFileType> fileFactory;

    protected bool SaveRawStream { get; init; }

    public DefaultFileLoader(KnownFileFormats format, Func<IResourceFilePatcher>? diffHandler = null)
    {
        this.supportedFormat = format;
        this.diffHandler = diffHandler;
        var nonRszConstructor = typeof(TFileType).GetConstructor([typeof(FileHandler)]);
        if (nonRszConstructor != null) {
            fileFactory = (ContentWorkspace ws, FileHandler fh) => (TFileType)nonRszConstructor.Invoke([fh])!;
        } else {
            var rszConstructor = typeof(TFileType).GetConstructor([typeof(RszFileOption), typeof(FileHandler)]);
            if (rszConstructor != null) {
                fileFactory = (ContentWorkspace ws, FileHandler fh) => (TFileType)rszConstructor.Invoke([ws.Env.RszFileOption, fh])!;
            } else {
                throw new NotImplementedException("Unsupported ReeLib base file constructor");
            }
        }
    }

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        if (handle.Resource != null) {
            return ((BaseFileResource<TFileType>)handle.Resource).File.Read() ? handle.Resource : null;
        }

        var file = fileFactory.Invoke(workspace, new FileHandler(handle.Stream, handle.Filepath));
        if (!file.Read()) return null;

        return new BaseFileResource<TFileType>(file);
    }

    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == supportedFormat;

    public TFileType GetFile(FileHandle handle) => ((BaseFileResource<TFileType>)handle.Resource).File;

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
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
            file.Save();
        } else {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            file.WriteTo(outputPath);
        }
        return true;
    }

    public IResourceFilePatcher? CreateDiffHandler() => diffHandler?.Invoke();
}
