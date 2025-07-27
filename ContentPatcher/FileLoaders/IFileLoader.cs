using ReeLib;

namespace ContentPatcher;

public interface IFileLoader
{
    bool CanHandleFile(string filepath, REFileFormat format);
    bool Load(ContentWorkspace workspace, FileHandle handle);
    bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath);
    IResourceFilePatcher? CreateDiffHandler();
}

public interface IFileHandleContentProvider<TFileType> where TFileType : BaseFile
{
    TFileType GetFile(FileHandle handle);
}

public static class FileLoaderExtensions
{
    public static bool SaveOrWriteTo(this BaseFile file, FileHandle handle, string outputPath)
    {
        if (file.FileHandler.FilePath != handle.Filepath) {
            throw new Exception("hmm");
            // file.FileHandler = new FileHandler(handle.Stream, handle.Path);
        }

        if (outputPath == handle.Filepath) {
            return file.Save();
        } else {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            return file.WriteTo(outputPath);
        }
    }
}