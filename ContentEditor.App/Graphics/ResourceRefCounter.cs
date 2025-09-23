using System.Diagnostics.CodeAnalysis;

namespace ContentEditor.App;

public sealed class ResourceRefCounter<TKey, TResource> : IDisposable
    where TResource : class
    where TKey : IEquatable<TKey>
{
    private readonly Dictionary<TKey, RefCountedResource> keyedResources = new();
    private readonly Dictionary<TResource, RefCountedResource> instances = new();
    private readonly Dictionary<TKey, TKey> resourceRemaps = new();
    private int _nextInstanceID = 1;
    public int NextInstanceID => _nextInstanceID;

    public RefCountedResource Add(TKey resourceKey, TResource resource)
    {
        if (resourceRemaps.TryGetValue(resourceKey, out var remappedKey)) resourceKey = remappedKey;
        if (keyedResources.TryGetValue(resourceKey, out var refs)) {
            refs.References++;
            return refs;
        }

        var id = _nextInstanceID++;
        instances[resource] = refs = new RefCountedResource(resourceKey, resource, 1, id);
        return keyedResources[resourceKey] = refs;
    }

    public RefCountedResource AddUnnamed(TResource resource)
    {
        if (instances.TryGetValue(resource, out var refs)) {
            refs.References++;
        } else {
            var id = _nextInstanceID++;
            instances[resource] = refs = new RefCountedResource(default!, resource, 1, id);
        }
        return refs;
    }

    public bool TryAddReference(TKey resourceKey, [MaybeNullWhen(false)] out RefCountedResource resource)
    {
        if (resourceRemaps.TryGetValue(resourceKey, out var remappedKey)) resourceKey = remappedKey;
        if (keyedResources.TryGetValue(resourceKey, out resource)) {
            resource.References++;
            return true;
        }
        return false;
    }

    public void AddKeyRemap(TKey remapKey, TKey targetKey)
    {
        resourceRemaps[remapKey] = targetKey;
    }

    public void Dereference(RefCountedResource resource)
    {
        resource.References--;
        if (resource.References <= 0) {
            if (false == resource.Key?.Equals(default)) {
                keyedResources.Remove(resource.Key);
            }
            instances.Remove(resource.Resource);
            (resource.Resource as IDisposable)?.Dispose();
        }
    }

    public void Dereference(TResource resource)
    {
        if (instances.TryGetValue(resource, out var refs)) {
            Dereference(refs);
        }
    }

    public void Dispose()
    {
        foreach (var handle in instances.Values) {
            (handle.Resource as IDisposable)?.Dispose();
        }
        instances.Clear();
        keyedResources.Clear();
    }

    public class RefCountedResource(TKey name, TResource resource, int references, int id)
    {
        public TKey Key { get; } = name;
        public int InstanceID { get; } = id;
        public TResource Resource { get; } = resource;
        public int References { get; set; } = references;

        public override string ToString() => $"[{InstanceID} ({References})] {Key} => {Resource}";
    }
}
