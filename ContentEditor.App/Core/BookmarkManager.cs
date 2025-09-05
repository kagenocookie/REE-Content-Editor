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
        public bool IsHideDefaults { get; set; } = false;
        // SILVER: Maybe these shouldn't be hardcode into the class? + TODO SILVER: Add more default bookmarks
        public static readonly Dictionary<string, List<BookmarkEntry>> DefaultBookmarks = new() {
            ["re8"] = new List<BookmarkEntry> {
                new BookmarkEntry { Path = "natives/stm/character/ch",      Tags = ["Character"],   Comment = "Folder containing base game characters"},
                new BookmarkEntry { Path = "natives/stm/_ge/character/ch",  Tags = ["Character"],   Comment = "Folder containing 'Gold Edition' characters"},
                new BookmarkEntry { Path = "natives/stm/character/it",      Tags = ["Weapon"],      Comment = "Folder containing base game weapons and items"},
            },
        };
        // SILVER: But these colors should be hardcoded
        public static readonly Dictionary<string, Vector4[]> TagColors = new()
        {
            { "Character", new[] { new Vector4(0.2f, 0.6f, 1f, 0.5f),   new Vector4(0.3f, 0.7f, 1f, 0.8f),      new Vector4(0.1f, 0.5f, 0.9f, 1f)}},
            { "Weapon",    new[] { new Vector4(0.9f, 0.3f, 0.3f, 0.5f), new Vector4(1f, 0.4f, 0.4f, 0.8f),      new Vector4(0.8f, 0.2f, 0.2f, 1f)}},
            { "Item",      new[] { new Vector4(0.3f, 0.8f, 0.3f, 0.5f), new Vector4(0.4f, 0.9f, 0.4f, 0.8f),    new Vector4(0.2f, 0.7f, 0.2f, 1f)}},
            { "Stage",     new[] { new Vector4(0.6f, 0.3f, 0.9f, 0.5f), new Vector4(0.6f, 0.3f, 0.9f, 0.8f),    new Vector4(0.6f, 0.3f, 0.9f, 1f)}},
            { "Misc",      new[] { new Vector4(0.9f, 0.8f, 0.2f, 0.5f), new Vector4(1f, 0.9f, 0.3f, 0.8f),      new Vector4(0.8f, 0.7f, 0.1f, 1f)}},
        };

        public BookmarkManager(string jsonFilePath)
        {
            _jsonFilePath = jsonFilePath;
            LoadBookmarks();
        }
        public IReadOnlyList<BookmarkEntry> GetBookmarks(string game)
        {
            if (_bookmarks.TryGetValue(game, out var list)) {
                return list.AsReadOnly();
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
        private void LoadBookmarks()
        {
            if (!File.Exists(_jsonFilePath)) return;

            var json = File.ReadAllText(_jsonFilePath);
            _bookmarks = JsonSerializer.Deserialize<Dictionary<string, List<BookmarkEntry>>>(json) ?? new Dictionary<string, List<BookmarkEntry>>();
            Logger.Debug($"Bookmarks loaded from {_jsonFilePath}");
        }
        public void SaveBookmarks()
        {
            var json = JsonSerializer.Serialize(_bookmarks, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_jsonFilePath, json);
            Logger.Debug($"Bookmarks saved to {_jsonFilePath}");
        }
    }
}
