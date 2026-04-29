using System.Diagnostics;
using System.Runtime.CompilerServices;
using Assimp;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.FileLoaders;

public partial class CommonMeshResource(string name, Workspace workspace) : IResourceFile
{
    private Assimp.Scene? _scene;
    private MeshFile? _mesh;
    private MotlistFile? _motlist;

    public List<Graphics.Mesh>? PreloadedMeshes { get; set; }
    public List<Graphics.Mesh>? OcclusionMeshes { get; set; }

    public MaterialGroupWrapper? ImportedMaterials { get; private set; }

    public GameName GameVersion = GameName.dd2;

    public string Name => name;

    public Assimp.Scene Scene
    {
        get => _scene ??= AddMeshToScene(new ExportContext(), NativeMesh, Name).scene;
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

    public IReadOnlyList<string> MaterialNames => NativeMesh.MaterialNames;

    public bool HasAnimations => _scene?.HasAnimations == true;

    public IEnumerable<int> GroupIDs =>
        NativeMesh.MeshData?.LODs[0].MeshGroups.Select(g => (int)g.groupId).Distinct()
        ?? [];

    public int VertexCount => NativeMesh.MeshBuffer?.Positions.Length
        ?? -1;

    public int PolyCount => NativeMesh.TotalTriangleCount;

    public int MaterialCount => NativeMesh.MaterialNames.Count;

    public int BoneCount => NativeMesh.BoneData?.Bones.Count
        ?? -1;

    public int MeshCount => NativeMesh.MeshData?.totalMeshCount
        ?? -1;

    public void WriteTo(string filepath)
    {
        NativeMesh.WriteTo(filepath);
    }

    public void ImportMaterials(Assimp.Scene scene)
    {
        if (!workspace.TryGetFileExtensionVersion("mdf2", out int version)) {
            version = 51;
        }
        var mdf = new MdfFile(new FileHandler(new MemoryStream(), $"{name}.mdf2.{version}"));
        foreach (var mat in scene.Materials) {
            if (!mat.HasName) {
                continue;
            }

            var matData = new ReeLib.Mdf.MaterialData(new ReeLib.Mdf.MaterialHeader() {
                matName = mat.Name,
                mmtrPath = "__imported.mmtr",
            });
            if (mat.HasTextureDiffuse) {
                var texPath = LoadAssimpTexture(scene, mat.TextureDiffuse);
                if (!string.IsNullOrEmpty(texPath)) {
                    matData.Textures.Add(new ReeLib.Mdf.TexHeader(MaterialGroupWrapper.AlbedoTextureNames.First(), texPath));
                }
            }
            mdf.Materials.Add(matData);
        }

        ImportedMaterials = new MaterialGroupWrapper(mdf);
        ImportedMaterials.UpdateMaterialLookups();
    }

    private string? LoadAssimpTexture(Assimp.Scene scene, TextureSlot texture)
    {
        Debug.Assert(MainLoop.IsMainThread);

        if (!texture.FilePath.StartsWith('*')) {
            return texture.FilePath;
        }

        var texData = scene.GetEmbeddedTexture(texture.FilePath);
        if (texData == null) return null;

        var tex = new Texture();
        if (texData.HasCompressedData) {
            var stream = new MemoryStream(texData.CompressedData);
            tex.LoadFromStream(stream, false);
        } else {
            var bytes = Unsafe.As<byte[]>(texData.NonCompressedData);
            tex.LoadFromRawData(bytes, (uint)texData.Width, (uint)texData.Height);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".dds");
        tex.SaveAs(tempPath);
        Logger.Info($"Saved embedded texture {texture.FilePath} to {tempPath}");
        return tempPath;
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
        var exportCtx = new ExportContext() {
            includeAllLods = includeLodsShadows,
            includeShadows = includeLodsShadows,
            format = exportFormat,
            includeShapeKeys = exportFormat == "glb",
            includeOcclusion = includeOcc,
            skeleton = skeleton,
        };
        exportCtx.scene.RootNode ??= new Node(Name);

        PrepareSkeleton(exportCtx, NativeMesh);
        foreach (var add in additionalMeshes ?? []) {
            PrepareSkeleton(exportCtx, add.NativeMesh);
        }

        var scene = AddMeshToScene(exportCtx, NativeMesh, Name);

        if (_mesh == null) {
            Logger.Error("Missing mesh file, can't export");
            return;
        }

        if (additionalMeshes?.Any() == true) {
            foreach (var addm in additionalMeshes) {
                AddMeshToScene(exportCtx, addm.NativeMesh, addm.Name);
            }
        }
        if (mots != null) {
            foreach (var mot in mots) {
                if (mot is MotFile mm) {
                    AddMotToScene(exportCtx.scene, mm, ext);
                }
            }
        }
        void PrintTree(Node node, int depth)
        {
            Logger.Info(new string(' ', depth * 2) + node.Name);
            foreach (var child in node.Children) {
                PrintTree(child, depth + 1);
            }
        }
        // PrintTree(exportCtx.scene.RootNode, 0);
        context.ExportFile(exportCtx.scene, filepath, exportFormat);
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
}
