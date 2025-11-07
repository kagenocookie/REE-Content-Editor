using System.Diagnostics;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling.Rcol;
using ContentEditor.App.Windowing;
using ContentPatcher;
using ReeLib;
using ReeLib.Aimp;
using ReeLib.Rcol;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

[RszComponentClass("via.navigation.AIMap")]
public class AIMap(GameObject gameObject, RszInstance data) : AIMapComponentBase(gameObject, data),
    IFixedClassnameComponent
{
    static string IFixedClassnameComponent.Classname => "via.navigation.AIMap";

    public override IEnumerable<string> StoredResources => Data.Get(RszFieldCache.AIMap.Maps).Select(x => RszFieldCache.MapHandle.Resource.Get((RszInstance)x));

}

[RszComponentClass("via.navigation.AIMapSection")]
public class AIMapSection(GameObject gameObject, RszInstance data) : AIMapComponentBase(gameObject, data),
    IFixedClassnameComponent
{
    static string IFixedClassnameComponent.Classname => "via.navigation.AIMapSection";

    public override IEnumerable<string> StoredResources => Data.Get(RszFieldCache.AIMapSection.Maps).Select(x => RszFieldCache.MapHandle.Resource.Get((RszInstance)x));
}

public abstract class AIMapComponentBase(GameObject gameObject, RszInstance data) : Component(gameObject, data),
    IEditableComponent<NavmeshEditor>, IGizmoComponent
{
    public abstract IEnumerable<string> StoredResources { get; }

    private readonly List<AimpFile> files = new();
    public IEnumerable<AimpFile> ActiveFiles => files;

    public bool IsEnabled => visibleContentTypes != 0;

    private AimpFile? overrideFile;
    public AimpFile? DisplayedFile => overrideFile;

    public NavmeshContentType visibleContentTypes;

    protected readonly Dictionary<NavmeshContentType, MeshHandle> navMeshes = new();
    protected Material? pointMaterial;
    protected Material? pointMaterial2;
    protected Material? triangleMaterial;
    protected Material? triangleMaterialObscure;
    protected Material? triangleFaceMaterial;
    protected Material? boundsMaterial;
    protected Material? aabbsMaterial;
    protected Material? wallsMaterial;

    public void SetOverrideFile(AimpFile? file)
    {
        if (file == overrideFile) return;

        overrideFile = file;
        UnloadMeshes();
        UpdateResourceFileList();
    }

    protected void UpdateResourceFileList()
    {
        files.Clear();
        if (overrideFile != null) files.Add(overrideFile);
        foreach (var stored in StoredResources) {
            if (string.IsNullOrEmpty(stored)) continue;

            if (Scene!.Workspace.ResourceManager.TryResolveFile(stored, out var file)) {
                var aifile = file.GetFile<AimpFile>();
                if (files.Contains(aifile)) continue;

                files.Add(aifile);
            }
        }
    }

    internal override void OnActivate()
    {
        base.OnActivate();
        Scene!.Root.Gizmos.Add(this);
        Scene!.Root.RegisterEditableComponent(this);
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        Scene!.Root.Gizmos.Remove(this);
        Scene!.Root.UnregisterEditableComponent(this);
        UnloadMeshes();
    }

    private void UnloadMeshes()
    {
        foreach (var m in navMeshes) {
            Scene!.RenderContext.UnloadMesh(m.Value);
        }
        navMeshes.Clear();
    }

    public GizmoContainer? Update(GizmoContainer? gizmo)
    {
        if (visibleContentTypes == NavmeshContentType.None || overrideFile == null) return null;

        if (overrideFile.mainContent != null) UpdateContainer(ref gizmo, overrideFile.mainContent);
        if (overrideFile.secondaryContent != null) UpdateContainer(ref gizmo, overrideFile.secondaryContent);

        return gizmo;
    }

    private void UpdateContainer(ref GizmoContainer? gizmo, ContentGroupContainer container)
    {
        int offset = 0;
        foreach (var content in container.contents) {
            var contentType = content switch {
                ContentGroupMapPoint => NavmeshContentType.Points,
                ContentGroupTriangle => NavmeshContentType.Triangles,
                ContentGroupPolygon => NavmeshContentType.Polygons,
                ContentGroupMapBoundary => NavmeshContentType.Boundaries,
                ContentGroupMapAABB => NavmeshContentType.AABBs,
                ContentGroupWall => NavmeshContentType.Walls,
                _ => NavmeshContentType.None,
            };

            if ((visibleContentTypes & contentType) != contentType) {
                continue;
            }

            gizmo ??= new GizmoContainer(RootScene!, this);

            switch (contentType) {
                case NavmeshContentType.Points:
                    UpdatePoints(gizmo, container, (ContentGroupMapPoint)content);
                    break;
                case NavmeshContentType.Triangles:
                    UpdateTriangles(gizmo, container, (ContentGroupTriangle)content);
                    break;
                case NavmeshContentType.Polygons:
                    UpdatePolygons(gizmo, container, (ContentGroupPolygon)content);
                    break;
                case NavmeshContentType.Boundaries:
                    UpdateBoundaries(gizmo, container, (ContentGroupMapBoundary)content, offset);
                    break;
                case NavmeshContentType.AABBs:
                    UpdateAABBs(gizmo, container, (ContentGroupMapAABB)content, offset);
                    break;
                case NavmeshContentType.Walls:
                    UpdateWalls(gizmo, container, (ContentGroupWall)content, offset);
                    break;
            }
            offset += content.NodeCount;
        }
    }

    private void UpdatePoints(GizmoContainer gizmo, ContentGroupContainer container, ContentGroupMapPoint content)
    {
        if (!navMeshes.TryGetValue(NavmeshContentType.Points, out var mesh)) {
            navMeshes[NavmeshContentType.Points] = mesh = new MeshHandle(new MeshResourceHandle(new LineMesh(overrideFile!, container, content)));
            var builder = new ShapeBuilder(ShapeBuilder.GeometryType.Line, MeshLayout.PositionOnly);
            foreach (var pt in content.Nodes) {
                builder.Add(new Sphere(pt.pos, 0.25f));
            }
            var sh = new ShapeMesh();
            sh.Build(builder);
            mesh.Handle.Meshes.Add(sh);
            Scene!.RenderContext.StoreMesh(mesh.Handle);
        }

        pointMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "pts").Color("_MainColor", new Color(0xff, 0xff, 0xff, 0xff));
        pointMaterial2 ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "pts").Color("_MainColor", new Color(0xff, 0xff, 0xff, 0xff)).Float("_FadeMaxDistance", 250);
        gizmo.Mesh(mesh.Meshes.First(), Transform.WorldTransform, pointMaterial, pointMaterial);
        gizmo.Mesh(mesh.Meshes.Skip(1).First(), Transform.WorldTransform, pointMaterial2);
    }

    private void UpdateTriangles(GizmoContainer gizmo, ContentGroupContainer container, ContentGroupTriangle content)
    {
        if (!navMeshes.TryGetValue(NavmeshContentType.Triangles, out var mesh)) {
            var tri = new TriangleMesh(overrideFile!, container, content);
            navMeshes[NavmeshContentType.Triangles] = mesh = new MeshHandle(new MeshResourceHandle(tri));
            mesh.Handle.Meshes.Add(new LineMesh(tri));
            Scene!.RenderContext.StoreMesh(mesh.Handle);
        }

        triangleMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "tris")
            .Color("_MainColor", new Color(0xcc, 0xee, 0xff, 0xff));
        triangleMaterialObscure ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "tri_obs")
            .Color("_MainColor", new Color(0xcc, 0xee, 0xff, 0xaa))
            .Float("_FadeMaxDistance", 150).Blend();
        triangleFaceMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "tri_face")
            .Color("_MainColor", new Color(0xcc, 0xee, 0xff, 0x88))
            .Float("_FadeMaxDistance", 200).Blend();
        gizmo.Mesh(mesh.Meshes.First(), Transform.WorldTransform, triangleFaceMaterial);
        gizmo.Mesh(mesh.Meshes.Skip(1).First(), Transform.WorldTransform, triangleMaterial, triangleMaterialObscure);
    }

    private void UpdatePolygons(GizmoContainer gizmo, ContentGroupContainer container, ContentGroupPolygon content)
    {
        if (!navMeshes.TryGetValue(NavmeshContentType.Polygons, out var mesh)) {
            var tri = new TriangleMesh(overrideFile!, container, content);
            navMeshes[NavmeshContentType.Polygons] = mesh = new MeshHandle(new MeshResourceHandle(tri));
            mesh.Handle.Meshes.Add(new LineMesh(tri));
            // mesh.Handle.Meshes.Add(new TriangleMesh(overrideFile!, container, content));
            // new LineMesh(overrideFile!, container, content)
            Scene!.RenderContext.StoreMesh(mesh.Handle);
        }

        triangleMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "polys")
            .Color("_MainColor", new Color(0xcc, 0xee, 0xff, 0xff));
        triangleMaterialObscure ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "poly_obs")
            .Color("_MainColor", new Color(0xcc, 0xee, 0xff, 0xaa))
            .Float("_FadeMaxDistance", 150).Blend();
        triangleFaceMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "poly_face")
            .Color("_MainColor", new Color(0xcc, 0xee, 0xff, 0x88))
            .Float("_FadeMaxDistance", 200).Blend();
        gizmo.Mesh(mesh.Meshes.First(), Transform.WorldTransform, triangleFaceMaterial);
        gizmo.Mesh(mesh.Meshes.Skip(1).First(), Transform.WorldTransform, triangleMaterial, triangleMaterialObscure);
    }

    private void UpdateBoundaries(GizmoContainer gizmo, ContentGroupContainer container, ContentGroupMapBoundary content, int offset)
    {
        if (!navMeshes.TryGetValue(NavmeshContentType.Boundaries, out var mesh)) {
            navMeshes[NavmeshContentType.Boundaries] = mesh = new MeshHandle(new MeshResourceHandle(new LineMesh(overrideFile!, container, content, offset)));

            Scene!.RenderContext.StoreMesh(mesh.Handle);
        }

        boundsMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "bounds")
            .Color("_MainColor", new Color(0xff, 0x66, 0xbb, 0xff));
        gizmo.Mesh(mesh.Meshes.First(), Transform.WorldTransform, boundsMaterial);
    }
    private void UpdateAABBs(GizmoContainer gizmo, ContentGroupContainer container, ContentGroupMapAABB content, int nodeOffset)
    {
        if (!navMeshes.TryGetValue(NavmeshContentType.AABBs, out var mesh)) {
            var isMain = container == overrideFile!.mainContent;
            var builder = new ShapeBuilder(isMain ? ShapeBuilder.GeometryType.Line : ShapeBuilder.GeometryType.Filled, MeshLayout.PositionOnly);
            var nodes = container.Nodes.Nodes;
            var polygons = content.Nodes;
            var verts = container.Vertices;
            for (int i = 0; i < content.NodeCount; ++i) {
                var node = nodes[nodeOffset + i];
                var poly = polygons[i];
                var min = verts[poly.indices[0]].Vector3;
                var max = verts[poly.indices[1]].Vector3;
                builder.Add(new AABB(min, max));
            }
            var tri = new ShapeMesh(builder);
            navMeshes[NavmeshContentType.AABBs] = mesh = new MeshHandle(new MeshResourceHandle(tri));
            Scene!.RenderContext.StoreMesh(mesh.Handle);
        }

        aabbsMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "aabb")
            .Color("_MainColor", new Color(0x99, 0x66, 0xbb, 0x66)).Blend();
        gizmo.Mesh(mesh.Meshes.First(), Transform.WorldTransform, aabbsMaterial);
    }

    private void UpdateWalls(GizmoContainer gizmo, ContentGroupContainer container, ContentGroupWall content, int nodeOffset)
    {
        if (!navMeshes.TryGetValue(NavmeshContentType.Walls, out var mesh)) {
            navMeshes[NavmeshContentType.Walls] = mesh = new MeshHandle(new MeshResourceHandle(new TriangleMesh(overrideFile!, container, content, nodeOffset)));
            mesh.Handle.Meshes.Add(new LineMesh(mesh.Meshes.First()));
            Scene!.RenderContext.StoreMesh(mesh.Handle);
        }

        wallsMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "walls")
            .Color("_MainColor", new Color(0xff, 0x88, 0x88, 0xcc)).Blend();

        triangleMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "polys")
            .Color("_MainColor", new Color(0xcc, 0xee, 0xff, 0xff));
        triangleMaterialObscure ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "poly_obs")
            .Color("_MainColor", new Color(0xcc, 0xee, 0xff, 0xaa))
            .Float("_FadeMaxDistance", 150).Blend();
        gizmo.Mesh(mesh.Meshes.First(), Transform.WorldTransform, wallsMaterial);
        gizmo.Mesh(mesh.Meshes.Skip(1).First(), Transform.WorldTransform, triangleMaterial, triangleMaterialObscure);
    }

}