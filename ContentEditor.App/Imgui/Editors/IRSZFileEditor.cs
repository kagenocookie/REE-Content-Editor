using ReeLib;

namespace ContentEditor.App.ImguiHandling;

public interface IRSZFileEditor
{
    RSZFile? GetRSZFile();

    IEnumerable<RSZFile> GetRSZFiles()
    {
        var rsz = GetRSZFile();
        if (rsz == null) return [];

        return [rsz];
    }
}
