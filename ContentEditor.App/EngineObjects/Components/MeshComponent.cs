using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentPatcher;
using ReeLib;
using ReeLib.Mesh;
using ReeLib.via;

namespace ContentEditor.App;

[RszComponentClass("via.render.Mesh")]
public class MeshComponent(GameObject gameObject, RszInstance data) : RenderableComponent(gameObject, data),
    IFixedClassnameComponent,
    IConstructorComponent,
    IScenePickableComponent,
    IBoneReferenceHolder
{
    public static new string Classname => "via.render.Mesh";

    private MeshHandle? mesh;
    private MaterialGroup? material;

    public MeshHandle? MeshHandle => mesh;

    public override AABB LocalBounds => mesh?.BoundingBox ?? AABB.Invalid;

    public bool HasMesh => mesh != null;
    public bool UseStreamingTex = false;
    private bool invalidMesh;

    public void ComponentInit()
    {
        RszFieldCache.Mesh.PartsEnable.Set(Data, Enumerable.Range(0, 256).Select(_ => (object)true).ToList());
    }

    public IEnumerable<MeshBone> GetBones() => (MeshHandle as AnimatedMeshHandle)?.Bones?.Bones ?? [];

    public MeshBone? FindBoneByHash(uint hash)
    {
        if (MeshHandle is not AnimatedMeshHandle mesh) return null;
        var bone = mesh.Bones?.GetByHash(hash);

        return bone;
    }

    public bool TryGetBoneTransform(uint hash, out Matrix4x4 matrix)
    {
        if (MeshHandle is not AnimatedMeshHandle mesh)  {
            matrix = Matrix4x4.Identity;
            return false;
        }

        return mesh.TryGetBoneTransform(hash, out matrix);
    }

    internal override void OnActivate()
    {
        base.OnActivate();

        RefreshIfActive();
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        UnloadMesh();
    }

    public void RefreshIfActive()
    {
        if (Scene?.IsActive != true || !AppConfig.Instance.RenderMeshes.Get()) return;

        RefreshMesh();
    }

    private void RefreshMesh()
    {
        UnloadMesh();
        var meshPath = RszFieldCache.Mesh.Resource.Get(Data);
        if (!string.IsNullOrEmpty(meshPath)) {
            SetMesh(meshPath, RszFieldCache.Mesh.Material.Get(Data));
        }
    }

    public void SetMesh(string meshFilepath, string? materialFilepath)
    {
        invalidMesh = false;
        UnloadMesh();
        mesh = Scene!.RenderContext.LoadMesh(meshFilepath);
        var shaderFlags = ShaderFlags.None;
        if (UseStreamingTex) {
            shaderFlags = ShaderFlags.EnableStreamingTex;
        }
        if (mesh?.HasArmature == true) {
            shaderFlags |= ShaderFlags.EnableSkinning;
            if (mesh?.Meshes.FirstOrDefault()?.layout.Is6Weight == true) {
                shaderFlags |= ShaderFlags.Use6Weights;
            }
        }
        material = string.IsNullOrEmpty(materialFilepath)
            ? Scene.RenderContext.LoadMaterialGroup(meshFilepath, shaderFlags)
            : Scene.RenderContext.LoadMaterialGroup(materialFilepath, shaderFlags);
        if (mesh != null && material != null) {
            Scene.RenderContext.SetMeshMaterial(mesh, material);
        }
        RszFieldCache.Mesh.Resource.Set(Data, meshFilepath);
        RszFieldCache.Mesh.Material.Set(Data, materialFilepath ?? string.Empty);

        if (mesh != null) {
            var parts = RszFieldCache.Mesh.PartsEnable.Get(Data);
            if (parts != null) {
                for (int i = 0; i < parts.Count; ++i) {
                    var enabled = (bool)parts[i];
                    mesh.SetMeshPartEnabled(i, enabled);
                }
            }
        }
        mesh?.Update();
        IsStatic = mesh == null || !mesh.HasArmature;
        RecomputeWorldAABB();
    }


    public void SetMesh(FileHandle meshFile, FileHandle? materialFile)
    {
        UnloadMesh();
        mesh = Scene!.RenderContext.LoadMesh(meshFile);
        var shaderFlags = ShaderFlags.None;
        if (UseStreamingTex) {
            shaderFlags = ShaderFlags.EnableStreamingTex;
        }
        if (mesh?.HasArmature == true) {
            shaderFlags |= ShaderFlags.EnableSkinning;
            if (mesh?.Meshes.FirstOrDefault()?.layout.Is6Weight == true) {
                shaderFlags |= ShaderFlags.Use6Weights;
            }
        }
        material = Scene.RenderContext.LoadMaterialGroup(materialFile ?? meshFile, shaderFlags);

        if (mesh != null) {
            Scene.RenderContext.SetMeshMaterial(mesh, material);
        }
        RszFieldCache.Mesh.Resource.Set(Data, meshFile.InternalPath ?? meshFile.Filepath ?? string.Empty);
        RszFieldCache.Mesh.Material.Set(Data, materialFile?.InternalPath ?? materialFile?.Filepath ?? string.Empty);
        mesh?.Update();
        IsStatic = mesh == null || !mesh.HasArmature;
    }


    private void UnloadMesh()
    {
        if (mesh == null || Scene == null) return;

        if (mesh != null) {
            Scene.RenderContext.UnloadMesh(mesh);
            mesh = null;
        }
        if (material != null) {
            Scene.RenderContext.UnloadMaterialGroup(material);
            material = null;
        }
        IsStatic = true;
    }

    internal override unsafe void Render(RenderContext context)
    {
        // TODO - this may be better handled on the level of scene + component grouping instead of inside individual components
        var render = AppConfig.Instance.RenderMeshes.Get();
        if (!render) {
            return;
        }
        if (mesh == null) {
            if (invalidMesh) return;
            RefreshMesh();
            if (mesh == null) {
                invalidMesh = true;
            }
        }
        if (mesh != null) {
            ref readonly var transform = ref GameObject.Transform.WorldTransform;
            context.RenderSimple(mesh, transform);
        }
    }

    public void CollectPickables(PickableData data)
    {
        if (mesh == null || !AppConfig.Instance.RenderMeshes.Get()) return;

        data.TryAdd(this, 0, mesh, Transform.WorldTransform, WorldSpaceBounds);
    }
}
