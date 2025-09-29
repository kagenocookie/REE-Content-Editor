using System.Globalization;
using System.Numerics;
using Assimp;
using ContentEditor;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mesh;
using ReeLib.Mot;
using ReeLib.Motlist;
using ReeLib.via;

namespace ContentPatcher;

public partial class AssimpMeshResource(string Name, Workspace workspace) : IResourceFile
{
    private Assimp.Scene? _scene;
    private MeshFile? _mesh;
    private MotlistFile? _motlist;

    public GameName GameVersion = GameName.dd2;

    public Assimp.Scene Scene
    {
        get => _scene ??= ConvertMeshToAssimpScene(NativeMesh, Name);
        set => _scene = value;
    }

    public MeshFile NativeMesh
    {
        get => _mesh ??= (ImportMeshFromAssimp(_scene!, MeshFile.GetGameMeshVersions(GameVersion)[0]));
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
        _mesh?.Meshes.SelectMany(m => m.LODs[0].MeshGroups.Select(g => (int)g.groupId)).Distinct()
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

    public int MeshCount => _mesh?.Meshes.Sum(mm => mm.totalMeshCount)
        ?? _scene?.MeshCount
        ?? -1;

    public void WriteTo(string filepath)
    {
        using AssimpContext importer = new AssimpContext();

        var ext = PathUtils.GetExtensionWithoutPeriod(filepath);
        string? exportFormat = null;
        foreach (var fmt in importer.GetSupportedExportFormats()) {
            if (fmt.FileExtension == ext) {
                exportFormat = fmt.FormatId;
                break;
            }
        }
        if (exportFormat == null) {
            throw new NotImplementedException("Unsupported export format " + ext);
        }

        var scene = Scene;
        importer.ExportFile(scene, filepath, exportFormat);
    }

    public void ExportToFile(string filepath, MotlistFile? motlist)
    {
        using AssimpContext importer = new AssimpContext();

        var ext = PathUtils.GetExtensionWithoutPeriod(filepath);
        string? exportFormat = null;
        foreach (var fmt in importer.GetSupportedExportFormats()) {
            if (fmt.FileExtension == ext) {
                exportFormat = fmt.FormatId;
                break;
            }
        }
        if (exportFormat == null) {
            throw new NotImplementedException("Unsupported export format " + ext);
        }
        if (motlist == null) {
            importer.ExportFile(Scene, filepath, exportFormat);
            return;
        }

        if (_mesh == null) {
            Logger.Error("Missing mesh file, can't export");
            return;
        }

        var scene = ExportWithAnimations(_mesh, motlist);
        importer.ExportFile(scene, filepath, exportFormat);
    }

    public Assimp.Scene ExportWithAnimations(MeshFile mesh, MotlistFile motlist)
    {
        var scene = ConvertMeshToAssimpScene(mesh, Name);
        foreach (var file in motlist.MotFiles) {
            if (file is MotFile mot) {
                AddMotToScene(scene, mot);
            }
        }

        return scene;
    }

    public Assimp.Scene ExportWithAnimations(MeshFile mesh, MotFile mot)
    {
        var scene = ConvertMeshToAssimpScene(mesh, Name);
        AddMotToScene(scene, mot);
        return scene;
    }
}
