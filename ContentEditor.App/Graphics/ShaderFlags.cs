namespace ContentEditor.App.Graphics;

// note: the current render queue supports up to 8 shader flag bits
// in case we ever need more, we can move the irrelevant ones after 8.
// Streaming tex does nothing shader wise, instancing has a separate queue.
[Flags]
public enum ShaderFlags
{
    None = 0,
    EnableSkinning = 1,
    EnableStreamingTex = 2,
    EnableInstancing = 4,
}
