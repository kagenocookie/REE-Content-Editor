namespace ContentPatcher;

[System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
sealed class ResourceFieldAttribute : System.Attribute
{
    public string FieldTypeName { get; }
    public Type? HandlerType { get; }
    public string[]? SupportedGames;

    public ResourceFieldAttribute(string patcherType, Type? handlerType = null, params string[]? supportedGames)
    {
        FieldTypeName = patcherType;
        HandlerType = handlerType;
        SupportedGames = supportedGames;
    }
}
