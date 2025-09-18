using ReeLib.via;

namespace ContentEditor.App.Graphics;

public class AnimatedMeshHandle : MeshHandle
{
    internal AnimatedMeshHandle(MeshResourceHandle mesh) : base(mesh)
    {
    }

    public override void Update()
    {
        var delta = Time.Delta;
        // TODO animate
    }

    public override string ToString() => $"[Animated mesh {Handle}]";
}
