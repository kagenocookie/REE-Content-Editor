namespace ContentPatcher;

[System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
sealed class CustomFieldHandlerAttribute : System.Attribute
{
    public Type HandledFieldType { get; }
    public string[]? SupportedGames;

    public CustomFieldHandlerAttribute(Type handledFieldType, params string[]? supportedGames)
    {
        HandledFieldType = handledFieldType;
        SupportedGames = supportedGames;
    }
}
