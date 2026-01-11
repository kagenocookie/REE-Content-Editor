namespace ContentPatcher;

public interface IEntitySetup
{
    public string[]? SupportedGames { get; }
    public void Setup(ContentWorkspace workspace);
}
