
namespace ContentPatcher;

public class EntityTypeList(string name)
{
    private readonly List<object> items = new();
    private readonly Dictionary<string, object> itemsDict = new();

    private string[]? _names;
    public string[] Names {
        get {
            if (_names == null) _names = items.Select(it => (it as EntityTypeList)?.Name ?? itemsDict.First(kv => kv.Value == it).Key).ToArray();
            return _names;
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

    public (object, string name) Get(int index) => (items[index], Names[index]);
    public object Get(string name) => itemsDict[name];

    public string Name { get; } = name;

    public void Add(EntityTypeList sublist)
    {
        if (itemsDict.ContainsKey(sublist.Name)) {
            throw new Exception("Duplicate entity type registration attempt: " + sublist.Name);
        }

        items.Add(sublist);
    }

    public string Add(string path, EntityConfig entityType)
    {
        var dot = path.IndexOf('.');
        if (dot == -1) {
            if (itemsDict.ContainsKey(path)) {
                throw new Exception("Duplicate entity type registration attempt: " + path);
            }

            items.Add(entityType);
            itemsDict[path] = entityType;
            return path;
        } else {
            var subname = path.Substring(0, dot);
            if (!itemsDict.TryGetValue(subname, out var subitem)) {
                itemsDict[subname] = subitem = new EntityTypeList(subname);
                items.Add(subitem);
            } else if (subitem is not EntityTypeList) {
                throw new Exception("Invalid entity type path - conflict found: " + path);
            }

            var sublist = subitem as EntityTypeList;
            if (sublist == null) {
                sublist = new EntityTypeList(subname);
            }
            return sublist.Add(path.Substring(dot + 1), entityType);
        }
    }
}