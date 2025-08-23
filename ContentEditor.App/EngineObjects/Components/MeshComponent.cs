using System.Buffers;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

[RszComponentClass("via.render.Mesh")]
public class MeshComponent(GameObject gameObject, RszInstance data) : RenderableComponent(gameObject, data), IFixedClassnameComponent
{
    private readonly int meshFieldIndex = data.IndexedFields.First(fi => fi.field.type is RszFieldType.String or RszFieldType.Resource).index;
    private readonly int materialFieldIndex = data.IndexedFields.Where(fi => fi.field.type is RszFieldType.String or RszFieldType.Resource).Skip(1).First().index;

    static string IFixedClassnameComponent.Classname => "via.render.Mesh";

    private AssimpMeshResource? mesh;
    private MaterialResource material = null!;
    private Scene? objectOwnerScene;

    private int objectHandle;
    public AABB MeshLocalBounds => objectOwnerScene?.RenderContext.GetBounds(objectHandle) ?? default;

    public bool HasMesh => objectHandle != 0;

    internal override void OnEnterScene(Scene rootScene)
    {
        base.OnEnterScene(rootScene);
        var meshPath = base.Data.Values[meshFieldIndex] as string;
        if (!string.IsNullOrEmpty(meshPath)) {
            mesh = rootScene.LoadResource<AssimpMeshResource>(meshPath);
        }
        var matPath = base.Data.Values[meshFieldIndex] as string;
        if (!string.IsNullOrEmpty(matPath)) {
            // material = rootScene.LoadResource<MaterialResource>(matPath)!; // TODO add a default material
        }
        if (mesh != null) {
            UpdateMesh(mesh, objectOwnerScene = rootScene);
        } else {
            objectOwnerScene = null;
        }
    }

    internal override void OnExitScene(Scene rootScene)
    {
        base.OnExitScene(rootScene);
        objectOwnerScene?.RenderContext.DestroyObject(objectHandle);
        if (mesh != null) {
            rootScene.UnloadResource(mesh);
            mesh = null;
        }
        if (material != null) {
            rootScene.UnloadResource(material);
            material = null!;
        }
    }

    public void UpdateMesh(AssimpMeshResource? mesh, Scene scene)
    {
        var rootScene = GameObject.Scene?.RootScene;
        if (rootScene == null) return;

        if (this.mesh != null) {
            rootScene.UnloadResource(this.mesh);
        }
        this.mesh = mesh;
        if (mesh != null) {
            rootScene.AddResourceReference(mesh);
            objectOwnerScene = scene;
            objectHandle = scene.RenderContext.CreateObject(mesh.Scene);
        } else {
            objectOwnerScene = null;
        }
    }

    internal override unsafe void Render(RenderContext context)
    {
        if (objectHandle != 0) {
            ref readonly var transform = ref GameObject.Transform.WorldTransform;
            context.RenderSimple(objectHandle, transform);
        }
    }
}
