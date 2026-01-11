namespace ContentPatcher;

/// <summary>
/// Annotates a named field handler that can be specified to be used for a field via the yaml configs.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
sealed class CustomUIHandlerTypeAttribute : System.Attribute
{
    public string Name;
    public string[]? SupportedGames;

    public CustomUIHandlerTypeAttribute(string name, params string[]? supportedGames)
    {
        Name = name;
        SupportedGames = supportedGames;
    }
}
