using System.Buffers;
using System.Numerics;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.UVar;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App.DD2;

[RszComponentClass("app.WorldEnvironmentController", nameof(GameIdentifier.dd2))]
public class WorldEnvironmentController(GameObject gameObject, RszInstance data) : UpdateComponent(gameObject, data)
{
    private const float MinX = -4096;
    private const float MinY = -4096;
    private const float MaxX = 4096;
    private const float MaxY = 4096;

    private const int FieldCount = 8;
    private const int EnvCount = 64;
    private const int SubEnvCount = 128;
    private const int CellsPerEnvCount = 16;

    private const float FieldSizeX = (MaxX - MinX) / FieldCount;
    private const float FieldSizeY = (MaxY - MinY) / FieldCount;

    private static readonly Vector3 EnvCellSize = new Vector3((MaxX - MinX) / EnvCount, 0, (MaxY - MinY) / EnvCount);

    public int CurrentEnvID { get; private set; }
    public int CurrentSubEnvID { get; private set; }
    public int CurrentFieldID { get; private set; }

    public float CurrentEnvX { get; private set; }
    public float CurrentEnvY { get; private set; }

    public float CurrentSubEnvX { get; private set; }
    public float CurrentSubEnvY { get; private set; }

    public float CurrentFieldX { get; private set; }
    public float CurrentFieldY { get; private set; }

    public HashSet<int> ActiveFieldIDs { get; } = new();
    public HashSet<int> ActiveEnvIDs { get; } = new();
    public HashSet<int> ActiveSubEnvIDs { get; } = new();

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

        (CurrentFieldX, CurrentFieldY, CurrentFieldID) = CalculateCell(MinX, MinY, MaxY, MaxY, worldPos, FieldCount);
        (CurrentEnvX, CurrentEnvY, CurrentEnvID) = CalculateCell(MinX, MinY, MaxY, MaxY, worldPos, EnvCount);
        (CurrentSubEnvX, CurrentSubEnvY, CurrentSubEnvID) = CalculateCell(MinX, MinY, MaxY, MaxY, worldPos, SubEnvCount);

        ActiveEnvIDs.Clear();
        ActiveEnvIDs.Add(CurrentEnvID);

        ActiveEnvIDs.Add(FlattenToCellID(CurrentEnvX + 1, CurrentEnvY, EnvCount));
        ActiveEnvIDs.Add(FlattenToCellID(CurrentEnvX - 1, CurrentEnvY, EnvCount));
        ActiveEnvIDs.Add(FlattenToCellID(CurrentEnvX, CurrentEnvY + 1, EnvCount));
        ActiveEnvIDs.Add(FlattenToCellID(CurrentEnvX, CurrentEnvY - 1, EnvCount));

        ActiveEnvIDs.Add(FlattenToCellID(CurrentEnvX + 1, CurrentEnvY + 1, EnvCount));
        ActiveEnvIDs.Add(FlattenToCellID(CurrentEnvX - 1, CurrentEnvY + 1, EnvCount));
        ActiveEnvIDs.Add(FlattenToCellID(CurrentEnvX + 1, CurrentEnvY - 1, EnvCount));
        ActiveEnvIDs.Add(FlattenToCellID(CurrentEnvX - 1, CurrentEnvY - 1, EnvCount));

        ActiveFieldIDs.Clear();
        ActiveFieldIDs.Add(CurrentFieldID);

        ActiveFieldIDs.Add(CalculateCellID(MinX, MinY, MaxY, MaxY, worldPos + new Vector3(FieldSizeX,  0, 0), FieldCount));
        ActiveFieldIDs.Add(CalculateCellID(MinX, MinY, MaxY, MaxY, worldPos + new Vector3(-FieldSizeX, 0, 0), FieldCount));
        ActiveFieldIDs.Add(CalculateCellID(MinX, MinY, MaxY, MaxY, worldPos + new Vector3(0, 0,  FieldSizeY), FieldCount));
        ActiveFieldIDs.Add(CalculateCellID(MinX, MinY, MaxY, MaxY, worldPos + new Vector3(0, 0, -FieldSizeY), FieldCount));

        ActiveFieldIDs.Add(CalculateCellID(MinX, MinY, MaxY, MaxY, worldPos + new Vector3(FieldSizeX,  0, FieldSizeY), FieldCount));
        ActiveFieldIDs.Add(CalculateCellID(MinX, MinY, MaxY, MaxY, worldPos + new Vector3(-FieldSizeX, 0, FieldSizeY), FieldCount));
        ActiveFieldIDs.Add(CalculateCellID(MinX, MinY, MaxY, MaxY, worldPos + new Vector3(FieldSizeX,  0, -FieldSizeY), FieldCount));
        ActiveFieldIDs.Add(CalculateCellID(MinX, MinY, MaxY, MaxY, worldPos + new Vector3(-FieldSizeX, 0, -FieldSizeY), FieldCount));

        ActiveSubEnvIDs.Clear();
        ActiveSubEnvIDs.Add(CurrentSubEnvID);

        ActiveSubEnvIDs.Add(FlattenToCellID(CurrentSubEnvX + 1, CurrentSubEnvY, SubEnvCount));
        ActiveSubEnvIDs.Add(FlattenToCellID(CurrentSubEnvX - 1, CurrentSubEnvY, SubEnvCount));
        ActiveSubEnvIDs.Add(FlattenToCellID(CurrentSubEnvX, CurrentSubEnvY + 1, SubEnvCount));
        ActiveSubEnvIDs.Add(FlattenToCellID(CurrentSubEnvX, CurrentSubEnvY - 1, SubEnvCount));

        ActiveSubEnvIDs.Add(FlattenToCellID(CurrentSubEnvX + 1, CurrentSubEnvY + 1, SubEnvCount));
        ActiveSubEnvIDs.Add(FlattenToCellID(CurrentSubEnvX - 1, CurrentSubEnvY + 1, SubEnvCount));
        ActiveSubEnvIDs.Add(FlattenToCellID(CurrentSubEnvX + 1, CurrentSubEnvY - 1, SubEnvCount));
        ActiveSubEnvIDs.Add(FlattenToCellID(CurrentSubEnvX - 1, CurrentSubEnvY - 1, SubEnvCount));

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
