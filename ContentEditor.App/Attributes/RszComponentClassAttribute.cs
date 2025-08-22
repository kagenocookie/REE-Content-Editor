namespace ContentPatcher;

[System.AttributeUsage(AttributeTargets.Method|AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
sealed class RszComponentClassAttribute : Attribute
{
    public string Classname { get; }
    public string[] Games { get; }

    public RszComponentClassAttribute(string classname, params string[] games)
    {
        Classname = classname;
        Games = games;
    }
}
