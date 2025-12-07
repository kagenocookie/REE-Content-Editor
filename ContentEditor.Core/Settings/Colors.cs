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

    public static Vector4 Colliders => new Vector4(0, 1, 0, 0.8f);
    public static Vector4 RequestSetColliders => new Vector4(1, 0.4f, 0.1f, 0.8f);
    public static Vector4 Lights => new Vector4(1, 1, 0, 0.8f);
    /// <summary>Cardboard</summary>
    public static Vector4 PFB => new Vector4(0.80f, 0.62f, 0.38f, 1);
    /// <summary>Purple</summary>
    public static Vector4 SCN => new Vector4(0.53f, 0.29f, 0.91f, 1);
    /// <summary>Cerulean</summary>
    public static Vector4 MESH => new Vector4(0, 0.54f, 0.94f, 1);
    /// <summary>Cyan</summary>
    public static Vector4 MCOL => new Vector4(0, 0.85f, 1, 1);
    /// <summary>Rust</summary>
    public static Vector4 RCOL => new Vector4(0.85f, 0.27f, 0.25f, 1);
    /// <summary>Gold</summary>
    public static Vector4 MDF => new Vector4(0.93f, 0.55f, 0.07f, 1);
}

public sealed class AppColors
{
    public Vector4 Default = new Vector4(1, 1, 1, 1);
    public Vector4 Disabled = new Vector4(0.75f, 0.75f, 0.75f, 0.75f);
    public Vector4 Faded = new Vector4(0.8f, 0.8f, 0.8f, 0.8f);
    public Vector4 Error = new Vector4(1, 0.4f, 0.4f, 1);
    public Vector4 Danger = new Vector4(1, 0, 0.6f, 1);
    public Vector4 Warning = new Vector4(1, 0.75f, 0.5f, 1);
    public Vector4 Info = new Vector4(0.625f, 0.625f, 1, 1);
    public Vector4 Note = new Vector4(0.53f, 0.93f, 1, 1);
    public Vector4 Success = new Vector4(0.36f, 1, 0.32f, 1);

    public Vector4 GameObject = new Vector4(1, 0.96f, 0.92f, 1);
    public Vector4 Folder = new Vector4(0.9f, 1f, 0.9f, 1);

    public static AppColors GetDarkThemeColors() => new AppColors();

    public static AppColors GetLightThemeColors() => new AppColors() {
        Default = new Vector4(0.02f, 0, 0, 1),
        Disabled = new Vector4(0.5f, 0.5f, 0.5f, 0.9f),
        Faded = new Vector4(0.18f, 0.27f, 0.37f, 0.8f),
        Error = new Vector4(1, 0.13f, 0.13f, 1),
        Danger = new Vector4(1, 0, 0.6f, 1),
        Warning = new Vector4(0.93f, 0.6f, 0.24f, 1),
        Info = new Vector4(0.34f, 0.5f, 0.88f, 1),
        Note = new Vector4(0.22f, 0.08f, 0.66f, 1),
        Success = new Vector4(0.04f, 0.73f, 0.13f, 1),
        GameObject = new Vector4(0.36f, 0, 0, 1),
        Folder = new Vector4(0.03f, 0.28f, 0.065f, 1),
    };

    public static readonly FieldInfo[] ColorFields = typeof(AppColors).GetFields().Where(f => !f.IsStatic).ToArray();
}
