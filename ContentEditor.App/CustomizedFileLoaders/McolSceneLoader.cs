using System.Text.Json;
using System.Text.Json.Nodes;
using ContentEditor.App;
using ContentEditor.App.ImguiHandling;
using ReeLib;
using ReeLib.Scn;

namespace ContentPatcher;

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
        // TODO also handle other non-triangle shapes
        return new AssimpMeshResource(scene);
    }
}
