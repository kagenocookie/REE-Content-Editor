using System.Diagnostics;
using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;
using DotRecast.Core;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using ReeLib;
using ReeLib.Aimp;
using ReeLib.via;

namespace ContentEditor.App.Tooling.Navmesh;

public static class NavmeshGenerator
{
    // TODO implement ReEngineInputGeomProviders that can handle a list of input resources (.mcol, .mesh, RSZ collider shapes, .coco, .hf, .chf, ...)
    // and convert all of them into suitable shape geometry

    public static AimpFile RebuildNavmeshFromMesh(AimpFile targetFile, NavmeshBuildParams config, string objFile)
    {
        var geo = SimpleInputGeomProvider.LoadFile(objFile);
        return RebuildNavmeshFromMesh(targetFile, config, geo);
    }

    public static SimpleInputGeomProvider GeometryFromMeshResource(Mesh mesh)
    {
        var vertSize = mesh.layout.VertexSize;
        var tris = new float[mesh.VertexData.Length / vertSize * 3];
        for (int i = 0; i < tris.Length / 3; ++i) {
            tris[i * 3 + 0] = mesh.VertexData[i * vertSize + 0];
            tris[i * 3 + 1] = mesh.VertexData[i * vertSize + 1];
            tris[i * 3 + 2] = mesh.VertexData[i * vertSize + 2];
        }
        var geo = new SimpleInputGeomProvider(tris, mesh.Indices);
        return geo;
    }

