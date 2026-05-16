using ContentPatcher;

namespace ContentEditor.App;

public class BookmarkHolder(
    ContentWorkspace workspace,
    string defaultPath = "default_bookmarks_pak.json",
    string userPath = "bookmarks_pak.json",
    bool useGlobalDefaults = false,
    bool isFilepathBookmarks = true)
{
    public BookmarkManager Defaults { get; } = new BookmarkManager(
        Path.Combine(AppConfig.Instance.ConfigBasePath, useGlobalDefaults ? "global" : workspace.Game.name, defaultPath),
        isFilepathBookmarks ? default : workspace.Platform
    );

    public BookmarkManager User { get; } = new BookmarkManager(
        Path.Combine(AppConfig.Instance.BookmarksFilepath.Get()!, userPath),
        isFilepathBookmarks ? default : workspace.Platform
    );

    public bool IsEmpty => !Defaults.GetBookmarks(workspace.Game.name).Any() && !User.GetBookmarks(workspace.Game.name).Any();

    public bool HasAny(string tag) => Defaults.GetBookmarks(workspace.Game.name).Any(b => b.Tags.Contains(tag)) || User.GetBookmarks(workspace.Game.name).Any(b => b.Tags.Contains(tag));

    public IEnumerable<BookmarkManager.BookmarkEntry> GetByTag(string tag)
    {
        foreach (var item in User.GetBookmarks(workspace.Game.name)) {
            if (!item.Tags.Contains(tag)) continue;
            yield return item;
        }

        foreach (var item in Defaults.GetBookmarks(workspace.Game.name)) {
            if (!item.Tags.Contains(tag)) continue;
            yield return item;
        }
    }
}
