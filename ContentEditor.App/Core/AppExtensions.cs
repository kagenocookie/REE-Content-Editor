using Assimp;
using ReeLib;

namespace ContentEditor.App;

public static class AppExtensions
{
    public static string GetFormatIDFromExtension(this AssimpContext context, string extension)
    {
        foreach (var fmt in context.GetSupportedExportFormats()) {
            if (fmt.FileExtension == extension) {
                return fmt.FormatId;
            }
        }

        throw new NotImplementedException("Unsupported export format " + extension);
    }

    public static bool IsDefaultReplacedBundleResource(this KnownFileFormats format)
        => format is KnownFileFormats.UserData or KnownFileFormats.Message ? false : true;

    public static bool ComponentAvailable<T>(this Workspace env) where T : IFixedClassnameComponent
    {
        return env.RszParser.GetRSZClass(T.Classname) != null;
    }
}