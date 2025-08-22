namespace ContentPatcher;

[System.AttributeUsage(AttributeTargets.Method|AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
sealed class RszContextActionAttribute : Attribute
{
    public string Classname { get; }
    public string[] Games { get; }

    public RszContextActionAttribute(string classname, params string[] games)
    {
        Classname = classname;
        Games = games;
    }
}
