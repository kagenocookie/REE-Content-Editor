using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.DD2;

[RszComponentClass("app.ContentsEnvController", nameof(GameIdentifier.dd2))]
public class ContentsEnvController(GameObject gameObject, RszInstance data) : Component(gameObject, data)
{
    internal override void OnActivate()
    {
        base.OnActivate();
        Scene?.FindFolder("Environment")?.RequestLoad();
    }
}
