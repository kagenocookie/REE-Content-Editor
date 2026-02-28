using Assimp;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.FileLoaders;

public partial class CommonMeshResource(string Name, Workspace workspace) : IResourceFile
{
    private Assimp.Scene? _scene;
    private MeshFile? _mesh;
    private MotlistFile? _motlist;

    public List<Graphics.Mesh>? PreloadedMeshes { get; set; }
    public List<Graphics.Mesh>? OcclusionMeshes { get; set; }

    public GameName GameVersion = GameName.dd2;

    public Assimp.Scene Scene
    {
        get => _scene ??= AddMeshToScene(new Assimp.Scene(), NativeMesh, Name, false, false, false, false, null);
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

    public List<Assimp.Material>? MaterialList
    {
        get {
            if (_scene != null) return _scene.Materials;
            if (_mesh != null) {
                return _mesh.MaterialNames.Select(name => new Assimp.Material() { Name = name }).ToList();
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

    public int PolyCount => _mesh?.MeshBuffer?.Faces?.Length
        ?? _mesh?.MeshBuffer?.IntegerFaces?.Length
        ?? _scene?.Meshes.Sum(m => m.FaceCount)
        ?? -1;

    public int MaterialCount => _mesh?.MaterialNames.Count
        ?? _scene?.MaterialCount
        ?? -1;

    public int BoneCount => _mesh?.BoneData?.Bones.Count
        ?? _scene?.Meshes.FirstOrDefault()?.BoneCount
        ?? -1;

    public int MeshCount => _mesh?.MeshData?.totalMeshCount
        ?? _scene?.MeshCount
        ?? -1;

    public void WriteTo(string filepath)
    {
        NativeMesh.WriteTo(filepath);
    }

    public void ExportToFile(
        string filepath,
        bool includeLodsShadows,
        bool includeOcc,
        FbxSkelFile? skeleton = null,
        IEnumerable<MotFileBase>? mots = null,
        IEnumerable<CommonMeshResource>? additionalMeshes = null
    )
    {
        using AssimpContext context = new AssimpContext();

        var ext = PathUtils.GetExtensionWithoutPeriod(filepath);
        string exportFormat = context.GetFormatIDFromExtension(ext);
        var scene = AddMeshToScene(new Assimp.Scene(), ext, false, includeLodsShadows, includeOcc, skeleton);

        if (_mesh == null) {
            Logger.Error("Missing mesh file, can't export");
            return;
        }

        if (mots != null) {
            foreach (var mot in mots) {
                if (mot is MotFile mm) {
                    AddMotToScene(scene, mm, ext);
                }
            }
        }
        if (additionalMeshes?.Any() == true) {
            foreach (var addm in additionalMeshes) {
                addm.AddMeshToScene(scene, ext, false, includeLodsShadows, includeOcc, skeleton);
            }
        }
        void PrintTree(Node node, int depth)
        {
            Logger.Info(new string(' ', depth * 2) + node.Name);
            foreach (var child in node.Children) {
                PrintTree(child, depth + 1);
            }
        }
        // PrintTree(scene.RootNode, 0);
        context.ExportFile(scene, filepath, exportFormat);
    }

    internal void PreloadMeshBuffers()
    {
        if (PreloadedMeshes != null || OcclusionMeshes != null) return;

        var mesh = NativeMesh;
        if (mesh.MeshData?.LODs.Count > 0) {
            PreloadedMeshes = new();
            var mainLod = mesh.MeshData!.LODs[0];
            foreach (var group in mainLod.MeshGroups) {
                foreach (var sub in group.Submeshes) {
                    var newMesh = new TriangleMesh(mesh, sub);
                    newMesh.MeshGroup = group.groupId;
                    PreloadedMeshes.Add(newMesh);
                }
            }
        }

        if (mesh.OccluderMesh?.MeshGroups.Count > 0) {
            OcclusionMeshes = new();
            foreach (var group in mesh.OccluderMesh.MeshGroups) {
                foreach (var sub in group.Submeshes) {
                    var newMesh = new TriangleMesh(mesh, sub, mesh.OccluderMesh.TargetBuffer);
                    newMesh.MeshGroup = group.groupId;
                    OcclusionMeshes.Add(newMesh);
                }
            }
        }

    }

    private Assimp.Scene AddMeshToScene(Assimp.Scene scene, string targetFileExtension, bool allowCache, bool includeLodsShadows, bool includeOcc, FbxSkelFile? skeleton = null)
    {
        var isGltf = targetFileExtension == "glb" || targetFileExtension == "gltf";
        if (allowCache && _scene != null && !isGltf) return _scene;

        AddMeshToScene(scene, NativeMesh, Name, isGltf, includeLodsShadows, includeLodsShadows, includeOcc, skeleton);
        if (allowCache) _scene = scene;
        return scene;
    }
}
