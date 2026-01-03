using DotRecast.Recast;

namespace ContentEditor.App.Tooling.Navmesh;

public class NavmeshBuildParams
{
    public RcPartition PartitionType { get; set; } = RcPartition.WATERSHED;
    public float CellSize { get; set; } = 0.2f;
    public float CellHeight { get; set; } = 0.25f;
    public float AgentHeight { get; set; } = 1.7f;
    public float AgentRadius { get; set; } = 0.25f;
    public float AgentMaxClimb { get; set; } = 0.3f;
    public float AgentMaxSlopeAngle { get; set; } = 40f;
    public float RegionMinArea { get; set; } = 8;
    public float RegionMergeArea { get; set; } = 20;
    public float ContourEdgeMaxLength { get; set; } = 12;
    public float ContourMaxError { get; set; } = 1.3f;
    public float DetailSampleDistance { get; set; } = 6f;
    public float DetailSampleMaxError { get; set; } = 1f;

    public RcConfig GetConfig()
    {
        return new RcConfig(
            useTiles: false,
            tileSizeX: 0,
            tileSizeZ: 0,
            borderSize: 0,
            partition: PartitionType,
            cellSize: CellSize,
            cellHeight: CellHeight,
            agentMaxSlope: AgentMaxSlopeAngle,
            agentHeight: AgentHeight,
            agentRadius: AgentRadius,
            agentMaxClimb: AgentMaxClimb,
            minRegionArea: RegionMinArea,
            mergeRegionArea: RegionMergeArea,
            edgeMaxLen: ContourEdgeMaxLength,
            edgeMaxError: ContourMaxError,
            vertsPerPoly: 9, // note: would we need different per-game vert limits?
            detailSampleDist: DetailSampleDistance,
            detailSampleMaxError: DetailSampleMaxError,
            filterLowHangingObstacles: true,
            filterLedgeSpans: true,
            filterWalkableLowHeightSpans: true,
            walkableAreaMod: new RcAreaModification(1),
            buildMeshDetail: true);
    }
}