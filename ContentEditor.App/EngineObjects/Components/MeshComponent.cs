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
    static string IFixedClassnameComponent.Classname => "via.render.Mesh";

    private AssimpMeshResource? mesh;
    private MaterialResource material = null!;

    private int objectHandle;
    public override AABB LocalBounds => objectHandle == 0 ? default : Scene?.RenderContext.GetBounds(objectHandle) ?? default;

    public bool HasMesh => objectHandle != 0;

    internal override void OnActivate()
    {
        base.OnActivate();

        UnloadMesh();
        var meshPath = RszFieldCache.Mesh.Resource.Get(Data);
        var matPath = RszFieldCache.Mesh.Material.Get(Data);
        if (!string.IsNullOrEmpty(meshPath)) {
            SetMesh(meshPath, matPath);
        }
    }

    public void SetMesh(string meshFilepath, string? materialFilepath)
    {
        var newMat = string.IsNullOrEmpty(materialFilepath) ? null : Scene!.LoadResource<AssimpMaterialResource>(materialFilepath);
        var newMesh = Scene!.LoadResource<AssimpMeshResource>(meshFilepath);

        UpdateMesh(newMesh, newMat);
    }

    public void SetMesh(FileHandle meshFile)
    {
        var newMesh = meshFile.GetResource<AssimpMeshResource>();
        Scene!.AddResourceReference(meshFile);
        RszFieldCache.Mesh.Resource.Set(Data, meshFile.InternalPath ?? string.Empty);
        UpdateMesh(newMesh, null);
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        UnloadMesh();
    }

    private void UpdateMesh(AssimpMeshResource? newMesh, AssimpMaterialResource? newMat)
    {
        UnloadMesh();

        if (mesh != null && mesh != newMesh) {
            Scene!.UnloadResource(mesh);
            mesh = null;
        }
        if (newMesh == null) {
            return;
        }

        mesh = newMesh;

        var matgroup = newMat == null ? 0 : Scene!.RenderContext.LoadMaterialGroup(newMat.Scene);
        objectHandle = Scene!.RenderContext.CreateObject(newMesh.Scene, matgroup);
    }

    private void UnloadMesh()
    {
        if (mesh == null || Scene == null) return;

        Scene.RenderContext.DestroyObject(objectHandle);
        if (mesh != null) {
            Scene.UnloadResource(mesh);
            mesh = null;
        }
        if (material != null) {
            Scene.UnloadResource(material);
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
