using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.DD2;

[RszComponentClass("app.EnvironmentSceneController", nameof(GameIdentifier.dd2))]
public class EnvironmentSceneController(GameObject gameObject, RszInstance data) : Component(gameObject, data)
{
    internal override void OnActivate()
    {
        base.OnActivate();
        Scene?.FindFolder("Environment")?.RequestLoad();
        Scene?.FindFolder("FarEnvironment")?.RequestLoad();
    }
}
