using ContentEditor.App.ImguiHandling;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.FileLoaders;

public class TerrainSceneLoader : TerrFileLoader,
    IFileLoader,
    IFileHandleContentProvider<CommonMeshResource>
{
    int IFileLoader.Priority => 30;

    CommonMeshResource IFileHandleContentProvider<CommonMeshResource>.GetFile(FileHandle handle)
    {
        var terr = handle.GetFile<TerrFile>();
        var scene = McolEditor.GetMeshScene(terr.bvh);
        if (scene == null) {
            scene = new Assimp.Scene();
        }
        return new CommonMeshResource(PathUtils.GetFilepathWithoutExtensionOrVersion(handle.Filename).ToString(), null!) { Scene = scene };
    }
}
