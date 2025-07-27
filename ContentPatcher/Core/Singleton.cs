namespace ContentPatcher;

#pragma warning disable CA1000 // Do not declare static members on generic types
public class Singleton<T>
{
    private static T? _instance;
    public static T Instance => _instance ??= Activator.CreateInstance<T>();
};
