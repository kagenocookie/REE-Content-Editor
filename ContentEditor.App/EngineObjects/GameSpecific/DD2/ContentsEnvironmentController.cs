using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.DD2;

[RszComponentClass("app.ContentsEnvironmentController", nameof(GameIdentifier.dd2))]
public class ContentsEnvironmentController(GameObject gameObject, RszInstance data) : UpdateComponent(gameObject, data)
{
    private Dictionary<int, Folder> envFolders = new();

    internal override void OnActivate()
    {
        base.OnActivate();

        foreach (var folder in Scene!.Folders) {
            if (int.TryParse(folder.Name.Replace("Env_", ""), out var envId)) {
                envFolders[envId] = folder;
            }
        }
        Scene.FindFolder("Environment")?.RequestLoad();
        Scene.FindFolder("FarEnvironment")?.RequestLoad();
    }

    public override void Update(float deltaTime)
    {
        var activeEnvs = WorldEnvironmentController.Instance?.ActiveEnvIDs;
        if (activeEnvs == null) return;

        foreach (var (id, folder) in envFolders) {
            if (activeEnvs.Contains(id)) {
                if (folder.ChildScene == null) {
                    folder.RequestLoad();
                    continue;
                }

                folder.ChildScene.SetActive(true);
            } else {
                folder.ChildScene?.SetActive(false);
            }
        }
    }
}
