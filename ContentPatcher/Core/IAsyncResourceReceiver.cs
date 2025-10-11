namespace ContentPatcher;

public interface IAsyncResourceReceiver
{
    public void ReceiveResource(FileHandle file, Action<FileHandle> callback);
}