    public static AimpFile RebuildNavmeshFromMesh(AimpFile targetFile, NavmeshBuildParams config, IInputGeomProvider geometry)
    {
        // for the most part, copied over from https://github.com/ikpil/DotRecast/blob/main/test/DotRecast.Recast.Test/RecastSoloMeshTest.cs
        // also see the link for notes regarding partition types

        var cfg = config.GetConfig();

        var bmin = geometry.GetMeshBoundsMin();
        var bmax = geometry.GetMeshBoundsMax();
        var bcfg = new RcBuilderConfig(cfg, bmin, bmax);
        var m_ctx = new RcContext();

        var m_solid = new RcHeightfield(bcfg.width, bcfg.height, bcfg.bmin, bcfg.bmax, cfg.Cs, cfg.Ch, cfg.BorderSize);

        foreach (RcTriMesh geom in geometry.Meshes())
        {
            float[] verts = geom.GetVerts();
            int[] tris = geom.GetTris();
            int ntris = tris.Length / 3;

            // Allocate array that can hold triangle area types.
            // If you have multiple meshes you need to process, allocate
            // and array which can hold the max number of triangles you need to process.

            // Find triangles which are walkable based on their slope and rasterize them.
            // If your input data is multiple meshes, you can transform them here, calculate
            // the are type for each of the meshes and rasterize them.
            int[] m_triareas = RcRecast.MarkWalkableTriangles(m_ctx, cfg.WalkableSlopeAngle, verts, tris, ntris, cfg.WalkableAreaMod);
            RcRasterizations.RasterizeTriangles(m_ctx, verts, tris, m_triareas, ntris, m_solid, cfg.WalkableClimb);
        }


        //
        // Step 3. Filter walkable surfaces.
        //

        // Once all geometry is rasterized, we do initial pass of filtering to
        // remove unwanted overhangs caused by the conservative rasterization
        // as well as filter spans where the character cannot possibly stand.
        RcFilters.FilterLowHangingWalkableObstacles(m_ctx, cfg.WalkableClimb, m_solid);
        RcFilters.FilterLedgeSpans(m_ctx, cfg.WalkableHeight, cfg.WalkableClimb, m_solid);
        RcFilters.FilterWalkableLowHeightSpans(m_ctx, cfg.WalkableHeight, m_solid);


        //
        // Step 4. Partition walkable surface to simple regions.
        //

        // Compact the heightfield so that it is faster to handle from now on.
        // This will result more cache coherent data as well as the neighbours
        // between walkable cells will be calculated.
        RcCompactHeightfield m_chf = RcCompacts.BuildCompactHeightfield(m_ctx, cfg.WalkableHeight, cfg.WalkableClimb, m_solid);

        // Erode the walkable area by agent radius.
        RcAreas.ErodeWalkableArea(m_ctx, cfg.WalkableRadius, m_chf);


        if (config.PartitionType == RcPartition.WATERSHED)
        {
            // Prepare for region partitioning, by calculating distance field
            // along the walkable surface.
            RcRegions.BuildDistanceField(m_ctx, m_chf);
            // Partition the walkable surface into simple regions without holes.
            RcRegions.BuildRegions(m_ctx, m_chf, cfg.MinRegionArea, cfg.MergeRegionArea);
        }
        else if (config.PartitionType == RcPartition.MONOTONE)
        {
            // Partition the walkable surface into simple regions without holes.
            // Monotone partitioning does not need distancefield.
            RcRegions.BuildRegionsMonotone(m_ctx, m_chf, cfg.MinRegionArea, cfg.MergeRegionArea);
        }
        else
        {
            // Partition the walkable surface into simple regions without holes.
            RcRegions.BuildLayerRegions(m_ctx, m_chf, cfg.MinRegionArea);
        }

        //
        // Step 5. Trace and simplify region contours.
        //

        // Create contours.
        RcContourSet m_cset = RcContours.BuildContours(m_ctx, m_chf, cfg.MaxSimplificationError, cfg.MaxEdgeLen, RcBuildContoursFlags.RC_CONTOUR_TESS_WALL_EDGES);

        //
        // Step 6. Build polygons mesh from contours.
        //

        // Build polygon navmesh from the contours.
        RcPolyMesh polyMesh = RcMeshs.BuildPolyMesh(m_ctx, m_cset, cfg.MaxVertsPerPoly);
        if (polyMesh.npolys == 0) {
            Logger.Error("Navmesh polygon generation failed");
            return targetFile;
        }

        //
        // Step 7. Create detail mesh which allows to access approximate height
        // on each polygon.
        //

        RcPolyMeshDetail detailMesh = RcMeshDetails.BuildPolyMeshDetail(m_ctx, polyMesh, m_chf, cfg.DetailSampleDist, cfg.DetailSampleMaxError);

        // generate navigation runtime data

        var dtParams = new DtNavMeshCreateParams();
        dtParams.verts = polyMesh.verts;
        dtParams.vertCount = polyMesh.nverts;
        dtParams.polys = polyMesh.polys;
        dtParams.polyAreas = polyMesh.areas;
        dtParams.polyFlags = polyMesh.flags;
        dtParams.polyCount = polyMesh.npolys;
        dtParams.nvp = polyMesh.nvp;

        dtParams.detailMeshes = detailMesh.meshes;
        dtParams.detailVerts = detailMesh.verts;
        dtParams.detailVertsCount = detailMesh.nverts;
        dtParams.detailTris = detailMesh.tris;
        dtParams.detailTriCount = detailMesh.ntris;

        dtParams.walkableHeight = cfg.WalkableHeight;
        dtParams.walkableRadius = cfg.WalkableRadius;
        dtParams.walkableClimb = cfg.WalkableClimb;
        dtParams.cs = cfg.Cs;
        dtParams.ch = cfg.Ch;
        dtParams.bmin = bmin;
        dtParams.bmax = bmax;
        dtParams.buildBvTree =  true;

        var detourMesh = DtNavMeshBuilder.CreateNavMeshData(dtParams);
        if (detourMesh == null) {
            Logger.Error("Failed to generate navmesh data");
            return targetFile;
        }
        var navmesh = new DtNavMesh();
        var status = navmesh.Init(detourMesh, cfg.MaxVertsPerPoly, 0);
        if (!status.Succeeded()) {
            Logger.Error("Failed to generate navmesh");
            return targetFile;
        }

        // now convert the Rc mesh into RE navmesh
        StoreNavmesh(targetFile, polyMesh, detailMesh, navmesh);

        return targetFile;
    }

