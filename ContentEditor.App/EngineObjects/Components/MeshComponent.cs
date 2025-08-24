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

    private Scene RootScene => objectOwnerScene ??= GameObject.Scene!.RootScene;

    private int objectHandle;
    public override AABB LocalBounds => objectHandle == 0 ? default : objectOwnerScene?.RenderContext.GetBounds(objectHandle) ?? default;

    public bool HasMesh => objectHandle != 0;

    internal override void OnActivate(Scene rootScene)
    {
        base.OnActivate(rootScene);

        UnloadMesh();
        objectOwnerScene = rootScene;
        var meshPath = base.Data.Values[meshFieldIndex] as string;
        var matPath = base.Data.Values[meshFieldIndex] as string;
        if (!string.IsNullOrEmpty(meshPath)) {
            SetMesh(meshPath, matPath);
        }
    }

    public void SetMesh(string meshFilepath, string? materialFilepath)
    {
        var newMesh = RootScene.LoadResource<AssimpMeshResource>(meshFilepath);
        // material = rootScene.LoadResource<MaterialResource>(matPath)!; // TODO load mdf2

        UpdateMesh(newMesh, RootScene);
    }

    public void SetMesh(FileHandle meshFile)
    {
        var newMesh = meshFile.GetResource<AssimpMeshResource>();
        RootScene.AddResourceReference(meshFile);
        Data.Values[meshFieldIndex] = meshFile.InternalPath ?? string.Empty;
        UpdateMesh(newMesh, RootScene);
    }

    internal override void OnDeactivate(Scene rootScene)
    {
        base.OnDeactivate(rootScene);
        UnloadMesh();
    }

    private void UpdateMesh(AssimpMeshResource? newMesh, Scene scene)
    {
        UnloadMesh();
        var rootScene = scene.RootScene;

        if (mesh != null && mesh != newMesh) {
            rootScene.UnloadResource(mesh);
            mesh = null;
        }
        if (newMesh == null) {
            return;
        }

        mesh = newMesh;
        objectOwnerScene = scene;
        objectHandle = scene.RenderContext.CreateObject(newMesh.Scene);
    }

    private void UnloadMesh()
    {
        if (mesh == null || objectOwnerScene == null) return;

        objectOwnerScene.RenderContext.DestroyObject(objectHandle);
        if (mesh != null) {
            objectOwnerScene.UnloadResource(mesh);
            mesh = null;
        }
        if (material != null) {
            objectOwnerScene.UnloadResource(material);
            material = null!;
        }
        objectHandle = 0;
    }

    internal override unsafe void Render(RenderContext context)
    {
        if (objectHandle != 0) {
            ref readonly var transform = ref GameObject.Transform.WorldTransform;
            context.RenderSimple(objectHandle, transform);
        }
    }
}
