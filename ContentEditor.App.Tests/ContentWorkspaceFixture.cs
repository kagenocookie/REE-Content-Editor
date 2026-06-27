using System.Diagnostics;
using System.Runtime.CompilerServices;
using ContentEditor;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.Tests;

/// <summary>
/// Base test fixture that provides a configured ContentWorkspace for testing.
/// </summary>
public sealed class ContentWorkspaceFixture : IDisposable
{
    /// <summary>
    /// The configured workspace instance for use in tests.
    /// </summary>
    public ContentWorkspace Workspace { get; private set; } = null!;

    /// <summary>
    /// Creates a workspace with the specified game identifier.
    /// This will be minimally configured - suitable for testing internal logic without game assets.
    /// </summary>
    /// <param name="game">The game identifier to use for the workspace.</param>
    public ContentWorkspaceFixture(GameIdentifier game, bool setupResourcePath)
    {
        ResourceRepository.DisableOnlineUpdater = true;
        ResourceRepository.LocalResourceRepositoryFilepath = Path.Combine(Directory.GetCurrentDirectory(), "resources/resource-cache.json");
        var config = GameConfig.CreateFromRepository(game);
        if (setupResourcePath) {
            config.ChunkPath = Path.Combine(Path.GetTempPath(), "REE-Content-Editor-tests/" + Guid.NewGuid().ToString());
            config.GamePath = config.ChunkPath;
            Directory.CreateDirectory(config.ChunkPath);
        } else {
            config.GamePath = "resources/" + game.name;
        }

        Workspace = new ContentWorkspace(
            new Workspace(config) { AllowUsePackedFiles = false },
            new PatchDataContainer("!")
        );
    }

    public int SaveAllModified()
    {
        return Workspace.SaveModifiedFiles();
    }

    public Bundle CreateTestBundle([CallerMemberName] string? name = null)
    {
        var bundle = Workspace.BundleManager.CreateBundle(name!);
        Debug.Assert(bundle != null);
        Workspace.SetBundle(bundle.Name);
        return bundle;
    }

    public (TFile file, ResourceListItem entry) CreateBundleFile<TFile>(string localPath, Action<ResourceListItem>? modResource = null) where TFile : BaseFile
    {
        if (Workspace.CurrentBundle == null) {
            CreateTestBundle(localPath);
        }
        Debug.Assert(Workspace.CurrentBundle != null);
        var bundleFolder = Workspace.BundleManager.GetBundleFolder(Workspace.CurrentBundle);
        var file = CreateFile<TFile>(Path.Combine(bundleFolder, localPath));
        var res = Workspace.CurrentBundle.AddResource(localPath, localPath);
        modResource?.Invoke(res);
        return (file, res);
    }

    public TFile CreateFile<TFile>(string path) where TFile : BaseFile
    {
        var fullPath = Path.Combine(Workspace.Env.Config.ChunkPath, "natives/STM", path);
        var file = DefaultFileLoader<TFile>.GetFileConstructor().Invoke(Workspace, new FileHandler(new MemoryStream(), fullPath));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        file.Save();
        return file;
    }

    public (TFile file, FileHandle handle) LoadTempFile<TFile>(string path) where TFile : BaseFile
    {
        var fullPath = Path.Combine(Workspace.Env.Config.ChunkPath, "natives/STM", path);
        if (!Workspace.ResourceManager.TryForceLoadFile(fullPath, out var handle)) {
            Assert.Fail("Failed to load file " + path);
        }

        var file = handle!.GetFile<TFile>();
        return (file, handle);
    }

    public void Dispose()
    {
        var chunks = Workspace.Env.Config.ChunkPath;
        if (Directory.Exists(chunks) && chunks.StartsWith(Path.GetTempPath())) {
            Directory.Delete(chunks, true);
        }

        Workspace.Dispose();
    }
}
