using System.Buffers;
using System.Numerics;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.UVar;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App.DD2;

public record class GridSplitter(Vector2 min, Vector2 max, int countPerLine, int activeSurroundingCellRadius)
{
    public HashSet<int> ActiveIDs { get; } = new();
    public int CurrentX { get; private set; }
    public int CurrentY { get; private set; }
    public int CurrentID { get; private set; }

    public Vector2 CellSize => (max - min) / countPerLine;

    public void Update(Vector3 origin)
    {
        ActiveIDs.Clear();
        (CurrentX, CurrentY, CurrentID) = CalculateCell(origin);
        ActiveIDs.Add(CurrentID);

        if (activeSurroundingCellRadius == 0) return;

        var endX = (int)Math.Min(countPerLine - 1, CurrentX + activeSurroundingCellRadius);
        var endY = (int)Math.Min(countPerLine - 1, CurrentY + activeSurroundingCellRadius);
        for (int x = (int)Math.Max(CurrentX - activeSurroundingCellRadius, 0); x <= endX; ++x) {
            for (int y = (int)Math.Max(CurrentY - activeSurroundingCellRadius, 0); y <= endY; ++y) {
                ActiveIDs.Add(x + y * countPerLine);
            }
        }
    }

    public (int x, int y, int id) CalculateCell(Vector3 point)
    {
        var x = (int)MathF.Floor((point.X - min.X) / (max.X - min.X) * countPerLine);
        var y = (int)MathF.Floor((point.Z - min.Y) / (max.Y - min.Y) * countPerLine);
        return (x, y, x + y * countPerLine);
    }

    public int CalculateCellID(Vector3 point)
    {
        return CalculateCell(point).id;
    }

    public (int x, int z) GetXZ(int id)
    {
        var x = id % countPerLine;
        var z = (id / countPerLine);
        return (x, z);
    }

    public Vector3 GetPosition(int id)
    {
        var x = id % countPerLine;
        var z = (id / countPerLine);

        return new Vector3(
            min.X + (max.X - min.X) / countPerLine * x,
            0,
            min.Y + (max.Y - min.Y) / countPerLine * z
        );
    }

    public static int FlattenToCellID(float x, float y, int cellCount)
    {
        var xx = (int)Math.Floor(Math.Clamp(x, 0, cellCount));
        var yy = (int)Math.Floor(Math.Clamp(y, 0, cellCount));
        return xx + yy * cellCount;
    }
}

[RszComponentClass("app.WorldEnvironmentController", nameof(GameIdentifier.dd2))]
public class WorldEnvironmentController(GameObject gameObject, RszInstance data) : UpdateComponent(gameObject, data)
{
    private const float MinX = -4096;
    private const float MinY = -4096;
    private const float MaxX = 4096;
    private const float MaxY = 4096;

    private const int FieldCount = 8;
    private const int GroundFieldCount = 16;
    private const int EnvCount = 64;
    private const int SubEnvCount = 128;
    private const int CellsPerEnvCount = 16;

    public GridSplitter Fields = new GridSplitter(new Vector2(MinX, MinY), new Vector2(MaxX, MaxY), FieldCount, 0);
    public GridSplitter GroundFields = new GridSplitter(new Vector2(MinX, MinY), new Vector2(MaxX, MaxY), GroundFieldCount, 1);
    public GridSplitter Env = new GridSplitter(new Vector2(MinX, MinY), new Vector2(MaxX, MaxY), EnvCount, 1);
    public GridSplitter SubEnv = new GridSplitter(new Vector2(MinX, MinY), new Vector2(MaxX, MaxY), SubEnvCount, 1);

    private const float FieldSizeX = (MaxX - MinX) / FieldCount;
    private const float FieldSizeY = (MaxY - MinY) / FieldCount;

    private static readonly Vector3 EnvCellSize = new Vector3((MaxX - MinX) / EnvCount, 0, (MaxY - MinY) / EnvCount);

    public int CurrentEnvID { get; private set; }
    public int CurrentSubEnvID { get; private set; }
    public int CurrentFieldID { get; private set; }

    public HashSet<int> ActiveFieldIDs => Fields.ActiveIDs;
    public HashSet<int> ActiveEnvIDs => Env.ActiveIDs;
    public HashSet<int> ActiveSubEnvIDs => SubEnv.ActiveIDs;

    public static WorldEnvironmentController? Instance { get; private set; }

