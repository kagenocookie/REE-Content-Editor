using System.Text;
using System.Text.Json;
using Lua;

namespace ContentEditor.App.Lua;

[LuaObject]
public partial class LuaLog
{
    private StringBuilder sb = new StringBuilder();

    public LuaFunction LogFunction { get; }

    public LuaLog(JsonSerializerOptions options)
    {
        LogFunction = new LuaFunction("info", (context, ct) => {
            sb.Clear();
            var i = 0;
            foreach (var arg in context.Arguments) {
                if (i++ != 0) {
                    sb.Append('\t');
                }
                sb.Append(LuaJson.LuaToString(arg, options));
            }
            Logger.Info(sb.ToString());
            sb.Clear();
            return new ValueTask<int>(context.Return(LuaValue.Nil));
        });
    }
}