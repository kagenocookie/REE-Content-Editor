using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.DD2;

[RszComponentClass("app.ContentsEnvironmentController", nameof(GameIdentifier.dd2))]
public class ContentsEnvironmentController(GameObject gameObject, RszInstance data) : UpdateComponent(gameObject, data)
{
    private Dictionary<int, Folder> envFolders = new();

    private Folder? envFolder;
    internal override void OnActivate()
    {
        base.OnActivate();

        envFolder = Scene!.FindFolder("Environment");
        envFolder?.RequestLoad();
        Scene.FindFolder("FarEnvironment")?.RequestLoad();
    }

    public override void Update(float deltaTime)
    {
        var wec = WorldEnvironmentController.Instance;
        if (wec == null) return;

        if (envFolder?.ChildScene == null) return;

        if (envFolders.Count == 0) {
            foreach (var folder in envFolder.ChildScene.Folders) {
                if (int.TryParse(folder.Name.Replace("Env_", ""), out var envId)) {
                    envFolders[envId] = folder;
                }
            }
        }

        foreach (var (id, folder) in envFolders) {
            if (wec.ActiveSubEnvIDs.Contains(id)) {
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
