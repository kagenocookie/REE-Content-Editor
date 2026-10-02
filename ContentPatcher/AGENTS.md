# Content patching and publication

## Key Files
| File | Purpose |
| --- | --- |
| `AGENTS.md` | Publication maintenance guidance |
| `ContentPatcher.csproj` | .NET target and project/package dependencies |
| `PatchParameters.cs` | Explicit output mode, bundle metadata, PAK and symlink options |
| `Patcher.cs` | Patch and publication orchestration |
| `Program.cs` | Command-line entry point |
| `Data/ResourceManager.cs` | Resource lookup, caching and loading |

## Dependencies
- .NET 10, ContentEditor.Core, the repository-pinned RE-Engine-Lib submodule and VYaml 1.2.0.

## Common Patterns
- Keep GamePatch, Publish and BundlePublish behavior distinct through PatchParameters.OutputType.
- IncludeBundleJsonForPublish controls bundle.json publication; IncludePatchMetadataJson controls restoration metadata. Preserve both options when adding callers.
- PAK output still publishes reframework/ files as loose files beside the PAK.
- Keep generated bin/obj output and game resources outside source commits.
