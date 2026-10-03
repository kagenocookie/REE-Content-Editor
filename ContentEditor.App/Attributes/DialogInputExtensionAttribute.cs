using ContentEditor.Core;

namespace ContentPatcher;

[System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
sealed class DialogInputExtensionAttribute : System.Attribute
{
    public string Name;

    public DialogInputExtensionAttribute(string name)
    {
        Name = name;
    }
}
