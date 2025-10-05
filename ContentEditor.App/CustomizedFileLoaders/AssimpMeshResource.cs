using Assimp;
using Assimp.Configs;
using Assimp.Unmanaged;
using ContentEditor;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.FileLoaders;

public partial class AssimpMeshResource(string Name, Workspace workspace) : IResourceFile
{
    private Assimp.Scene? _scene;
    private MeshFile? _mesh;
    private MotlistFile? _motlist;

    public GameName GameVersion = GameName.dd2;

    public Assimp.Scene Scene
    {
        get => _scene ??= ConvertMeshToAssimpScene(NativeMesh, Name, false);
        set => _scene = value;
    }

    public MeshFile NativeMesh
    {
        get => _mesh ??= (ImportMeshFromAssimp(_scene!, MeshFile.GetGameVersionConfigs(GameVersion)[0]));
        set => _mesh = value;
    }

    public MotlistFile Motlist
    {
        get => _motlist ??= (ImportAnimationsFromAssimp(_scene!, GameVersion));
        set => _motlist = value;
    }

    public List<Material>? MaterialList
    {
        get {
            if (_scene != null) return _scene.Materials;
            if (_mesh != null) {
                return _mesh.MaterialNames.Select(name => new Material() { Name = name }).ToList();
            }
            return null;
        }
    }

    public bool HasNativeMesh => _mesh != null;
    public bool HasAssimpScene => _scene != null;
    public bool HasAnimations => _scene?.HasAnimations == true;

    public IEnumerable<int> GroupIDs =>
        _mesh?.MeshData?.LODs[0].MeshGroups.Select(g => (int)g.groupId).Distinct()
        ?? _scene?.Meshes.Select(m => string.IsNullOrEmpty(m.Name) ? 0 : MeshLoader.GetMeshGroupFromName(m.Name)).Distinct()
        ?? [];

    public int VertexCount => _mesh?.MeshBuffer?.Positions.Length
        ?? _scene?.Meshes.Sum(m => m.VertexCount)
        ?? -1;

    public int PolyCount => _mesh?.MeshBuffer?.Faces.Length
        ?? _scene?.Meshes.Sum(m => m.FaceCount)
        ?? -1;

    public int MaterialCount => _mesh?.MaterialNames.Count
        ?? _scene?.MaterialCount
        ?? -1;

    public int BoneCount => _mesh?.BoneData?.Bones.Count
        ?? _scene?.Meshes[0].BoneCount
        ?? -1;

    public int MeshCount => _mesh?.MeshData?.totalMeshCount
        ?? _scene?.MeshCount
        ?? -1;

    public void WriteTo(string filepath)
    {
        using AssimpContext context = new AssimpContext();

        var ext = PathUtils.GetExtensionWithoutPeriod(filepath);
        string? exportFormat = null;
        foreach (var fmt in context.GetSupportedExportFormats()) {
            if (fmt.FileExtension == ext) {
                exportFormat = fmt.FormatId;
                break;
            }
        }
        if (exportFormat == null) {
            throw new NotImplementedException("Unsupported export format " + ext);
        }

        var scene = GetSceneForExport(ext, true);
        context.ExportFile(scene, filepath, exportFormat);
    }

    public void ExportToFile(string filepath, MotlistFile? motlist = null, MotFile? singleMot = null)
    {
        using AssimpContext context = new AssimpContext();

        var ext = PathUtils.GetExtensionWithoutPeriod(filepath);
        string? exportFormat = null;
        foreach (var fmt in context.GetSupportedExportFormats()) {
            if (fmt.FileExtension == ext) {
                exportFormat = fmt.FormatId;
                break;
            }
        }
        if (exportFormat == null) {
            throw new NotImplementedException("Unsupported export format " + ext);
        }
        var scene = GetSceneForExport(ext, false);
        if (motlist == null && singleMot == null) {
            context.ExportFile(scene, filepath, exportFormat);
            return;
        }

        if (_mesh == null) {
            Logger.Error("Missing mesh file, can't export");
            return;
        }

        if (singleMot != null) AddMotToScene(scene, singleMot);
        if (motlist != null) AddMotlistToScene(scene, motlist);
        context.ExportFile(scene, filepath, exportFormat);
    }

    private Assimp.Scene GetSceneForExport(string targetFileExtension, bool allowCache)
    {
        var isGltf = targetFileExtension == "glb" || targetFileExtension == "gltf";
        if (allowCache && _scene != null && !isGltf) return _scene;

        Assimp.Scene scene = ConvertMeshToAssimpScene(NativeMesh, Name, isGltf);
        if (allowCache) _scene = scene;
        return scene;
    }
}
