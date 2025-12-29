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
        public struct TagInfo
        {
            public string Icon;
            public Func<Vector4[]> Colors;

            public TagInfo(string icon, Func<Vector4[]> colors)
            {
                Icon = icon;
                Colors = colors;
            }
        }
        public static Dictionary<string, TagInfo> TagInfoMap = new() {
            ["Animation"] = new TagInfo($"{AppIcons.SI_Animation}", () => new[] { Colors.TagAnimation, Colors.TagAnimationHovered, Colors.TagAnimationSelected }),
            ["Character"] = new TagInfo($"{AppIcons.SI_TagCharacter}", () => new[] { Colors.TagCharacter, Colors.TagCharacterHovered, Colors.TagCharacterSelected }),
            ["DLC"] = new TagInfo($"{AppIcons.SI_TagDLC}", () => new[] { Colors.TagDLC, Colors.TagDLCHovered, Colors.TagDLCSelected }),
            ["Enemy"] = new TagInfo($"{AppIcons.SI_TagCharacter}", () => new[] { Colors.TagEnemy, Colors.TagEnemyHovered, Colors.TagEnemySelected }),
            ["Item"] = new TagInfo($"{AppIcons.SI_TagItem}", () => new[] { Colors.TagItem, Colors.TagItemHovered, Colors.TagItemSelected }),
            ["Misc"] = new TagInfo($"{AppIcons.SI_TagMisc}", () => new[] { Colors.TagMisc, Colors.TagMiscHovered, Colors.TagMiscSelected }),
            ["Prefab"] = new TagInfo($"{AppIcons.SI_FileType_PFB}", () => new[] { Colors.TagPrefab, Colors.TagPrefabHovered, Colors.TagPrefabSelected }),
            ["Stage"] = new TagInfo($"{AppIcons.SI_FileType_SCN}", () => new[] { Colors.TagStage, Colors.TagStageHovered, Colors.TagStageSelected }),
            ["UI"] = new TagInfo($"{AppIcons.SI_FileType_GUI}", () => new[] { Colors.TagUI, Colors.TagUIHovered, Colors.TagUISelected }),
            ["Weapon"] = new TagInfo($"{AppIcons.SI_TagWeapon}", () => new[] { Colors.TagWeapon, Colors.TagWeaponHovered, Colors.TagWeaponSelected }),
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
