using ContentEditor.App.ImguiHandling;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.FileLoaders;

public class TerrainSceneLoader : TerrFileLoader,
    IFileLoader,
    IFileHandleContentProvider<AssimpMeshResource>
{
    int IFileLoader.Priority => 30;

    AssimpMeshResource IFileHandleContentProvider<AssimpMeshResource>.GetFile(FileHandle handle)
    {
        var terr = handle.GetFile<TerrFile>();
        var scene = McolEditor.GetMeshScene(terr.bvh);
        if (scene == null) {
            scene = new Assimp.Scene();
        }
        return new AssimpMeshResource(PathUtils.GetFilepathWithoutExtensionOrVersion(handle.Filename).ToString(), null!) { Scene = scene };
    }
}
