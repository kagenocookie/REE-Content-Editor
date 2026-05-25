# File System Details

## RE Engine File System

RE Engine uses a custom archive format called **PAK** that differs from standard ZIP in a critical way. Unlike ZIP which stores file paths as strings, RE Engine PAK files store files **without filenames**, instead computing two 32-bit hashes of the lowercase and upper case paths, which also makes file paths case insensitive. The hash is computed by concatenating a platform prefix (`natives/STM/`, `natives/x64/`, `natives/EGS/`, ...), the actual path, the file format version, and optionally additional localization suffixes for files that have multiple language versions (`.x64.en`, ...).

All modding tools including Content Editor use a **list file** to bridge PAK hashes with file paths. A list file contains all known full pak paths used by a game. This is needed because finding the paths otherwise is a non-trivial operation that can take a while.

To account for this path handling, Content Editor treats paths in terms of different path formats with specific names and purposes:

- **Natives path** (example: `natives/STM/Character/Ch/Ch0100/ch0100_body.tex.251111100.x64.en`)
    - The full path used to compute the PAK hash and for file resolution
    - This is currently used by Content Editor during patching for generating the full output paths that are required for modded files to work (either PAK or loose files), as well as in the list files since the full path is needed for PAK file contents
- **Target path** (example: `Character/Ch/Ch0100/ch0100_body.tex.251111100.x64.en`)
    - The full ingame file resolution path except without the platform prefix
    - This is the main path type Content Editor uses to refer to files for accessing and for output file generation
    - The platform prefix is usually meaningless for the mod developer, so removing it means less things to keep aware of
    - It also makes it easier to swap platforms without needing to modify the source project file listing
- **Resource path** (example: `Character/Ch/Ch0100/ch0100_body.tex`)
    - The normal resource file path as stored in resource files
    - No platform prefix and no file version extensions
    - All RE Engine resources files only ever use this format to refer to other files
    - The engine automatically appends the active platform prefix and versions during the resource load function
- **Bundle local path** (example: `my_sub_folder/ch0100_body.tex.251111100`)
    - Path within bundle, relative to the bundle folder
    - Files can be moved around arbitrarily within a bundle folder, and the actual output path can be changed anytime through the Bundle Manager
    - Because the majority of mods only ever modify a few files, letting the user have everything stored at the base folder makes files easier to work with

## Content Editor File System

Content Editor wraps RE-Engine-Lib to provide a unified virtual file system. When resolving a file path, the resource manager looks for files in this order:

1. **Active bundle files** - Modified files in the active mod bundle (includes raw file edits and entity-based partial modifications)
2. **Loose files** - Files on disk outside PAK archives in the game's natives/ folder (if enabled via settings)
3. **PAK files** - Unmodified files from original game PAK archives

The **ResourceManager** provides a unified view of the virtual file system by merging all of the above into just the final resolved file. Each file is also annotated with the source of how it got resolved.

By default, only base game files resolve. To get the editor to automatically load custom modded files (e.g. material textures, mesh paths, ...) instead of their vanilla counterparts, a bundle must be created with the modified files, which allows the tool to find the correct modified files instead.

```csharp
public enum FileHandleType
{
    Disk,           // Loaded from an arbitrary disk path
    Bundle,         // Loaded from a bundle
    LooseFile,      // Loaded from a loose file in the game's natives/ folder
    Memory,         // Loaded as a transient file that only exists in memory - can be either from a PAK file or embedded inside another file. Not saveable without the user specifying a save path.
    New,            // Newly created from scratch file. Not saveable without the user specifying a save path.
}
```

### Resource Listing

Bundles maintain a resource file listing that maps local bundle paths to their target paths. The resource listing tracks:
- **Local bundle path** - Path within the bundle folder, stored as the dictionary key.
- **Target path** - The path the file gets patched to for use ingame.
- **Replace** - whether the file should full replace a file or use partial patching instead where available.
- **Diff** - the computed diff of the bundle file compared to its original vanilla counterpart as a JSON object. Used for partial patching.

Whenever a file is moved, its local path also needs to be updated in the bundle's resource listing accordingly.

## File Operations

### Reading Files

```csharp
// Resolve file path and read the file (checks bundle, loose files, PAK, base)
if (workspace.ResourceManager.TryResolveGameFile("items/item.msg", out var handle))
{
    // Get file contents (you need to know the specific file format you're expecting)
    if (handle.Format.format != KnownFileFormats.Message) return;
    var msg = handle.GetFile<MsgFile>();

    Console.WriteLine(msg.FindEntryByKey("item_001_name").GetMessage(Language.English));

    handle.Modified = true;
}

// Or use workspace Env directly to get a raw file stream. This reads it fresh from PAK or loose files, bypassing bundles and the resource system and generally not intended for file modification through Content Editor, but can be useful in some situations.
var file = workspace.Env.FindSingleFile("items/item.msg");
if (file != null)
{
    var msg = new MsgFile(new FileLoader(file, "items/item.msg"));
    msg.Read();
}
```

### File Enumeration

```csharp
// Get all files with specific extension from Workspace
var files = workspace.Env.GetFilesWithExtension("user");
foreach (var file in files)
{
    Console.WriteLine(file);
}

// Find files from current workspace based on the active list file
var listFile = workspace.Env.ListFile;
var allFiles = listFile.GetFiles(".*");
var meshTexFiles = listFile.GetFiles("**\.(mesh|tex)\.**");
```
