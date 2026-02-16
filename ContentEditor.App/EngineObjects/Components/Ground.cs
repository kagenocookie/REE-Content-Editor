using System.Numerics;
using ContentEditor.App.DD2;
using ContentEditor.App.FileLoaders;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.via;

namespace ContentEditor.App;

[RszComponentClass("via.landscape.Ground", nameof(GameName.dd2))]
public class Ground(GameObject gameObject, RszInstance data) : RenderableComponent(gameObject, data), IFixedClassnameComponent, IUpdateable
{
    static string IFixedClassnameComponent.Classname => "via.landscape.Ground";

    private readonly Dictionary<int, MeshHandle?> meshes = new();
    private readonly List<MaterialGroup> materials = new();
    private GrndFile? groundResource;
    private FileHandle? groundMaterialResource;

    public override AABB LocalBounds => groundResource == null ? AABB.Invalid : new AABB(groundResource.Header.Min, groundResource.Header.Max);

    public bool HasMesh => meshes.Count != 0;

    internal override void OnActivate()
    {
        base.OnActivate();

        GameObject.Scene!.Updateable.Add(this);
        if (!AppConfig.Instance.RenderMeshes.Get()) return;

        if (Scene!.Workspace.ResourceManager.TryResolveResourceFile<GrndFile>(RszFieldCache.Ground.GroundResource.Get(Data), out groundResource)) {
            Scene!.Workspace.ResourceManager.TryResolveGameFile(RszFieldCache.Ground.GroundMaterialResource.Get(Data), out groundMaterialResource);

            ReloadMeshes();
        }
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        GameObject.Scene!.Updateable.Remove(this);
        UnloadMeshes();
    }

    public void ReloadMeshes()
    {
        UnloadMeshes();
        if (groundResource == null || WorldEnvironmentController.Instance == null) return;

        TryLoadCurrentTexture(WorldEnvironmentController.Instance.GroundFields.CurrentID);
    }

    private void UnloadMeshes()
    {
        foreach (var handle in meshes) {
        // for (int i = 0; i < meshes.Count; ++i) {
            // var handle = meshes[i];
            // if (handle == null) return;
            if (handle.Value == null) continue;

            Scene!.RenderContext.UnloadMesh(handle.Value);
            Scene.RenderContext.UnloadMaterialGroup(handle.Value.Material);
        }
        meshes.Clear();
        materials.Clear();
    }

    internal override unsafe void Render(RenderContext context)
    {
        var render = AppConfig.Instance.RenderMeshes.Get();
        if (!render || WorldEnvironmentController.Instance == null) {
            UnloadMeshes();
            return;
        }
        if (meshes.Count == 0) {
            ReloadMeshes();
        }
        foreach (var (position, mesh) in meshes) {
            if (mesh == null) continue;

            // var pos = WorldEnvironmentController.Instance.GroundFields.GetPosition(position);
            // context.RenderSimple(mesh, Matrix4X4.CreateTranslation(pos.ToGeneric()));
            context.RenderSimple(mesh, Matrix4x4.Identity);
        }
    }

    public void Update(float deltaTime)
    {
        if (!AppConfig.Instance.RenderMeshes.Get() || WorldEnvironmentController.Instance == null || groundResource == null) return;

        foreach (var id in WorldEnvironmentController.Instance.GroundFields.ActiveIDs) {
            TryLoadCurrentTexture(id);
        }
    }

    private void TryLoadCurrentTexture(int curId)
    {
        if (groundResource == null) return;

        if (!meshes.TryGetValue(curId, out var tmesh)) {
            var tex = groundResource.GroundTextures[curId];
            if (Scene!.Workspace.ResourceManager.TryResolveGameFile(tex, out var handle)) {
                var res = handle.GetResource<GroundTerrainResourceFile>();

                var pos = WorldEnvironmentController.Instance!.GroundFields.GetPosition(curId);
                var size = WorldEnvironmentController.Instance!.GroundFields.CellSize;

                res.Min = new System.Numerics.Vector3(pos.X, groundResource.Header.minY, pos.Z);
                res.Max = new System.Numerics.Vector3(pos.X + size.X, groundResource.Header.minY + groundResource.Header.height, pos.Z + size.Y);
                tmesh = Scene.RenderContext.LoadMesh(handle);
                var mm = Scene.RenderContext.LoadMaterialGroup(groundMaterialResource ?? handle);
                tmesh?.SetMaterials(mm);
                meshes.Add(curId, tmesh);
                materials.Add(mm);
            } else {
                Logger.Error("could not resolve ground terrain texture " + tex);
            }
        }
    }
}
