namespace ContentPatcher;

using System.Text.Json.Nodes;

/// <summary>
/// Represents a file diff resolver and patcher. <br/>
/// Exactly one instance of this is created per file and will be kept active within the <see cref="ResourceManager"/> for the whole patching process.
/// </summary>
public interface IResourceFilePatcher
{
    /// <summary>
    /// Loads the base file for this handler. The data from the file should be kept in memory as it will be re-read during the diffing process.
    /// </summary>
    IResourceFile LoadBase(ContentWorkspace workspace, FileHandle file);
    /// <summary>
    /// Calculate a diff JSON for a file's changes on top of the previously loaded base file.
    /// </summary>
    JsonNode? FindDiff(FileHandle file);
    /// <summary>
    /// Apply a diff to the previously loaded base file.
    /// </summary>
    void ApplyDiff(JsonNode diff);
}

/// <summary>
/// Represents a file resource.
/// </summary>
public interface IResourceFile
{
    void WriteTo(string filepath);
}
