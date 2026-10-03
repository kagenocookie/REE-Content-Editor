using System.Text.Json.Nodes;
using ContentPatcher;

namespace ContentEditor.App;

/// <summary>
/// Base interface for dynamic dialog input components.
/// </summary>
public interface IDialogInputComponent : IObjectUIHandler
{
    string FieldName { get; }

    void Init(Dictionary<string, object> args, EntityConfig? entity, EntityField? field);
    bool IsValid(UIContext context, JsonObject data);
}
