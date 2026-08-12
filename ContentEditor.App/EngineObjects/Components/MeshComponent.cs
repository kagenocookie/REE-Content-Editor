using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentPatcher;
using ReeLib;
using ReeLib.Mesh;
using ReeLib.via;

namespace ContentEditor.App;

public enum MeshDisplayMode
{
    Default,
    Solid,
    Wireframe,
}

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
    public MeshDisplayMode PreviewDisplayMode { get; set; }
    public IReadOnlySet<int>? HiddenPreviewSubmeshIndices { get; set; }
    public IReadOnlySet<int>? HighlightedSubmeshIndices { get; set; }
    public IReadOnlySet<int>? EditSubmeshIndices { get; set; }
    public bool EditWireframeOverlay { get; set; }
    public bool ShowEditVertices { get; set; }
    public float EditVertexPointSize { get; set; } = 6.0f;

    private readonly Dictionary<(BuiltInMaterials material, ShaderFlags flags), Material> previewMaterials = new();

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
            // TODO handle material count mismatch more accurately?
            mesh.SetMaterials(material);
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

        mesh?.SetMaterials(material);
        RszFieldCache.Mesh.Resource.Set(Data, meshFile.ResourcePath ?? meshFile.Filepath ?? string.Empty);
        RszFieldCache.Mesh.Material.Set(Data, materialFile?.ResourcePath ?? materialFile?.Filepath ?? string.Empty);
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
        // TODO - ideally don't have this occ check here and handle it differently somehow
        var render = mesh?.Meshes.FirstOrDefault()?.MaterialNameHash == 2180083513 ?  AppConfig.Instance.RenderOcclusion.Get() : AppConfig.Instance.RenderMeshes.Get();
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
            if (context is OpenGLRenderContext ogl && (PreviewDisplayMode != MeshDisplayMode.Default || HiddenPreviewSubmeshIndices?.Count > 0)) {
                mesh.PrepareSubmeshParts();
                var previewMaterial = PreviewDisplayMode == MeshDisplayMode.Default
                    ? null
                    : GetPreviewMaterial(context, mesh, PreviewDisplayMode == MeshDisplayMode.Solid ? BuiltInMaterials.Solid : BuiltInMaterials.Wireframe);
                foreach (var (submeshIndex, materialIndex) in mesh.EnabledSubmeshIndices) {
                    if (HiddenPreviewSubmeshIndices?.Contains(submeshIndex) == true) continue;
                    ogl.Batch.Simple.Add(new NormalRenderBatchItem(previewMaterial ?? mesh.GetMaterial(materialIndex), mesh.GetMesh(submeshIndex), transform, mesh));
                }
            } else {
                context.RenderSimple(mesh, transform);
            }

            if (context is OpenGLRenderContext overlayContext && HighlightedSubmeshIndices?.Count > 0) {
                var highlightMaterial = GetPreviewMaterial(context, mesh, BuiltInMaterials.MonoColor);
                foreach (var selectedIndex in HighlightedSubmeshIndices) {
                    if ((uint)selectedIndex >= (uint)mesh.Meshes.Count()) continue;
                    if (HiddenPreviewSubmeshIndices?.Contains(selectedIndex) == true) continue;
                    var selectedMesh = mesh.GetMesh(selectedIndex);
                    if (mesh.GetMeshPartEnabled(selectedMesh.MeshGroup)) {
                        overlayContext.Batch.Gizmo.Add(new GizmoRenderBatchItem(highlightMaterial, selectedMesh, transform, null, mesh, true));
                    }
                }
            }

            if (context is OpenGLRenderContext wireframeContext && (Scene?.WireframeOverlay == true || EditWireframeOverlay) && PreviewDisplayMode != MeshDisplayMode.Wireframe) {
                mesh.PrepareSubmeshParts();
                var wireframeMaterial = GetPreviewMaterial(context, mesh, BuiltInMaterials.Wireframe);
                foreach (var (submeshIndex, _) in mesh.EnabledSubmeshIndices) {
                    if (HiddenPreviewSubmeshIndices?.Contains(submeshIndex) == true) continue;
                    if (Scene?.WireframeOverlay != true && EditSubmeshIndices?.Contains(submeshIndex) != true) continue;
                    wireframeContext.Batch.Gizmo.Add(new GizmoRenderBatchItem(wireframeMaterial, mesh.GetMesh(submeshIndex), transform, null, mesh, true));
                }
            }

            if (context is OpenGLRenderContext vertexContext && ShowEditVertices) {
                mesh.PrepareSubmeshParts();
                var vertexMaterial = GetPreviewMaterial(context, mesh, BuiltInMaterials.EditVertices);
                foreach (var (submeshIndex, _) in mesh.EnabledSubmeshIndices) {
                    if (HiddenPreviewSubmeshIndices?.Contains(submeshIndex) == true) continue;
                    if (EditSubmeshIndices?.Contains(submeshIndex) != true) continue;
                    vertexContext.Batch.Gizmo.Add(new GizmoRenderBatchItem(vertexMaterial, mesh.GetMesh(submeshIndex), transform, null, mesh, true, Silk.NET.OpenGL.PrimitiveType.Points, EditVertexPointSize));
                }
            }
        }
    }

    private Material GetPreviewMaterial(RenderContext context, MeshHandle meshHandle, BuiltInMaterials materialType)
    {
        var flags = ShaderFlags.None;
        if (meshHandle.HasArmature) {
            flags |= ShaderFlags.EnableSkinning;
            if (meshHandle.Meshes.FirstOrDefault()?.layout.Is6Weight == true) flags |= ShaderFlags.Use6Weights;
        }
        var key = (materialType, flags);
        if (previewMaterials.TryGetValue(key, out var material)) return material;

        material = context.GetBuiltInMaterial(materialType, flags);
        switch (materialType) {
            case BuiltInMaterials.Solid:
                material.Name = "mesh_editor_solid";
                material.SetParameter("_MainColor", new Color(190, 190, 190, 255));
                break;
            case BuiltInMaterials.Wireframe:
                material.Name = "mesh_editor_wireframe";
                material.SetParameter("_InnerColor", new Color(200, 200, 200, 255));
                material.SetParameter("_OuterColor", new Color(0, 0, 0, 0));
                break;
            case BuiltInMaterials.MonoColor:
                material.Name = "mesh_editor_selection";
                material.SetParameter("_MainColor", new Color(255, 128, 0, 150));
                material.BlendMode = new MaterialBlendMode(true, Silk.NET.OpenGL.BlendingFactor.SrcAlpha, Silk.NET.OpenGL.BlendingFactor.OneMinusSrcAlpha);
                break;
            case BuiltInMaterials.EditVertices:
                material.Name = "mesh_editor_vertices";
                material.SetParameter("_MainColor", new Color(255, 128, 0, 255));
                break;
        }
        previewMaterials[key] = material;
        return material;
    }

    public void CollectPickables(PickableData data)
    {
        if (mesh == null || !AppConfig.Instance.RenderMeshes.Get()) return;

        data.TryAdd(this, 0, mesh, Transform.WorldTransform, WorldSpaceBounds);
    }
}
