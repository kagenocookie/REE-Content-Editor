using ContentEditor.Core;

namespace ContentPatcher;

public class HierarchyTypeList<T>(string name) where T : class
{
    private readonly List<object> items = new();
    private readonly Dictionary<string, object> itemsDict = new();
    private readonly Dictionary<string, string> _friendlyNameOverrides = new();
    private HierarchyTypeList<T>? _parentList { get; init; }

    private string[]? _names;
    public string[] Names {
        get {
            if (_names == null) _names = items.Select(it => (it as HierarchyTypeList<T>)?.Name ?? itemsDict.First(kv => kv.Value == it).Key).ToArray();
            return _names;
        }
    }
    private string[]? _friendlyNames;
    public string[] FriendlyNames {
        get {
            if (_friendlyNames == null) _friendlyNames = Names.Select(n => _friendlyNameOverrides.GetValueOrDefault(n) ?? n.PrettyPrint()).ToArray();
            return _friendlyNames;
        }
    }

    private object[]? _values;
    public object[] Items {
        get {
            if (_values == null) _values = Names.Select(name => itemsDict[name]).ToArray();
            return _values;
        }
    }
    public int Count => items.Count;

    /// <returns>`itemOrList` is either a `T` or a `EntityTypeList<T>`</returns>
    public (object itemOrList, string name) Get(int index) => (items[index], Names[index]);

    public string Name { get; } = name;

    public string GetFriendlyName(string type)
    {
        if (_friendlyNameOverrides.TryGetValue(type, out var str)) return str;

        var dot = type.LastIndexOf('.');
        if (dot == -1) {
            return type.PrettyPrint();
        }
        return type.Substring(dot + 1).PrettyPrint();
    }

    public void Add(HierarchyTypeList<T> sublist)
    {
        if (itemsDict.ContainsKey(sublist.Name)) {
            throw new Exception("Duplicate entity type registration attempt: " + sublist.Name);
        }

        items.Add(sublist);
    }

    public string Add(string path, T entityType, string? friendlyName = null)
    {
        var dot = path.IndexOf('.');
        if (dot == -1) {
            if (itemsDict.ContainsKey(path)) {
                throw new Exception("Duplicate entity type registration attempt: " + path);
            }

            items.Add(entityType);
            itemsDict[path] = entityType;
            if (friendlyName != null) {
                _friendlyNameOverrides[path] = friendlyName;
            }
            return path;
        } else {
            if (friendlyName != null) {
                _friendlyNameOverrides[path] = friendlyName;
            }
            var subname = path.Substring(0, dot);
            if (!itemsDict.TryGetValue(subname, out var subitem)) {
                itemsDict[subname] = subitem = new HierarchyTypeList<T>(subname);
                items.Add(subitem);
            } else if (subitem is not HierarchyTypeList<T>) {
                throw new Exception("Invalid entity type path - conflict found: " + path);
            }

            var sublist = subitem as HierarchyTypeList<T>;
            if (sublist == null) {
                sublist = new HierarchyTypeList<T>(subname) { _parentList = this };
            }
            return sublist.Add(path.Substring(dot + 1), entityType, friendlyName);
        }
    }

    public void SortEntries()
    {
        items.Sort((a, b) => {
            var strA = (a as HierarchyTypeList<T>)?.Name ?? itemsDict.First(kv => kv.Value == a).Key;
            var strB = (b as HierarchyTypeList<T>)?.Name ?? itemsDict.First(kv => kv.Value == b).Key;
            return strA.CompareTo(strB);
        });
        // sort only the root entries - this way the leaf entries stay in the more easily controllable definition order

        // foreach (var sub in items) {
        //     if (sub is HierarchyTypeList<T> hsub) {
        //         hsub.SortEntries();
        //     }
        // }
    }
}