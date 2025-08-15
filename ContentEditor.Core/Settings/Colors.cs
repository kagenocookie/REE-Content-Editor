using System.Numerics;

namespace ContentEditor;

public static class Colors
{
    public static readonly Vector4 Default = new Vector4(1, 1, 1, 1);
    public static readonly Vector4 Disabled = new Vector4(0.75f, 0.75f, 0.75f, 0.75f);
    public static readonly Vector4 Faded = new Vector4(0.8f, 0.8f, 0.8f, 0.8f);
    public static readonly Vector4 Error = new Vector4(1, 0.4f, 0.4f, 1);
    public static readonly Vector4 Danger = new Vector4(1, 0, 0.6f, 1);
    public static readonly Vector4 Warning = new Vector4(1, 0.75f, 0.5f, 1);
    public static readonly Vector4 Info = new Vector4(1, 1, 0.625f, 0.625f);
    public static readonly Vector4 Note = new Vector4(1, 1, 0.53f, 0.53f);
    public static readonly Vector4 Success = new Vector4(1, 1, 0.625f, 0.625f);

    public static readonly Vector4 GameObject = new Vector4(1, 0.96f, 0.92f, 1);
    public static readonly Vector4 Folder = new Vector4(0.9f, 1f, 0.9f, 1);
}