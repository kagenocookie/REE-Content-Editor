using ContentEditor.Editor;

namespace ContentPatcher;

[ResourceField("DD2_StylePreview")]
public class PartSwapperPreview : FieldOnlyEntityField<PartSwapperPreview>
{
    public override string? ResourceType => null;
    public string DataField { get; private set; } = string.Empty;
    public string PartField { get; private set; } = string.Empty;

    public override ResourceHandler? CreateResourceHandler(ResourceConfig config) => new NoopResourceHandler<PartSwapperPreview>(config);

    public override void LoadParams(EntityFieldConfig data)
    {
        DataField = data.RequireParam<string>("dataField");
        PartField = data.RequireParam<string>("partField");
    }
}
