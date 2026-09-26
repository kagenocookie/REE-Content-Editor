using ContentPatcher;
using Lua;
using ReeLib;

namespace ContentEditor.App.Lua;

[LuaObject]
public partial class LuaTypeDatabase(Workspace workspace)
{
    [LuaMember("find_class")]
    public LuaReflectionObject? FindClass(string classname)
    {
        var cls = workspace.RszParser.GetRSZClass(classname);
        if (cls == null) {
            return null;
        }

        return new LuaReflectionObject(cls);
    }

    [LuaMember("get_subclasses")]
    public LuaTable GetSubClasses(string classname)
    {
        return LuaWrapper.ToLuaTable(workspace.TypeCache.GetSubclasses(classname).ToArray());
    }

    [LuaMember("create")]
    public LuaRszInstance? CreateInstance(string classname)
    {
        var cls = workspace.RszParser.GetRSZClass(classname);
        if (cls == null) {
            return null;
        }

        var instance = workspace.CreateRszInstance(cls);
        return new LuaRszInstance(instance);
    }
}
