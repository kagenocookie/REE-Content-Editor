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
        return (comp.Classname.Contains(filter, StringComparison.OrdinalIgnoreCase));
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

        var cls = ws.Env.RszParser.GetRSZClass(key);
        if (cls == null) {
            Logger.Error("Invalid classname " + key);
            return null;
        }
        var gameobj = context.FindValueInParentValues<GameObject>();
        if (Logger.ErrorIf(gameobj == null, "Missing game object")) return null;
        if (gameobj.Components.Any(c => c.Classname == key)) {
            Logger.Error("GameObject already has component " + key);
            return null;
        }

        return new Component(gameobj, RszInstance.CreateInstance(ws.Env.RszParser, cls));
    }

    protected override string GetKey(Component item)
    {
        return item.Classname;
    }
}
