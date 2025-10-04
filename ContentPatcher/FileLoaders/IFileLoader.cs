using ReeLib;

namespace ContentPatcher;

public interface IFileLoader
{
    /// <summary>
    /// The priority for picking this loader for a file. Lower number takes precedence before higher numbers.
    /// </summary>
    int Priority => 50;
    /// <summary>
    /// Check whether this loader can load the given file.
    /// </summary>
    bool CanHandleFile(string filepath, REFileFormat format);
    /// <summary>
    /// Load a file. The file handle can already contain a previously loaded resource.
    /// </summary>
    IResourceFile? Load(ContentWorkspace workspace, FileHandle handle);
    /// <summary>
    /// Save the file to disk.
    /// </summary>
    bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath);
    /// <summary>
    /// Create a partial diff handler for files supported by this file loader. Can return null if this is not supported.
    /// </summary>
    IResourceFilePatcher? CreateDiffHandler();
}

public interface IFileHandleContentProvider<TFileType> where TFileType : class
{
    TFileType GetFile(FileHandle handle);
}

public static class FileLoaderExtensions
{
    public static bool SaveOrWriteTo(this BaseFile file, FileHandle handle, string outputPath)
    {
        if (file.FileHandler.FilePath != handle.Filepath) {
            // in some cases for a patched file flow, the filepath on the handle may have changed
            file.FileHandler.Dispose();
            file.FileHandler = new FileHandler(new MemoryStream(), outputPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (outputPath == handle.Filepath) {
            return file.Save();
        } else {
            // write to a temp memory stream and not directly to disk to speed it up
            // WriteTo will then dump the memory stream to the outputPath
            var handler = new FileHandler(new MemoryStream(), outputPath);
            return file.WriteTo(handler);
        }
    }

    /// <summary>
    /// Executes a write of the given file into a new memory stream and re-reads the file from it, returning the new file. Effectively does a full binary clone of the data.
    /// </summary>
    /// <returns>The cloned file.</returns>
    public static TFile RewriteClone<TFile>(this TFile file, ContentWorkspace workspace) where TFile : BaseFile
    {
        var stream = new MemoryStream();
        var handler = new FileHandler(stream, file.FileHandler.FilePath);
        file.WriteTo(handler, false);
        handler.Seek(0);
        var newFile = DefaultFileLoader<TFile>.GetFileConstructor().Invoke(workspace, handler);
        newFile.Read();
        return newFile;
    }
}