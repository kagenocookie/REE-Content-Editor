namespace ContentEditor.App.Graphics;

[Flags]
public enum ShaderFlags
{
    // first 8 flag bits affect shader IDs and are reserved for flags relevant for shader code
    None = 0,
    EnableSkinning = 1,
    Use6Weights = 2,

    EnableStreamingTex = (1 << 8), // flag doesn't matter for anything shader side
    EnableInstancing = (1 << 9), // instanced has a separate queue, does not affect ordering within the same queue
}
