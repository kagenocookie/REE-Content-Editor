using ContentEditor.Core;

namespace ContentEditor.Editor;

public class EntityParams
{
    public string? ResourceType { get; set; }
    public long ResourceId { get; set; } = -1;
    public Entity? Entity { get; set; }
    public string? EntityField { get; set; }

    public EntityParams Clone() => (EntityParams)MemberwiseClone();

    public static readonly EntityParams Placeholder = new EntityParams();
}
