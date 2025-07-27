namespace ContentPatcher;

[System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
sealed class ResourceFieldAttribute : System.Attribute
{
    public string PatcherType { get; }
    public string[]? SupportedGames;

    public ResourceFieldAttribute(string patcherType, params string[]? supportedGames)
    {
        PatcherType = patcherType;
        SupportedGames = supportedGames;
    }
}
