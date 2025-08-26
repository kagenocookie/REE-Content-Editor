using System.Diagnostics.CodeAnalysis;

namespace ContentEditor.App;

public sealed class ResourceRefCounter<TResource> : IDisposable where TResource : IDisposable
{
    private readonly Dictionary<string, RefCountedResource<TResource>> resources = new();

    public RefCountedResource<TResource> Add(string path, TResource resource)
    {
        if (resources.TryGetValue(path, out var handle)) {
            handle.References++;
            return handle;
        }

        return resources[path] = handle = new RefCountedResource<TResource>(path, resource, 1);
    }

    public RefCountedResource<TResource> AddUnnamed(TResource resource)
    {
        // add a separate hashset for these instead?
        foreach (var (pp, res) in resources) {
            if (ReferenceEquals(res.Resource, resource)) {
                res.References++;
                return res;
            }
        }

        var path = RandomString(20);
        return resources[path] = new RefCountedResource<TResource>(path, resource, 1);
    }

    public bool TryAddReference(string filePath, [MaybeNullWhen(false)] out RefCountedResource<TResource> resource)
    {
        if (resources.TryGetValue(filePath, out resource)) {
            resource.References++;
            return true;
        }
        return false;
    }

    public void Dereference(RefCountedResource<TResource> resource)
    {
        resource.References--;
        if (resource.References <= 0) {
            resources.Remove(resource.Name);
            resource.Resource.Dispose();
        }
    }

    public void Dereference(TResource resource)
    {
        foreach (var res in resources.Values) {
            if (ReferenceEquals(res.Resource, resource)) {
                Dereference(res);
                return;
            }
        }
    }

    private static Random random = new Random();
    private static string RandomString(int length)
    {
        var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var stringChars = new char[length];

        for (int i = 0; i < stringChars.Length; i++) {
            stringChars[i] = chars[random.Next(chars.Length)];
        }
        return new String(stringChars);
    }

    public void Dispose()
    {
        foreach (var handle in resources.Values) {
            handle.Resource.Dispose();
        }
        resources.Clear();
    }
}

public class RefCountedResource<TResource>(string name, TResource resource, int references)
{
    public string Name { get; } = name;
    public TResource Resource { get; } = resource;
    public int References { get; set; } = references;
}

