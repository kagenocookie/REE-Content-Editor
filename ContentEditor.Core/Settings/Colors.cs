using System.Numerics;
using System.Reflection;

namespace ContentEditor;

public static class Colors
{
    public static AppColors Current { get; set; } = new AppColors();

    public static Vector4 Default => Current.Default;
    public static Vector4 Disabled => Current.Disabled;
    public static Vector4 Faded => Current.Faded;
    public static Vector4 Error => Current.Error;
    public static Vector4 Danger => Current.Danger;
    public static Vector4 Warning => Current.Warning;
    public static Vector4 Info => Current.Info;
    public static Vector4 Note => Current.Note;
    public static Vector4 Success => Current.Success;

    public static Vector4 GameObject => Current.GameObject;
    public static Vector4 Folder => Current.Folder;
}

public sealed class AppColors
{
    public Vector4 Default = new Vector4(1, 1, 1, 1);
    public Vector4 Disabled = new Vector4(0.75f, 0.75f, 0.75f, 0.75f);
    public Vector4 Faded = new Vector4(0.8f, 0.8f, 0.8f, 0.8f);
    public Vector4 Error = new Vector4(1, 0.4f, 0.4f, 1);
    public Vector4 Danger = new Vector4(1, 0, 0.6f, 1);
    public Vector4 Warning = new Vector4(1, 0.75f, 0.5f, 1);
    public Vector4 Info = new Vector4(1, 1, 0.625f, 0.625f);
    public Vector4 Note = new Vector4(1, 1, 0.53f, 0.53f);
    public Vector4 Success = new Vector4(1, 1, 0.625f, 0.625f);

    public Vector4 GameObject = new Vector4(1, 0.96f, 0.92f, 1);
    public Vector4 Folder = new Vector4(0.9f, 1f, 0.9f, 1);

    public static readonly FieldInfo[] ColorFields = typeof(AppColors).GetFields().Where(f => !f.IsStatic).ToArray();
}
