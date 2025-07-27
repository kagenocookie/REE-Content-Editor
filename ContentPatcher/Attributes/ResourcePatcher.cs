namespace ContentPatcher;

[System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
sealed class ResourcePatcherAttribute : System.Attribute
{
    public string PatcherType { get; }
    public string DeserializeMethod { get; }

    public ResourcePatcherAttribute(string patcherType, string deserializeMethod)
    {
        this.PatcherType = patcherType;
        DeserializeMethod = deserializeMethod;
    }
}