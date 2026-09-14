namespace ContentPatcher;

public class PatchParameters
{
    /// <summary>
    /// The path to which to output the patched files. Can be a folder or a full file path for a target .pak file.
    /// </summary>
    public string OutputFilepath { get; set; } = "";

    /// <summary>
    /// What type of output to generate.
    /// </summary>
    public PatchOutputType OutputType { get; set; }

    /// <summary>
    /// Whether to export files in a PAK file. If false, all files will be output as loose. Any reframework/ files will still be published as loose alongside the PAK file either way.
    /// </summary>
    public bool ExportAsPak { get; set; }

    /// <summary>
    /// Whether to force rescan bundle settings and reload all bundle data.
    /// </summary>
    public bool ReloadBundles { get; set; } = true;

    /// <summary>
    /// Include the main bundle.json in publish output.
    /// </summary>
    public bool IncludeBundleJsonForPublish { get; set; }

    /// <summary>
    /// Whether to include the patch metadata JSON. This can be used to accurately revert previous patch changes.
    /// </summary>
    public bool IncludePatchMetadataJson { get; set; }

    /// <summary>
    /// Whether to prefer using symbolic links for output files.
    /// </summary>
    public bool AllowSymlinks { get; set; }

    /// <summary>
    /// Whether to force storing GDeflate based textures in sub pak files. Mainly exists as compatibility in case issues come up in newer game versions.
    /// </summary>
    public bool StoreGDeflateTexturesAsSubPak { get; set; } = false;
}

public enum PatchOutputType
{
    /// <summary>
    /// Patch all modified files into the game folder for local play / testing.
    /// </summary>
    GamePatch,
    /// <summary>
    /// Publish a mod/bundle with only the required output files.
    /// </summary>
    Publish,
    /// <summary>
    /// Publish a mod/bundle in the raw bundle format that can be used as-is in the desktop Content Editor.
    /// </summary>
    BundlePublish,
}