    private Dictionary<int, Folder>? fieldFolders;
    private Dictionary<int, Folder> envFolders = new();

    internal override void OnActivate()
    {
        base.OnActivate();
        Instance = this;
    }

    internal override void OnDeactivate()
    {
        if (Instance == this) Instance = null;
        base.OnDeactivate();
    }

    private static (int x, int y, int id) CalculateCell(float minX, float minY, float maxX, float maxY, Vector3 point, int cellCount)
    {
        var x = (int)MathF.Floor((point.X - minX) / (maxX - minX) * cellCount);
        var y = (int)MathF.Floor((point.Z - minY) / (maxY - minY) * cellCount);
        return (x, y, x + y * cellCount);
    }

    private static int CalculateCellID(float minX, float minY, float maxX, float maxY, Vector3 point, int cellCount)
    {
        return CalculateCell(minX, minY, maxX, maxY, point, cellCount).id;
    }

    private static int FlattenToCellID(float x, float y, int cellCount)
    {
        var xx = (int)Math.Floor(Math.Clamp(x, 0, cellCount));
        var yy = (int)Math.Floor(Math.Clamp(y, 0, cellCount));
        return xx + yy * cellCount;
    }

    public override void Update(float deltaTime)
    {
        if (Scene == null) return;
        // TODO add enable-world-control setting?
        var worldPos = Scene.ActiveCamera.Transform.Position;

        Fields.Update(worldPos);
        GroundFields.Update(worldPos);
        Env.Update(worldPos);
        SubEnv.Update(worldPos);

        Fields.ActiveIDs.Add(Fields.CalculateCellID(worldPos + new Vector3(FieldSizeX,  0, 0)));
        Fields.ActiveIDs.Add(Fields.CalculateCellID(worldPos + new Vector3(-FieldSizeX, 0, 0)));
        Fields.ActiveIDs.Add(Fields.CalculateCellID(worldPos + new Vector3(0, 0,  FieldSizeY)));
        Fields.ActiveIDs.Add(Fields.CalculateCellID(worldPos + new Vector3(0, 0, -FieldSizeY)));

        Fields.ActiveIDs.Add(Fields.CalculateCellID(worldPos + new Vector3(FieldSizeX,  0, FieldSizeY)));
        Fields.ActiveIDs.Add(Fields.CalculateCellID(worldPos + new Vector3(-FieldSizeX, 0, FieldSizeY)));
        Fields.ActiveIDs.Add(Fields.CalculateCellID(worldPos + new Vector3(FieldSizeX,  0, -FieldSizeY)));
        Fields.ActiveIDs.Add(Fields.CalculateCellID(worldPos + new Vector3(-FieldSizeX, 0, -FieldSizeY)));

        var envRoot = Scene.GetChildScene("Env");
        if (envRoot == null) {
            Scene.FindFolder("Env")?.RequestLoad();
        } else {
            UpdateFields(envRoot);
        }
    }

    private void UpdateFields(Scene fieldRoot)
    {
        if (fieldFolders == null) {
            fieldFolders = new Dictionary<int, Folder>();
            foreach (var folder in fieldRoot.Folders) {
                if (int.TryParse(folder.Name.Replace("FieldArea", ""), out var fieldId)) {
                    fieldFolders[fieldId] = folder;
                }
            }
        }

        foreach (var (id, folder) in fieldFolders) {
            if (ActiveFieldIDs.Contains(id)) {
                if (folder.ChildScene == null) {
                    folder.RequestLoad();
                    continue;
                }

                folder.ChildScene.SetActive(true);
                UpdateEnv(folder.ChildScene);
            } else {
                folder.ChildScene?.SetActive(false);
            }
        }
    }

    private void UpdateEnv(Scene envContainer)
    {
        if (envContainer.ChildScenes.Count == 0) {
            foreach (var folder in envContainer.Folders) {
                if (int.TryParse(folder.Name.Replace("Env_", ""), out var envId)) {
                    envFolders[envId] = folder;
                }
            }
        }

        foreach (var (id, folder) in envFolders) {
            if (ActiveEnvIDs.Contains(id)) {
                if (folder.ChildScene == null) {
                    folder.RequestLoad();
                    continue;
                }

                folder.ChildScene.SetActive(true);
            } else {
                folder.ChildScene?.SetActive(false);
            }
        }
    }
}
