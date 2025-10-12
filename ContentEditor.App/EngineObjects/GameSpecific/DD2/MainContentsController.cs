using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.DD2;

[RszComponentClass("app.MainContentsController", nameof(GameIdentifier.dd2))]
public class MainContentsController(GameObject gameObject, RszInstance data) : Component(gameObject, data)
{
    internal override void OnActivate()
    {
        base.OnActivate();
        Scene?.FindFolder("Ground")?.RequestLoad();
    }
}
