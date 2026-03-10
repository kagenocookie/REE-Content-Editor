using ContentEditor.App;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.Reversing;

internal static class PrefabLister
{
    public static List<BookmarkManager.BookmarkEntry>? GenerateFileSets(ContentWorkspace workspace)
    {
        try {
            if (workspace.Game.GameEnum == GameName.re9) {
                return GenerateRE9(workspace);
            }

            Logger.Warn("Bookmark generation not currently available for " + Languages.TranslateGame(workspace.Game.name));
            return null;
        } catch (Exception e) {
            Logger.Error(e.Message);
            return null;
        }
    }

    private static List<BookmarkManager.BookmarkEntry> GenerateRE9(ContentWorkspace workspace)
    {
        var itemEntities = workspace.ResourceManager.GetEntityInstances("item_data");
        var items = new List<BookmarkManager.BookmarkEntry>();
        foreach (var (id, item) in itemEntities) {
            var data = item.Get<RSZObjectResource>("data");
            var path = data?.Instance.Get(RszFieldCache.RE9.ItemData._LayouterPrefab)?.Get(RszFieldCache.Prefab.Path);
            if (!string.IsNullOrEmpty(path)) {
                var resolvedPath = workspace.Env.AppendFileVersion(workspace.Env.PrependBasePath(path));
                items.Add(new BookmarkManager.BookmarkEntry() {
                    Comment = item.Get<MessageData>("name")?.Get(ReeLib.Msg.Language.English) ?? PathUtils.GetFilenameWithoutExtensionOrVersion(path).ToString(),
                    Path = resolvedPath,
                    Tags = ["Item", "Prefab"]
                });
                continue;
            }

            Logger.Warn("Couldn't get prefab filepath for item " + id + " / " + item);
        }
        return items;
    }
}
