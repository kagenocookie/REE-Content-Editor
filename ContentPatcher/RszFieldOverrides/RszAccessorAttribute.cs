namespace ContentPatcher;

/// <summary>
/// Marks a RSZ field accessor with the target classname.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Field|AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
sealed class RszAccessorAttribute : System.Attribute
{
    public string Classname { get; }
    public string[] Games { get; }
    public bool GamesExclude { get; init; } = false;

    public RszAccessorAttribute(string classname, params string[] games)
    {
        Classname = classname;
        Games = games;
    }
}
