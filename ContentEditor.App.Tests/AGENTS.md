# Tests

## Key Files
| File | Purpose |
| --- | --- |
| ContentEditor.App.Tests.csproj | NUnit test project and dependencies |
| LocalizationTests.cs | Complete Lang coverage, JSON/YAML format tokens, native ImGui ID stability, caches and language fallback |
| ResourceManagerTests.cs | Resource behavior tests |
| ContentWorkspaceFixture.cs | Workspace fixture |
| TestFixtures.cs | Fixture factories |
| resources/agnostic_rsz.json | Synthetic RSZ classes for asset-free resource tests, including the Prefab class required by CommonRszClasses |

## Dependencies
- Existing NUnit and .NET 10 toolchain; no new packages.

## Common Patterns
- Tests mutating the global Lang state must be nonparallel and restore English afterward.
- Localization tests use language packs copied through the application project reference.
- ImGui ID tests call the native hash function without creating an editor or touching game files.
- Preserve dynamic user arguments and UTF-8 terminators; exercise language switching with already-cached labels.
- Keep the synthetic RSZ registry aligned with required common-class lookups when updating RE-Engine-Lib; its dummy IDs/layouts are for tests only, never game resource templates.
