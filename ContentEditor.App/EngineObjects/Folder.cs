namespace ContentEditor.App;

public class Folder : NodeObject<Folder>, IDisposable
{
    public readonly List<GameObject> GameObjects = new();

    public Folder(string name)
    {
        Name = name;
    }

    public IEnumerable<GameObject> GetAllGameObjects()
    {
        foreach (var folder in Children) {
            foreach (var sub in folder.GetAllGameObjects()) {
                yield return sub;
            }
        }
        foreach (var child in GameObjects) {
            yield return child;
            foreach (var sub in child.GetAllChildren()) {
                yield return sub;
            }
        }
    }

    public GameObject? Find(ReadOnlySpan<char> path)
    {
        var part = path.IndexOf('/');
        if (part == -1) {
            return FindGameObjectByName(path);
        }
        var child = FindGameObjectByName(path.Slice(0, part));
        if (child == null) {
            var folder = GetChild(path);
            return folder?.Find(path.Slice(part + 1));
        }

        return child?.Find(path.Slice(part + 1));
    }

    private GameObject? FindGameObjectByName(ReadOnlySpan<char> name)
    {
        foreach (var go in GameObjects) {
            if (name.SequenceEqual(go.Name)) {
                return go;
            }
        }
        return null;
    }

    public void Dispose()
    {
        foreach (var folder in Children) {
            folder.Dispose();
        }

        foreach (var go in GameObjects) {
            go.Dispose();
        }
    }
}
