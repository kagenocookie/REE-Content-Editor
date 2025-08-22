namespace ContentPatcher;

/// <summary>
/// Marks a class to be used as the editor for a custom field. A new handler instance is created for each individual entity + field.
/// </summary>
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
