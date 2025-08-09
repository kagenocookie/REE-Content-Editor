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

[System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
sealed class ObjectImguiHandlerAttribute : System.Attribute
{
    /// <summary>
    /// The target type which is handled by this handler.
    /// </summary>
    public Type HandledFieldType { get; }
    /// <summary>
    /// This handler type applies to subclasses of the target type as well.
    /// </summary>
    public bool Inherited { get; set; }
    /// <summary>
    /// A lower priority number has precedence over higher numbers in case of multiple potential valid handlers for a type.
    /// </summary>
    public int Priority { get; set; }
    /// <summary>
    /// If the handler does not have any internal state, the same instance can be reused for any object using it.
    /// </summary>
    public bool Stateless { get; set; }

    public ObjectImguiHandlerAttribute(Type handledFieldType)
    {
        HandledFieldType = handledFieldType;
    }
}
