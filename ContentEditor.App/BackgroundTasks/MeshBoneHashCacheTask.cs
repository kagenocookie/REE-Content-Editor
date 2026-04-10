using System.Text.Json;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mesh;

namespace ContentEditor.BackgroundTasks;

public class MeshBoneHashCacheTask(Workspace workspace) : FileCacheTaskBase(workspace)
{
    protected override string GetCacheFilePath(GameIdentifier game) => GetCachePath(game);
    public static string GetCachePath(GameIdentifier game) => Path.Combine(GetBaseCacheDir(game.name), "bone_hashes.json");

    public override string ToString() => $"Caching mesh bone name hashes";

    private Dictionary<uint, string> data = new();

    protected override string FilterPattern => ".*\\.mesh\\..*";

    protected override string FileExtension => "mesh";

    protected override void HandleFile(string path, Stream stream)
    {
        var fileHandler = new FileHandler(stream, path);
        uint magic = fileHandler.Read<uint>(0);
        MeshBoneHierarchy? boneData = null;
        // use raw files instead of mesh loader to make it go faster by skipping streaming data and mply-mesh conversion
        if (magic == MeshFile.Magic) {
            var mesh = new MeshFile(new FileHandler(stream, path));
            if (!mesh.Read() || mesh.BoneData == null) return;

            boneData = mesh.BoneData;
        } else if (magic == MplyMeshFile.Magic) {
            var mesh = new MplyMeshFile(new FileHandler(stream, path));
            if (!mesh.Read() || mesh.BoneData == null) return;

            boneData = mesh.BoneData;
        } else {
            // likely streaming file
            return;
        }

        if (boneData == null) return;
        foreach (var bone in boneData.Bones) {
            data.TryAdd(MurMur3HashUtils.GetHash(bone.name), bone.name);
        }
    }

    protected override void Serialize(string outputFilepath)
    {
        using var fs = File.Create(outputFilepath);
        JsonSerializer.Serialize(fs, data);
        Logger.Info("Stored mesh bone name hashes cache in " + outputFilepath);
    }
}
