using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.Tests;

/// <summary>
/// Tests for ResourceManager and file handling operations.
/// </summary>
[TestFixture]
public class ResourceManagerTests
{
    private ContentWorkspaceFixture _workspaceFixture = null!;
    private ContentWorkspace _workspace = null!;

    [SetUp]
    public void Setup()
    {
        _workspaceFixture = TestFixtures.CreateWorkspaceWithChunksFolder();
        _workspace = _workspaceFixture.Workspace;
    }

    [TearDown]
    public void TearDown()
    {
        if (_workspaceFixture != null)
        {
            _workspaceFixture?.Dispose();
        }
        _workspace = null!;
        _workspaceFixture = null!;
    }

    [Test]
    public void ResourceManager_CreateAndReadSampleFile()
    {
        var msg = _workspaceFixture.CreateFile<MsgFile>("test.msg.22");
        _workspace.ResourceManager.TryGetOrLoadFile("test.msg.22", out var file);

        Assert.That(file, Is.Not.Null);
    }

    [Test]
    public void ResourceManager_BundleAndRawFile_LoadBundleAndResolveDefaultBaseFile()
    {
        var bundle = _workspaceFixture.CreateTestBundle();

        var rawMsg = _workspaceFixture.CreateFile<MsgFile>("test.msg.22");
        var guid = Guid.NewGuid();
        rawMsg.AddNewEntry("key", guid)
            .SetMessage(ReeLib.Msg.Language.Japanese, "Unchanged")
            .SetMessage(ReeLib.Msg.Language.English, "Original");
        rawMsg.Save();

        var bundleMsg = _workspaceFixture.CreateBundleFile<MsgFile>("test.msg.22");
        bundleMsg.AddNewEntry("key", guid)
            .SetMessage(ReeLib.Msg.Language.Japanese, "Unchanged")
            .SetMessage(ReeLib.Msg.Language.English, "Updated");
        bundleMsg.Save();

        var (file, resource) = GenerateFileDiff("test.msg.22");

        Assert.That(file.FileSource, Is.EqualTo(bundle.Name));
        Assert.That(file.HandleType, Is.EqualTo(FileHandleType.Bundle));
        Assert.That(resource.Diff, Is.Not.Null);
        Assert.That(resource.Diff.ToJsonString(), Contains.Substring("Updated"));
        Assert.That(resource.Diff.ToJsonString(), !Contains.Substring("Unchanged"));
    }

    [Test]
    public void ResourceManager_BundleCustomFile_EmptyDiff()
    {
        var bundle = _workspaceFixture.CreateTestBundle();

        var bundleMsg = _workspaceFixture.CreateBundleFile<MsgFile>("test.msg.22");
        var bundleEntry = bundleMsg.AddNewEntry("key", Guid.NewGuid());
        bundleEntry.SetMessage(ReeLib.Msg.Language.Japanese, "text");
        bundleMsg.Save();

        var (file, resource) = GenerateFileDiff("test.msg.22");

        Assert.That(file.FileSource, Is.EqualTo(bundle.Name));
        Assert.That(file.HandleType, Is.EqualTo(FileHandleType.Bundle));
        Assert.That(resource.Diff, Is.Null);
    }

    [Test]
    public void ResourceManager_BundleAndRawFileCustomBasePath_ResolveCorrectBaseFile()
    {
        var bundle = _workspaceFixture.CreateTestBundle();

        var rawMsg = _workspaceFixture.CreateFile<MsgFile>("test_original.msg.22");
        var guid = Guid.NewGuid();
        rawMsg.AddNewEntry("key", guid).SetMessage(ReeLib.Msg.Language.English, "Original");
        rawMsg.Save();

        var bundleMsg = _workspaceFixture.CreateBundleFile<MsgFile>("test.msg.22", res => res.BaseFile = "test_original.msg.22");
        bundleMsg.AddNewEntry("key", guid).SetMessage(ReeLib.Msg.Language.English, "Updated");
        bundleMsg.Save();

        var (file, resource) = GenerateFileDiff("test.msg.22");

        Assert.That(file.FileSource, Is.EqualTo(bundle.Name));
        Assert.That(file.HandleType, Is.EqualTo(FileHandleType.Bundle));
        Assert.That(resource.Diff, Is.Not.Null);
        Assert.That(resource.Diff.ToJsonString(), Contains.Substring("Updated"));
        Assert.That(resource.Diff.ToJsonString(), !Contains.Substring("Unchanged"));
    }

    [Test]
    public void ResourceManager_BundleFileLoadFailure_RecoversFromBaseFileAndDiff()
    {
        var bundle = _workspaceFixture.CreateTestBundle();

        var rawMsg = _workspaceFixture.CreateFile<MsgFile>("test.msg.22");
        var guid = Guid.NewGuid();
        rawMsg.AddNewEntry("key", guid).SetMessage(ReeLib.Msg.Language.English, "Original");
        rawMsg.Save();

        var bundleMsg = _workspaceFixture.CreateBundleFile<MsgFile>("test.msg.22");
        bundleMsg.AddNewEntry("key", guid).SetMessage(ReeLib.Msg.Language.English, "Updated");
        bundleMsg.Save();

        GenerateFileDiff("test.msg.22");
        _workspace.ResourceManager.CloseAllFiles();

        File.WriteAllText(bundleMsg.FileHandler.FilePath!, "");

        if (!_workspace.ResourceManager.TryResolveGameFile("test.msg.22", out var file)) {
            Assert.Fail("Failed to resolve patched bundle file");
            return;
        }

        Assert.That(file.GetFile<MsgFile>().FindEntryByKey("key")?.GetMessage(ReeLib.Msg.Language.English), Is.EqualTo("Updated"));
    }


    private (FileHandle file, ResourceListItem resource) GenerateFileDiff(string localPath, Bundle? bundle = null)
    {
        if (_workspace.ResourceManager.TryGetOrLoadFile(localPath, out var file)) {
            _workspace.SaveBundleFileDiff(file);
            if ((bundle ?? _workspace.CurrentBundle!).TryFindResource(file.TargetPath!, out var resource)) {
                return (file, resource);
            }
        }
        Assert.Fail("Failed to generate bundle file diff");
        return default;
    }
}