    private static void StoreNavmesh(AimpFile targetFile, RcPolyMesh polyMesh, RcPolyMeshDetail detailMesh, DtNavMesh navmesh)
    {
        targetFile.InitContentGroups(detailMesh.nverts, polyMesh.nverts);

        var tile = navmesh.GetTile(0);

        var triContent = targetFile.mainContent!;
        var polyContent = targetFile.secondaryContent!;
        var triGroup = triContent.InitGroup<ContentGroupTriangle>(detailMesh.nverts, true);
        var polyGroup = polyContent.InitGroup<ContentGroupPolygon>(polyMesh.nverts, true);

        // remove all previous triangle/polygon data but retain other content group nodes so we can put them back in later
        triGroup.Nodes.Clear();
        polyGroup.Nodes.Clear();
        triGroup.NodeInfos.Clear();
        polyGroup.NodeInfos.Clear();
        triContent.bounds = new AABB(polyMesh.bmin, polyMesh.bmax);
        polyContent.bounds = new AABB(polyMesh.bmin, polyMesh.bmax);

        // add all polygon verts
        for (int i = 0; i < polyMesh.nverts; ++i) {
            var vert = polyGroup.Vertices![i] = new Vector3(
                polyMesh.bmin.X + polyMesh.verts[i * 3 + 0] * polyMesh.cs,
                polyMesh.bmin.Y + polyMesh.verts[i * 3 + 1] * polyMesh.ch,
                polyMesh.bmin.Z + polyMesh.verts[i * 3 + 2] * polyMesh.cs);
            polyContent.Vertices[i] = new PaddedVec3(vert);
        }

        // add all triangle verts
        for (int i = 0; i < detailMesh.nverts; ++i) {
            var vert = triGroup.Vertices![i] = new Vector3(
                detailMesh.verts[i * 3 + 0],
                detailMesh.verts[i * 3 + 1],
                detailMesh.verts[i * 3 + 2]);
            triContent.Vertices[i] = new PaddedVec3(vert);
        }

        // build up polygon nodes list
        var polyIndices = new List<int>();
        for (int i = 0; i < polyMesh.npolys; i++) {
            int p = i * polyMesh.nvp * 2;
            var poly = new PolygonNode() { version = targetFile.Header.Version };
            polyIndices.Clear();
            var aabb = AABB.MaxMin;
            for (int j = 0; j < polyMesh.nvp; ++j) {
                int v = polyMesh.polys[p + j];
                if (v == RcRecast.RC_MESH_NULL_IDX) break;

                polyIndices.Add(v);
                aabb = aabb.Extend(polyGroup.Vertices![v]);
            }
            poly.indices = polyIndices.ToArray();
            poly.min = aabb.minpos;
            poly.max = aabb.maxpos;
            poly.attributes.Init(poly.indices.Length);
            polyGroup.Nodes.Add(poly);
            var nodeInfo = new NodeInfo() {
                attributes = 0,
                flags = 0,
                index = polyGroup.NodeInfos.Count,
                groupIndex = 0,
                userdataIndex = 0,
                nextIndex = polyGroup.NodeInfos.Count + 1,
            };
            polyGroup.NodeInfos.Add(nodeInfo);
        }

        // store all polygon-to-polygon links
        int totalLinkCount = 0;
        for (int i = 0; i < polyMesh.npolys; i++) {
            var nodeInfo = polyGroup.NodeInfos[i];
            var polyData = tile.data.polys[i];

            for (int linkIndex = 0; linkIndex < polyData.neis.Length; linkIndex++) {
                int nei = polyData.neis[linkIndex];
                if (nei == 0) continue;

                nodeInfo.Links.Add(new LinkInfo() {
                    sourceNodeIndex = nodeInfo.index,
                    targetNodeIndex = nei,
                    attributes = 0,
                    index = totalLinkCount++,
                    SourceNode = nodeInfo,
                    TargetNode = polyGroup.NodeInfos[nei - 1],
                    edgeIndex = linkIndex,
                });
            }
        }

        // build up triangle nodes list
        totalLinkCount = 0;
        polyGroup.triangleIndices = new IndexSet[polyMesh.npolys];
        triGroup.polygonIndices = [];
        var triIndices = new List<int>();
        var polyTreeIndices = new List<int>();
        for (int m = 0; m < detailMesh.nmeshes; ++m) {
            int firstVert = detailMesh.meshes[m * 4];
            var poly = tile.data.polys[m];
            var polyNodeInfo = polyGroup.NodeInfos[m];
            int firstTriangle = detailMesh.meshes[m * 4 + 2];
            triIndices.Clear();
            for (int f = 0; f < detailMesh.meshes[m * 4 + 3]; f++)
            {
                ref DtPolyDetail reference = ref tile.data.detailMeshes[m];
                int num2 = (reference.triBase + f) * 4;

                var triNode = new TriangleNode();
                triNode.index1 = (firstVert + detailMesh.tris[(firstTriangle + f) * 4 + 0]);
                triNode.index2 = (firstVert + detailMesh.tris[(firstTriangle + f) * 4 + 1]);
                triNode.index3 = (firstVert + detailMesh.tris[(firstTriangle + f) * 4 + 2]);
                triNode.attributes.Init(3);
                triGroup.Nodes.Add(triNode);
                var nodeInfo = new NodeInfo() {
                    attributes = 0,
                    flags = 0,
                    index = triGroup.NodeInfos.Count,
                    groupIndex = 0,
                    userdataIndex = 0,
                    nextIndex = triGroup.NodeInfos.Count + 1,
                };
                triGroup.NodeInfos.Add(nodeInfo);

                triIndices.Add(nodeInfo.index);
                polyTreeIndices.Add(m);
                nodeInfo.PairNodes.Add(polyNodeInfo);
                polyNodeInfo.PairNodes.Add(nodeInfo);
            }
            polyGroup.triangleIndices[m] = new IndexSet() { indices = triIndices.ToArray() };
        }

        triGroup.polygonIndices = polyTreeIndices.ToArray();
        triContent.NodeInfo.maxIndex = triGroup.NodeCount;
        polyContent.NodeInfo.maxIndex = polyGroup.NodeCount;

        // store triangle neighbor links
        var neighborPolyTriangles = new List<NodeInfo>();
        int triCount = 0;
        for (int m = 0; m < detailMesh.nmeshes; ++m) {
            int firstVert = detailMesh.meshes[m * 4];
            var poly = tile.data.polys[m];
            neighborPolyTriangles.Clear();
            foreach (var pn in poly.neis) {
                if (pn == 0) continue;

                var tris = polyGroup.triangleIndices[pn - 1].indices;
                foreach (var tri in tris) {
                    neighborPolyTriangles.Add(triGroup.NodeInfos[tri]);
                }
            }
            foreach (var trii in polyGroup.triangleIndices[m].indices) {
                neighborPolyTriangles.Add(triGroup.NodeInfos[trii]);
            }

            int firstTriangle = detailMesh.meshes[m * 4 + 2];
            for (int f = 0; f < detailMesh.meshes[m * 4 + 3]; f++)
            {
                ref DtPolyDetail reference = ref tile.data.detailMeshes[m];
                int num2 = (reference.triBase + f) * 4;

                var nodeInfo = triGroup.NodeInfos[triCount++];
                var triInfo = triGroup.Nodes[nodeInfo.index];

                // default recast/detour does not give us triangle link infos directly, so we need to figure them out ourselves
                var (otherTriangle, edgeIndex) = FindTriangleNeighborIndex(triGroup, neighborPolyTriangles, triInfo.index1, triInfo.index2, nodeInfo.index);
                if (otherTriangle != -1) {
                    nodeInfo.Links.Add(new LinkInfo() {
                        attributes = 0,
                        index = totalLinkCount++,
                        sourceNodeIndex = nodeInfo.index,
                        targetNodeIndex = otherTriangle,
                        SourceNode = nodeInfo,
                        TargetNode = triGroup.NodeInfos[otherTriangle],
                        edgeIndex = edgeIndex,
                    });
                    Debug.Assert(nodeInfo != triGroup.NodeInfos[otherTriangle]);
                }

                (otherTriangle, edgeIndex) = FindTriangleNeighborIndex(triGroup, neighborPolyTriangles, triInfo.index2, triInfo.index3, nodeInfo.index);
                if (otherTriangle != -1) {
                    nodeInfo.Links.Add(new LinkInfo() {
                        attributes = 0,
                        index = totalLinkCount++,
                        sourceNodeIndex = nodeInfo.index,
                        targetNodeIndex = otherTriangle,
                        SourceNode = nodeInfo,
                        TargetNode = triGroup.NodeInfos[otherTriangle],
                        edgeIndex = edgeIndex,
                    });
                    Debug.Assert(nodeInfo != triGroup.NodeInfos[otherTriangle]);
                }

                (otherTriangle, edgeIndex) = FindTriangleNeighborIndex(triGroup, neighborPolyTriangles, triInfo.index3, triInfo.index1, nodeInfo.index);
                if (otherTriangle != -1) {
                    nodeInfo.Links.Add(new LinkInfo() {
                        attributes = 0,
                        index = totalLinkCount++,
                        sourceNodeIndex = nodeInfo.index,
                        targetNodeIndex = otherTriangle,
                        SourceNode = nodeInfo,
                        TargetNode = triGroup.NodeInfos[otherTriangle],
                        edgeIndex = edgeIndex,
                    });
                    Debug.Assert(nodeInfo != triGroup.NodeInfos[otherTriangle]);
                }
            }
        }

        static (int index, int edgeIndex) FindTriangleNeighborIndex(ContentGroupTriangle triGroup, List<NodeInfo> neighbors, int localIndex1, int localIndex2, int selfNodeIndex)
        {
            // one of the neighboring polygons should contain a triangle that shares 2 vertices with our reference triangle
            // if not found then there's no neighbor polygon
            // need to match by vertex position and not index because the default recast implementation doesn't seem to be deduplicating vert indices (?)
            var local1 = triGroup.Vertices![localIndex1];
            var local2 = triGroup.Vertices[localIndex2];
            foreach (var node in neighbors) {
                if (node.index == selfNodeIndex) continue;
                var otherTri = triGroup.Nodes[node.index];
                var match1 = triGroup.Vertices![otherTri.index1] == local1 || triGroup.Vertices![otherTri.index1] == local2;
                var match2 = triGroup.Vertices![otherTri.index2] == local1 || triGroup.Vertices![otherTri.index2] == local2;
                var match3 = triGroup.Vertices![otherTri.index3] == local1 || triGroup.Vertices![otherTri.index3] == local2;

                if (match1 && match2) return (node.index, 0);
                if (match2 && match3) return (node.index, 1);
                if (match1 && match3) return (node.index, 2);
            }

            return (-1, 0);
        }

        PostProcessAdditionalNodes(targetFile);
        targetFile.PackData();
    }

    private static void PostProcessAdditionalNodes(AimpFile targetFile)
    {
        // TODO properly re-generate Boundary/AABB/Wall nodes and their links
        // just deleting them for now
        while (targetFile.mainContent!.contents.Length > 1) {
            targetFile.mainContent.RemoveGroup(targetFile.mainContent.contents[1]);
        }
        while (targetFile.secondaryContent!.contents.Length > 1) {
            targetFile.secondaryContent.RemoveGroup(targetFile.secondaryContent.contents[1]);
        }

    }
}
