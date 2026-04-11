using System.Diagnostics;
using ContentEditor.App.Graphics;
using ReeLib;

namespace ContentEditor.App;

[RszComponentClass("via.motion.Motion")]
public class Motion(GameObject gameObject, RszInstance data) : UpdateComponent(gameObject, data), IFixedClassnameComponent
{
    public static new string Classname => "via.motion.Motion";

    private MeshComponent? meshComponent;

    public Animator? Animator { get; private set; }

    internal override void OnActivate()
    {
        base.OnActivate();
        meshComponent = GetComponent<MeshComponent>();
    }

    public override void Update(float deltaTime)
    {
        UpdateAnimation(deltaTime);
        if (!string.IsNullOrEmpty(Transform.ParentJoint)) {
            Transform.InvalidateTransform();
        }
    }

    public void InitAnimation()
    {
        Debug.Assert(Scene != null);
        Animator = new Animator(Scene.Workspace);
    }

    public void UpdateAnimation(float deltaTime)
    {
        if (meshComponent == null || Animator == null || meshComponent.MeshHandle is not AnimatedMeshHandle animMesh) return;
    }
}