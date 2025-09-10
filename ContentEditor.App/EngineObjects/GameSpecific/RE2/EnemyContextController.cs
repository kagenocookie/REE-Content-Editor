using System.Buffers;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.Gui;
using ReeLib.UVar;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

[RszComponentClass("app.ropeway.EnemyContextController", nameof(GameIdentifier.re2), nameof(GameIdentifier.re2rt))]
[RszComponentClass("offline.EnemyContextController", nameof(GameIdentifier.re3), nameof(GameIdentifier.re3rt))]
public class EnemyContextController(GameObject gameObject, RszInstance data) : BaseSingleMeshComponent(gameObject, data)
{
    private int lastEnemyId = -1;

    protected override void RefreshMesh()
    {
        var enemyId = RszFieldCache.RE2.EnemyContextController.InitialKind.Get(Data);
        if (enemyId >= 0) {
            SetEnemyID(enemyId);
        } else {
            UnloadMesh();
            lastEnemyId = -1;
        }
    }

    private void SetEnemyID(int enemyId)
    {
        var desc = Scene!.Workspace.Env.TypeCache.GetEnumDescriptor(RszFieldCache.RE2.EnemyContextController.InitialKind.GetField(Data.RszClass)!.original_type);
        var label = desc.GetLabel(enemyId);
        if (!TryLoadMesh($"SectionRoot/Character/Enemy/{label}/Body/Body00/{label}_body00.mesh")) {
            if (!TryLoadMesh($"SectionRoot/Character/Enemy/{label}/{label}/{label}.mesh")) {
                UnloadMesh();
            }
        }

        lastEnemyId = enemyId;
    }

    protected override bool IsMeshUpToDate() => lastEnemyId == RszFieldCache.RE2.EnemyContextController.InitialKind.Get(Data);

    private bool TryLoadMesh(string meshPath)
    {
        if (Scene!.Workspace.ResourceManager.TryResolveFile(meshPath, out _)) {
            var mdfPath = Path.ChangeExtension(meshPath, "mdf2");
            SetMesh(meshPath, mdfPath);
            return true;
        }
        return false;
    }
}
