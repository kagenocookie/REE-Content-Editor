namespace ContentPatcher;

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
    public int Priority { get; set; } = 5;
    /// <summary>
    /// If the handler does not have any internal state, the same instance can be reused for any object using it.
    /// </summary>
    public bool Stateless { get; set; }

    public ObjectImguiHandlerAttribute(Type handledFieldType)
    {
        HandledFieldType = handledFieldType;
    }
}
