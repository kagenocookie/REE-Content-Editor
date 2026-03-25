using System.Text.Json;
using System.Text.Json.Nodes;
using ContentEditor.App;
using ContentEditor.App.ImguiHandling;
using ContentPatcher;
using ReeLib;
using ReeLib.Scn;

namespace ContentEditor.App.FileLoaders;

public class McolSceneLoader : McolFileLoader,
    IFileLoader,
    IFileHandleContentProvider<CommonMeshResource>
{
    int IFileLoader.Priority => 30;

    CommonMeshResource IFileHandleContentProvider<CommonMeshResource>.GetFile(FileHandle handle)
    {
        var mcol = handle.GetFile<McolFile>();
        var scene = McolEditor.GetMeshScene(mcol.bvh);
        scene ??= new Assimp.Scene();
        scene.RootNode ??= new Assimp.Node(handle.Filename.ToString());
        return new CommonMeshResource(PathUtils.GetFilepathWithoutExtensionOrVersion(handle.Filename).ToString(), null!) { Scene = scene };
    }
}
