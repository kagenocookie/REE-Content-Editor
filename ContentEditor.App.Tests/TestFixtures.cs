using ContentEditor;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.Tests;

/// <summary>
/// Collection of test fixtures and helpers for content editing tests.
/// </summary>
public static class TestFixtures
{
    /// <summary>
    /// Creates a minimal workspace fixture for testing internal logic.
    /// </summary>
    public static ContentWorkspaceFixture CreateMinimalWorkspace()
    {
        return new ContentWorkspaceFixture(GameIdentifier.dd2, false);
    }

    /// <summary>
    /// Creates a minimal workspace fixture for testing internal logic.
    /// </summary>
    public static ContentWorkspaceFixture CreateWorkspaceWithChunksFolder()
    {
        return new ContentWorkspaceFixture(GameIdentifier.dd2, true);
    }
}
