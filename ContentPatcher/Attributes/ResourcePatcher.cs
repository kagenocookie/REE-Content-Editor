namespace ContentPatcher;

[System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
sealed class ResourcePatcherAttribute : System.Attribute
{
    public string PatcherType { get; }

    public ResourcePatcherAttribute(string patcherType)
    {
        this.PatcherType = patcherType;
    }
}