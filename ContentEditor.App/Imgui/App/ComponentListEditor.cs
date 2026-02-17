using System.Diagnostics;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.ImguiHandling;

public class ComponentListEditor : DictionaryListImguiHandler<string, Component, List<Component>>
{
    public ComponentListEditor()
    {
        FlatList = true;
        Filterable = true;
    }

    protected override bool Filter(UIContext context, string filter)
    {
        var comp = context.Get<Component>();
        return (comp.Classname.Contains(filter.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
    }

    protected override IObjectUIHandler CreateNewItemInput(UIContext context)
    {
        var ws = context.GetWorkspace();
        if (ws == null) return new StringFieldHandler();
        return new AutocompleteStringHandler(true, ws.Env.TypeCache.GetSubclasses("via.Component").ToArray()) { NoUndoRedo = true };
    }

    protected override Component? CreateItem(UIContext context, string key)
    {
        if (string.IsNullOrEmpty(key)) {
            Logger.Error("Select a valid classname");
            return null;
        }
        var ws = context.GetWorkspace();
        if (Logger.ErrorIf(ws == null, "Missing workspace context")) return null;

        var gameobj = context.FindValueInParentValues<GameObject>();
        if (Logger.ErrorIf(gameobj == null, "Missing game object")) return null;
        if (gameobj.Components.Any(c => c.Classname == key)) {
            Logger.Error("GameObject already has component " + key);
            return null;
        }

        Component? component = null;
        UndoRedo.RecordCallback(context, () => {
            if (component == null) {
                component = Component.Create(gameobj, ws.Env, key);
            } else {
                gameobj.AddComponent(component);
            }
        }, () => {
            Debug.Assert(component != null);
            gameobj.RemoveComponent(component);
        });
        UndoRedo.AttachCallbackToLastAction(UndoRedo.CallbackType.Both, () => context.ClearChildren());
        return null;
    }

    protected override string GetKey(Component item)
    {
        if (!ComponentKeyCache.TryGetValue(item.Data.RszClass, out var cc)) {
            ComponentKeyCache[item.Data.RszClass] = cc = $"{item.Data.RszClass.ShortName.PrettyPrint()}##{item.Classname}";
        }
        return cc;
    }

    private static readonly Dictionary<RszClass, string> ComponentKeyCache = new();
}
