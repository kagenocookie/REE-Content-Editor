namespace ContentEditor.App.FileLoaders;

[Serializable]
public class FileImportException : Exception
{
    public FileImportException() : base("File could not be imported")
    {
    }

    public FileImportException(string? message) : base(message)
    {
    }

    public FileImportException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}
