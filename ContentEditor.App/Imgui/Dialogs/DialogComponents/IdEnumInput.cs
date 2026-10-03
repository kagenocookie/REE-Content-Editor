using System.Text.Json;
using System.Text.Json.Nodes;
using ContentEditor.App.ImguiHandling;
using ContentPatcher;
using ReeLib.Il2cpp;

namespace ContentEditor.App;

[DialogInputExtension("id-enum-input")]
public class IdEnumInput : IDialogInputComponent
{
    public string FieldName { get; set; } = "";

    private EntityConfig? config;
    private EntityField? field;
    private string classname = "";

    private bool _isValid;

    public void Init(Dictionary<string, object> args, EntityConfig? entity, EntityField? field)
    {
        classname = (string)args["classname"];
        this.config = entity;
        this.field = field;
    }

    public bool IsValid(UIContext context, JsonObject data)
    {
        var workspace = context.GetWorkspace();
        if (config == null || workspace == null) return true;

        return _isValid;
    }

    private EnumDescriptor? enumDescriptor;

    public void OnIMGUI(UIContext context)
    {
        var workspace = context.GetWorkspace();
        enumDescriptor ??= workspace?.Env.TypeCache.GetEnumDescriptor(classname);
        if (enumDescriptor == null) {
            ImGui.TextColored(Colors.Error, Lang.Errors.EnumNotFound.Format(classname));
            return;
        }

        var obj = (JsonObject)context.target!;
        if (context.children.Count == 0) {
            if (!obj.ContainsKey(FieldName)) {
                obj[FieldName] = JsonValue.Create(enumDescriptor.GetValues().First());
            }
            var handler = new RszEnumFieldHandler(enumDescriptor);
            context.AddChild<JsonObject, object>(context.label, obj, handler,
                (c) => c![FieldName]!.AsValue().Deserialize(enumDescriptor.BackingType),
                (c, v) => c[FieldName] = JsonValue.Create(v),
                UIOptions.DisableUndoRedo
            );
        }

        context.ShowChildrenUI();

        _isValid = true;
        if (config == null || workspace == null) return;

        var selectedId = Convert.ToInt64(context.children[0].GetRaw());
        if (workspace.ResourceManager.EntityExists(config.ShortName, selectedId)) {
            ImGui.TextColored(Colors.Error, Lang.Errors.MustBeUnique);
            _isValid = false;
        }
    }
}
