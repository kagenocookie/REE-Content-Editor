using System.Numerics;
using System.Text.Json;
using System.Threading;

namespace ContentEditor.App
{
    public class BookmarkManager
    {
        public class BookmarkEntry
        {
            public string Path { get; set; } = string.Empty;
            public List<string> Tags { get; set; } = new();
            private string _comment = string.Empty;
            public string Comment {
                get => _comment;
                set => _comment = value?.Length > 64 ? value[..64] : value ?? string.Empty;
            }
        }
        private readonly string _jsonFilePath;
        private Dictionary<string, List<BookmarkEntry>> _bookmarks = new();
        public bool IsHideBookmarks { get; set; } = false;
        public readonly struct TagInfo {
            public readonly string Icon;
            public readonly Vector4[] Colors;

            public TagInfo(string icon, Vector4[] colors) {
                Icon = icon;
                Colors = colors;
            }
        }

        // SILVER: These colors should stay hardcoded (for now) also maybe 'Stage' should be renamed to 'Scene'
        public static readonly Dictionary<string, TagInfo> TagInfoMap = new() {
            ["Character"] = new TagInfo($"{AppIcons.SI_TagCharacter}",  new[] { new Vector4(0.2f, 0.6f, 1f, 0.5f), new Vector4(0.3f, 0.7f, 1f, 0.8f), new Vector4(0.1f, 0.5f, 0.9f, 1f) }),
            ["Enemy"] = new TagInfo($"{AppIcons.SI_TagCharacter}",      new[] { new Vector4(0.9f, 0.3f, 0.3f, 0.5f), new Vector4(1f, 0.4f, 0.4f, 0.8f), new Vector4(0.8f, 0.2f, 0.2f, 1f) }),
            ["Weapon"] = new TagInfo($"{AppIcons.SI_TagWeapon}",        new[] { new Vector4(0.9f, 0.3f, 0.3f, 0.5f), new Vector4(1f, 0.4f, 0.4f, 0.8f), new Vector4(0.8f, 0.2f, 0.2f, 1f) }),
            ["Item"] = new TagInfo($"{AppIcons.SI_TagItem}",            new[] { new Vector4(0.3f, 0.8f, 0.3f, 0.5f), new Vector4(0.4f, 0.9f, 0.4f, 0.8f), new Vector4(0.2f, 0.7f, 0.2f, 1f) }),
            ["Stage"] = new TagInfo($"{AppIcons.SI_FileType_SCN}",      new[] { new Vector4(0.6f, 0.3f, 0.9f, 0.5f), new Vector4(0.6f, 0.3f, 0.9f, 0.8f), new Vector4(0.6f, 0.3f, 0.9f, 1f) }),
            ["Misc"] = new TagInfo("Misc",                              new[] { new Vector4(0.9f, 0.8f, 0.2f, 0.5f), new Vector4(1f, 0.9f, 0.3f, 0.8f), new Vector4(0.8f, 0.7f, 0.1f, 1f) }),// TODO SILVER: Make Misc icon
            ["UI"] = new TagInfo($"{AppIcons.SI_FileType_GUI}",         new[] { new Vector4(1f, 0.4f, 0.1f, 0.5f), new Vector4(1f, 0.4f, 0.1f, 0.8f), new Vector4(1f, 0.4f, 0.1f, 1f) }),
            ["DLC"] = new TagInfo($"{AppIcons.SI_TagDLC}",              new[] { new Vector4(1f, 0f, 1f, 0.5f), new Vector4(1f, 0f, 1f, 0.8f), new Vector4(1f, 0f, 1f, 1f) }),
        };

        public BookmarkManager(string jsonFilePath)
        {
            _jsonFilePath = jsonFilePath;
            LoadBookmarks();
        }
        public IList<BookmarkEntry> GetBookmarks(string game)
        {
            if (_bookmarks.TryGetValue(game, out var list)) {
                return list;
            }
            return Array.Empty<BookmarkEntry>();
        }
        public void AddBookmark(string game, string path, IEnumerable<string>? tags = null, string? comment = null)
        {
            if (!_bookmarks.TryGetValue(game, out var list)) {
                _bookmarks[game] = list = new List<BookmarkEntry>();
            }

            if (!list.Any(b => b.Path == path)) {
                list.Add(new BookmarkEntry {
                    Path = path,
                    Tags = tags?.ToList() ?? new List<string>(),
                    Comment = comment ?? string.Empty
                });
                SaveBookmarks();
            }
        }
        public void RemoveBookmark(string game, string path)
        {
            if (_bookmarks.TryGetValue(game, out var list)) {
                list.RemoveAll(b => b.Path == path);
                SaveBookmarks();
            }
        }
        public void ClearBookmarks(string game)
        {
            if (_bookmarks.TryGetValue(game, out var list)) {
                list.Clear();
                SaveBookmarks();
            }
        }
        public bool IsBookmarked(string game, string path)
        {
            if (!_bookmarks.TryGetValue(game, out var list)) {
                return false;
            }
            return list.Any(b => b.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        }
        private void LoadBookmarks()
        {
            if (!File.Exists(_jsonFilePath)) return;

            var json = File.ReadAllText(_jsonFilePath);
            _bookmarks = JsonSerializer.Deserialize<Dictionary<string, List<BookmarkEntry>>>(json) ?? new Dictionary<string, List<BookmarkEntry>>();
        }
        public void SaveBookmarks()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_jsonFilePath)!);
            var json = JsonSerializer.Serialize(_bookmarks, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_jsonFilePath, json);
        }
    }
}
