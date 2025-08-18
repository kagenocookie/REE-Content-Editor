namespace ContentPatcher;

/// <summary>
/// Marks a class to be used as the editor for an RSZ class type. A new handler instance is created for each individual entity + field.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
sealed class RszClassHandlerAttribute : System.Attribute
{
    public string Classname { get; }
    public string[] Games { get; }

    public RszClassHandlerAttribute(string classname, params string[] games)
    {
        Classname = classname;
        Games = games;
    }
}
