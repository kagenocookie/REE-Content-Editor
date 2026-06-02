using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Tooling.Navmesh;
using ContentPatcher;
using ReeLib;
using ReeLib.Aimp;
using ReeLib.via;

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

public abstract class AIMapComponentBase(GameObject gameObject, RszInstance data) : RenderableComponent(gameObject, data),
    IEditableComponent<NavmeshEditMode>, IGizmoComponent, IScenePickableComponent
{
    public abstract IEnumerable<string> StoredResources { get; }

    private readonly List<AimpFile> files = new();
    public IEnumerable<AimpFile> ActiveFiles => files;

    public override AABB LocalBounds => AABB.Combine(navMeshes.Select(n => n.Value.BoundingBox));

    public bool IsEnabled => visibleContentTypes != 0;

    private AimpFile? overrideFile;
    public AimpFile? DisplayedFile => overrideFile;

    public NavmeshContentType visibleContentTypes;
    private bool isStale;

    private readonly List<NodeInfo> selectedNodes = new();

    protected readonly Dictionary<NavmeshContentType, MeshHandle> navMeshes = new();
    protected Material? waypMaterial;
    protected Material? waypMaterialObscure;
    protected Material? linkMaterial;
    protected Material? pointMaterial;
    protected Material? triangleMaterial;
    protected Material? triangleMaterialObscure;
    protected Material? triangleFaceMaterial;
    protected Material? boundsMaterial;
    protected Material? aabbsMaterial;
    protected Material? wallsMaterial;
    protected Material? selectedMaterial;

    public ulong AttributesFilter { get; set { field = value; isStale = true; } }

    internal override void Render(RenderContext context)
    {
        // the actual rendering is done via gizmos, we just need to be a Renderable to support click picking
    }

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

            if (Scene!.Workspace.ResourceManager.TryResolveGameFile(stored, out var file)) {
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

    public void ResetPreviewGeometry()
    {
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
        if (visibleContentTypes == 0 || overrideFile == null) return null;
        if (isStale) {
            UnloadMeshes();
            isStale = false;
            RecomputeWorldAABB();
        }

        if (overrideFile.mainContent != null) UpdateContainer(ref gizmo, overrideFile.mainContent);
        if (overrideFile.secondaryContent != null) UpdateContainer(ref gizmo, overrideFile.secondaryContent);
        UpdateSelectedNodeDisplay(gizmo);

        return gizmo;
    }

    private ContentGroup? GetContentGroup(NavmeshContentType type)
    {
        if (overrideFile == null) return null;
        return type switch {
            NavmeshContentType.Triangles => overrideFile.mainContent?.contents[0] as ContentGroupTriangle,
            NavmeshContentType.Boundaries => overrideFile.secondaryContent?.contents.OfType<ContentGroupMapBoundary>().FirstOrDefault(),
            NavmeshContentType.Walls => overrideFile.secondaryContent?.contents.OfType<ContentGroupMapBoundary>().FirstOrDefault(),
            NavmeshContentType.AABBs => overrideFile.secondaryContent?.contents.OfType<ContentGroupMapAABB>().FirstOrDefault()
                ?? overrideFile.mainContent?.contents.OfType<ContentGroupMapAABB>().FirstOrDefault(),
            NavmeshContentType.Polygons => overrideFile.secondaryContent?.contents[0] as ContentGroupPolygon,
            NavmeshContentType.Points => overrideFile.mainContent?.contents[0] as ContentGroupMapPoint,
            _ => null,
        };
    }

    private void UpdateContainer(ref GizmoContainer? gizmo, ContentGroupContainer container)
    {
        UpdateLinks(ref gizmo, container, container == overrideFile!.mainContent ? NavmeshContentType.MainLinks : NavmeshContentType.SecondaryLinks);
        foreach (var content in container.contents) {
            NavmeshContentType contentType = content switch {
                ContentGroupMapPoint => NavmeshContentType.Points,
                ContentGroupTriangle => NavmeshContentType.Triangles,
                ContentGroupPolygon => NavmeshContentType.Polygons,
                ContentGroupMapBoundary => NavmeshContentType.Boundaries,
                ContentGroupMapAABB => NavmeshContentType.AABBs,
                ContentGroupWall => NavmeshContentType.Walls,
                _ => 0,
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
                    UpdateBoundaries(gizmo, container, (ContentGroupMapBoundary)content);
                    break;
                case NavmeshContentType.AABBs:
                    UpdateAABBs(gizmo, container, (ContentGroupMapAABB)content);
                    break;
                case NavmeshContentType.Walls:
                    UpdateWalls(gizmo, container, (ContentGroupWall)content);
                    break;
            }
        }
    }

    private void UpdateLinks(ref GizmoContainer? gizmo, ContentGroupContainer container, NavmeshContentType group)
    {
        if ((visibleContentTypes & group) != group) return;

        var isWayp = container.contents[0] is ContentGroupMapPoint;
        gizmo ??= new GizmoContainer(Scene!, this);
        if (!navMeshes.TryGetValue(group, out var mesh)) {
            navMeshes[group] = mesh = new MeshHandle(new MeshResourceHandle(new LineMesh(overrideFile!, container)));
            Scene!.RenderContext.StoreMesh(mesh.Handle);
            RecomputeWorldAABB();
        }

        if (isWayp) {
            waypMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "pts").Color("_MainColor", new Color(0xff, 0xff, 0xff, 0xff));
            waypMaterialObscure ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "pts").Color("_MainColor", new Color(0xff, 0xff, 0xff, 0x30)).Blend();
            gizmo.Mesh(mesh.Meshes.First(), Transform.WorldTransform, waypMaterial, waypMaterialObscure);
        } else {
            linkMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "links").Color("_MainColor", new Color(0xdd, 0x33, 0x77, 0xff));
            gizmo.Mesh(mesh.Meshes.First(), Transform.WorldTransform, linkMaterial, linkMaterial);
        }
    }

    private void UpdatePoints(GizmoContainer gizmo, ContentGroupContainer container, ContentGroupMapPoint content)
    {
        if (!navMeshes.TryGetValue(NavmeshContentType.Points, out var mesh)) {
            var builder = new ShapeBuilder(ShapeBuilder.GeometryType.Line, MeshLayout.PositionOnly);
            foreach (var pt in content.Nodes) {
                builder.Add(new Sphere(pt.pos, 0.25f));
            }
            var sh = new ShapeMesh();
            sh.Build(builder);
            navMeshes[NavmeshContentType.Points] = mesh = new MeshHandle(new MeshResourceHandle(sh));
            Scene!.RenderContext.StoreMesh(mesh.Handle);
            RecomputeWorldAABB();
        }

        pointMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "pts").Color("_MainColor", new Color(0xff, 0xff, 0xff, 0xff)).Float("_FadeMaxDistance", 250);
        gizmo.Mesh(mesh.Meshes.First(), Transform.WorldTransform, pointMaterial);
    }

    private void UpdateTriangles(GizmoContainer gizmo, ContentGroupContainer container, ContentGroupTriangle content)
    {
        if (!navMeshes.TryGetValue(NavmeshContentType.Triangles, out var mesh)) {
            var tri = new TriangleMesh(overrideFile!, container, content, AttributesFilter);
            navMeshes[NavmeshContentType.Triangles] = mesh = new MeshHandle(new MeshResourceHandle(tri));
            mesh.Handle.Meshes.Add(new LineMesh(tri));
            Scene!.RenderContext.StoreMesh(mesh.Handle);
            RecomputeWorldAABB();
        }

        triangleMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "tris")
            .Color("_MainColor", new Color(0xcc, 0xee, 0xff, 0xff));
        triangleMaterialObscure ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "tri_obs")
            .Color("_MainColor", new Color(0xcc, 0xee, 0xff, 0x50))
            .Float("_FadeMaxDistance", 120).Blend();
        triangleFaceMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "tri_face")
            .Color("_MainColor", new Color(0xcc, 0xee, 0xff, 0x88))
            .Float("_FadeMaxDistance", 200).Blend();
        gizmo.Mesh(mesh.Meshes.First(), Transform.WorldTransform, triangleFaceMaterial);
        gizmo.Mesh(mesh.Meshes.Skip(1).First(), Transform.WorldTransform, triangleMaterial, triangleMaterialObscure);
        // gizmo.Mesh(mesh.Meshes.Skip(1).First(), Transform.WorldTransform, triangleMaterial);
    }

    private void UpdatePolygons(GizmoContainer gizmo, ContentGroupContainer container, ContentGroupPolygon content)
    {
        if (!navMeshes.TryGetValue(NavmeshContentType.Polygons, out var mesh)) {
            var tri = new TriangleMesh(overrideFile!, container, content, AttributesFilter);
            navMeshes[NavmeshContentType.Polygons] = mesh = new MeshHandle(new MeshResourceHandle(tri));
            mesh.Handle.Meshes.Add(new LineMesh(tri));
            Scene!.RenderContext.StoreMesh(mesh.Handle);
            RecomputeWorldAABB();
        }

        triangleMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "tris")
            .Color("_MainColor", new Color(0xcc, 0xee, 0xff, 0xff));
        triangleMaterialObscure ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "tri_obs")
            .Color("_MainColor", new Color(0xcc, 0xee, 0xff, 0x50))
            .Float("_FadeMaxDistance", 120).Blend();
        triangleFaceMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "tri_face")
            .Color("_MainColor", new Color(0xcc, 0xee, 0xff, 0x88))
            .Float("_FadeMaxDistance", 200).Blend();
        gizmo.Mesh(mesh.Meshes.First(), Transform.WorldTransform, triangleFaceMaterial);
        gizmo.Mesh(mesh.Meshes.Skip(1).First(), Transform.WorldTransform, triangleMaterial, triangleMaterialObscure);
    }

    private void UpdateBoundaries(GizmoContainer gizmo, ContentGroupContainer container, ContentGroupMapBoundary content)
    {
        if (!navMeshes.TryGetValue(NavmeshContentType.Boundaries, out var mesh)) {
            navMeshes[NavmeshContentType.Boundaries] = mesh = new MeshHandle(new MeshResourceHandle(new LineMesh(overrideFile!, container, content)));

            Scene!.RenderContext.StoreMesh(mesh.Handle);
            RecomputeWorldAABB();
        }

        boundsMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.GizmoVertexColor, "bounds")
            .Color("_MainColor", new Color(0xff, 0x66, 0xbb, 0xff));
        gizmo.Mesh(mesh.Meshes.First(), Transform.WorldTransform, boundsMaterial);

        gizmo.BeginControl();
        foreach (var info in content.NodeInfos) {
            var node = content.Nodes[info.localIndex];
            var aabb = new AABB(node.min, node.max);
            if (gizmo.Cur.EditableAABB(Matrix4x4.Identity, ref aabb, out int _)) {
                UndoRedo.RecordCallbackSetter(null, node, new AABB(node.min, node.max), aabb, (nn, val) => {
                    nn.min = val.minpos;
                    nn.max = val.maxpos;

                    NavmeshGenerator.PostProcessAdditionalNodes(overrideFile!);
                    overrideFile!.PackData();
                    isStale = true;
                    // TODO mark file handle as modified (we don't have the file handle here...)
                    // Scene.Workspace.ResourceManager.TryResolveGameFile(overrideFiley)
                }, $"{info}");
            }
        }
    }

    private void UpdateAABBs(GizmoContainer gizmo, ContentGroupContainer container, ContentGroupMapAABB content)
    {
        var infos = container.NodeInfo.Nodes;
        var nodes = content.Nodes;
        if (!navMeshes.TryGetValue(NavmeshContentType.AABBs, out var mesh)) {
            var isMain = container == overrideFile!.mainContent;
            var builder = new ShapeBuilder(isMain ? ShapeBuilder.GeometryType.Line : ShapeBuilder.GeometryType.Filled, MeshLayout.PositionOnly);
            var verts = container.Vertices;
            foreach (var info in content.NodeInfos) {
                var node = content.Nodes[info.localIndex];
                builder.Add(node.bounds);
            }
            var tri = new ShapeMesh(builder);
            navMeshes[NavmeshContentType.AABBs] = mesh = new MeshHandle(new MeshResourceHandle(tri));
            Scene!.RenderContext.StoreMesh(mesh.Handle);
            RecomputeWorldAABB();
        }

        aabbsMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "aabb")
            .Color("_MainColor", new Color(0x99, 0x66, 0xbb, 0x66)).Blend();
        gizmo.Mesh(mesh.Meshes.First(), Transform.WorldTransform, aabbsMaterial);
    }

    private void UpdateWalls(GizmoContainer gizmo, ContentGroupContainer container, ContentGroupWall content)
    {
        if (!navMeshes.TryGetValue(NavmeshContentType.Walls, out var mesh)) {
            navMeshes[NavmeshContentType.Walls] = mesh = new MeshHandle(new MeshResourceHandle(new TriangleMesh(overrideFile!, container, content)));
            mesh.Handle.Meshes.Add(new LineMesh(mesh.Meshes.First()));
            Scene!.RenderContext.StoreMesh(mesh.Handle);
            RecomputeWorldAABB();
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

    private void UpdateSelectedNodeDisplay(GizmoContainer? gizmo)
    {
        if (overrideFile == null) return;

        selectedMaterial ??= Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "selected")
            .Color("_MainColor", new Color(0xff, 0xff, 0xff, 0x55)).Blend();

        gizmo ??= new GizmoContainer(Scene!, this);
        gizmo.PushMaterial(selectedMaterial, selectedMaterial, ShapeBuilder.GeometryType.Filled, 5);
        if (selectedNodes.Count > 0) {
            foreach (var node in selectedNodes) {
                var group = overrideFile.GetGroupForNode(node);
                var container = overrideFile.mainContent!.contents.Contains(group) == true ? overrideFile.mainContent : overrideFile.secondaryContent;
                if (group is ContentGroupTriangle triGroup) {
                    if (!this.visibleContentTypes.HasFlag(NavmeshContentType.Triangles)) continue;
                    var triNode = triGroup.Nodes[node.localIndex];
                    var p1 = container!.Vertices![triNode.index1].Vector3;
                    var p2 = container.Vertices[triNode.index2].Vector3;
                    var p3 = container.Vertices[triNode.index3].Vector3;
                    var norm = Vector3.Normalize(Vector3.Cross(p2 - p1, p3 - p2)) * 0.01f;
                    gizmo.Cur.Add(new Triangle(p1 + norm, p2 + norm, p3 + norm));
                } else if (group is ContentGroupPolygon polyGroup) {
                    if (!this.visibleContentTypes.HasFlag(NavmeshContentType.Polygons)) continue;
                    var polyNode = polyGroup.Nodes[node.localIndex];
                    for (int i = 2; i < polyNode.indices.Length; i++) {
                        var p1 = container!.Vertices![polyNode.indices[0]].Vector3;
                        var p2 = container.Vertices[polyNode.indices[i - 1]].Vector3;
                        var p3 = container.Vertices[polyNode.indices[i]].Vector3;
                        var norm = Vector3.Normalize(Vector3.Cross(p2 - p1, p3 - p2)) * 0.01f;
                        gizmo.Cur.Add(new Triangle(p1 + norm, p2 + norm, p3 + norm));
                    }
                }
            }
        }
        if (boundaryStart.X != float.MaxValue) {
            gizmo.Cur.Add(new OBB(Matrix4x4.CreateTranslation(boundaryStart), new Vector3(0.5f)));
        }
        gizmo.PopMaterial();
    }

    private Vector3 boundaryStart = new Vector3(float.MaxValue);

    public void HandleSelect(IntersectionInfo info, int contextId, ISceneEditor editor)
    {
        if (overrideFile?.Header.mapType != MapType.Navmesh) {
            Logger.Warn("AIMap scene operations only supported for navmesh type maps");
            return;
        }
        selectedNodes.Clear();
        var type = (NavmeshContentType)contextId;
        if (!navMeshes.TryGetValue(type, out var meshHandle)) {
            return;
        }
        var mesh = meshHandle.Meshes.ElementAtOrDefault(info.meshIndex);
        var group = GetContentGroup(type);
        if (mesh == null || group == null) {
            return;
        }
        var navEdit = editor.GetScene()?.Root.ActiveEditMode as NavmeshEditMode;
        var selectMode = navEdit?.Mode ?? SceneMode.Selection;
        var fill = navEdit?.AttributeFill ?? false;
        var attr = navEdit?.SelectedAttribute ?? 0;
        if (selectMode == SceneMode.AddBoundary) {
            if (boundaryStart.X == float.MaxValue) {
                boundaryStart = info.point;
            } else {
                var boundaryEnd = info.point;

                var aabbGroup = overrideFile.GetOrAddGroup<ContentGroupMapAABB>();
                var boundaryGroup = overrideFile.GetOrAddGroup<ContentGroupMapBoundary>();
                if (aabbGroup == null || boundaryGroup == null) {
                    Logger.Error("Could not create boundary group in map file");
                    return;
                }
                var node1_aabb = new AABBNode(AABB.CreateFromOrigin(boundaryStart, new Vector3(0.5f)));
                var node2_aabb = new AABBNode(AABB.CreateFromOrigin(boundaryEnd, new Vector3(0.5f)));
                var info1_aabb = overrideFile.CreateNode(aabbGroup, node1_aabb);
                var info2_aabb = overrideFile.CreateNode(aabbGroup, node2_aabb);
                var node1 = new ContentGroupMapBoundary.MapBoundaryNode(node1_aabb.bounds);
                var node2 = new ContentGroupMapBoundary.MapBoundaryNode(node2_aabb.bounds);
                var info1 = overrideFile.CreateNode(boundaryGroup, node1);
                var info2 = overrideFile.CreateNode(boundaryGroup, node2);
                info1.PairNodes.Add(info1_aabb);
                info2.PairNodes.Add(info2_aabb);
                info1_aabb.PairNodes.Add(info1);
                info2_aabb.PairNodes.Add(info2);

                UndoRedo.RecordCallback(null, () => {
                    if (!boundaryGroup.Nodes.Contains(node1)) {
                        boundaryGroup.Nodes.Add(node1);
                        boundaryGroup.Nodes.Add(node2);
                        info1.localIndex = boundaryGroup.NodeInfos.Count;
                        boundaryGroup.NodeInfos.Add(info1);
                        info2.localIndex = boundaryGroup.NodeInfos.Count;
                        boundaryGroup.NodeInfos.Add(info2);
                        aabbGroup!.Nodes.Add(node1_aabb);
                        aabbGroup.Nodes.Add(node2_aabb);
                        info1_aabb.localIndex = aabbGroup.NodeInfos.Count;
                        info2_aabb.localIndex = aabbGroup.NodeInfos.Count;
                        aabbGroup.NodeInfos.Add(info1_aabb);
                        aabbGroup.NodeInfos.Add(info2_aabb);
                        info1.Links.Clear();
                        info2.Links.Clear();
                    }

                    info1.Links.Add(new LinkInfo() { SourceNode = info1, TargetNode = info2, sourceNodeIndex = info1.index, targetNodeIndex = info2.index });
                    info2.Links.Add(new LinkInfo() { SourceNode = info2, TargetNode = info1, sourceNodeIndex = info2.index, targetNodeIndex = info1.index });
                    NavmeshGenerator.PostProcessAdditionalNodes(overrideFile);
                    overrideFile.PackData();
                    isStale = true;
                }, () => {
                    overrideFile.RemoveNode(boundaryGroup, info1);
                    overrideFile.RemoveNode(boundaryGroup, info2);
                    overrideFile.RemoveNode(aabbGroup, info1_aabb);
                    overrideFile.RemoveNode(aabbGroup, info2_aabb);
                    NavmeshGenerator.PostProcessAdditionalNodes(overrideFile);
                    overrideFile.PackData();
                    isStale = true;
                });

                boundaryStart = new Vector3(float.MaxValue);
            }
            return;
        }
        boundaryStart = new Vector3(float.MaxValue);

        NodeInfo? nodeInfo = null;
        if (type == NavmeshContentType.Triangles) {
            var nodeId = info.triangleIndex;
            nodeInfo = group.NodeInfos.ElementAtOrDefault(nodeId);
        } else if (type == NavmeshContentType.Polygons) {
            var polyGroup = (ContentGroupPolygon)group;
            var nodeId = 0;
            var curTriIndex = 0;
            foreach (var polyNode in polyGroup.Nodes) {
                var polyTriCount = polyNode.indices.Length - 2;
                if (curTriIndex + polyTriCount > info.triangleIndex) {
                    break;
                }
                curTriIndex += polyTriCount;
                nodeId++;
            }

            nodeInfo = group.NodeInfos.ElementAtOrDefault(nodeId);
        }

        if (nodeInfo != null) {
            if (selectMode == SceneMode.SetAttribute) {
                ModifyAttributes(nodeInfo, group, fill, (ulong)attr, false);
            } else if (selectMode == SceneMode.RemoveAttribute) {
                ModifyAttributes(nodeInfo, group, fill, (ulong)attr, true);
            }

            selectedNodes.Add(nodeInfo);
            (editor as IInspectorController)?.Inspector.PrimaryTarget = new InspectorComponentLink(this, nodeInfo);
        } else {
            (editor as IInspectorController)?.Inspector.PrimaryTarget = new InspectorComponentLink(this, group);
        }
    }

    private void ModifyAttributes(NodeInfo node, ContentGroup group, bool propagateNeighbors, ulong attributes, bool remove)
    {
        var list = new List<NodeInfo>() { node };
        var attrsBackup = new Dictionary<NodeInfo, ulong>();
        for (int i = 0; i < list.Count; i++) {
            var item = list[i];
            attrsBackup[item] = item.attributes;
            if (!propagateNeighbors) break;
            foreach (var nei in item.Links) {
                if (nei.TargetNode?.attributes == node.attributes && attrsBackup.TryAdd(nei.TargetNode, node.attributes)) {
                    list.Add(nei.TargetNode);
                }
                if (nei.SourceNode?.attributes == node.attributes && attrsBackup.TryAdd(nei.SourceNode, node.attributes)) {
                    list.Add(nei.SourceNode);
                }
            }
        }
        var setAttrs = () => {
            foreach (var item in list) item.attributes |= attributes;
        };
        var removeAttrs = () => {
            foreach (var item in list) item.attributes &= ~attributes;
        };
        var restoreAttrs = () => {
            foreach (var item in list) item.attributes = attrsBackup[item];
        };
        UndoRedo.RecordCallback(null, remove ? removeAttrs : setAttrs, restoreAttrs);
        UndoRedo.AttachCallbackToLastAction(UndoRedo.CallbackType.Both, () => isStale = true);
    }

    public void CollectPickables(PickableData data)
    {
        if (overrideFile == null) return;

        if (visibleContentTypes.HasFlag(NavmeshContentType.Triangles) && navMeshes.TryGetValue(NavmeshContentType.Triangles, out var mesh)) {
            var bounds = WorldSpaceBounds;
            data.TryAdd(this, (int)NavmeshContentType.Triangles, mesh, Transform.WorldTransform, bounds);
        }

        if (visibleContentTypes.HasFlag(NavmeshContentType.Polygons) && navMeshes.TryGetValue(NavmeshContentType.Polygons, out mesh)) {
            var bounds = WorldSpaceBounds;
            data.TryAdd(this, (int)NavmeshContentType.Polygons, mesh, Transform.WorldTransform, bounds);
        }
    }
}
