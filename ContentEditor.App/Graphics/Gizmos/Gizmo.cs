using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public abstract class Gizmo
{
    protected readonly GL GL;

    // note: no need to dispose any of this. Currently, gizmos will stay alive for the duration of the RenderCtx
    // and the meshes will then get disposed together with everything else
    public List<MeshHandle> Meshes { get; protected set; } = new();
    public List<MaterialGroup> Materials { get; protected set; } = new();

    protected Gizmo(GL gL)
    {
        GL = gL;
    }

    public virtual void Init(OpenGLRenderContext context)
    {

    }

    public virtual void Update(OpenGLRenderContext context, float deltaTime)
    {

    }

    public abstract void Render(OpenGLRenderContext context);
}