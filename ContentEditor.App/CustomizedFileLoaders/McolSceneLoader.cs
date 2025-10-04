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
    IFileHandleContentProvider<AssimpMeshResource>
{
    int IFileLoader.Priority => 30;

    AssimpMeshResource IFileHandleContentProvider<AssimpMeshResource>.GetFile(FileHandle handle)
    {
        var mcol = handle.GetFile<McolFile>();
        var scene = McolEditor.GetMeshScene(mcol);
        if (scene == null) {
            scene = new Assimp.Scene();
        }
        return new AssimpMeshResource(PathUtils.GetFilepathWithoutExtensionOrVersion(handle.Filename).ToString(), null!) { Scene = scene };
    }
}
