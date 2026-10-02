# Repository guide

## Key Files
| File | Purpose |
| --- | --- |
| README.md | Upstream usage and development guide |
| content-patcher.sln | .NET solution |
| Directory.Build.props | Shared configuration resource path |
| .gitmodules | Pinned external library sources |

## Subdirectories
| Directory | Purpose |
| --- | --- |
| ContentEditor.App | Desktop editor and localized UI |
| ContentEditor.App.Tests | NUnit tests |
| ContentEditor.App.PlatformShared | Shared platform integration |
| ContentEditor.App.Windows | Windows integration |
| ContentEditor.Core | Shared editing and UI primitives |
| ContentEditor.SourceGenerators | Source generators |
| ContentPatcher | Patching and resource handling |
| RE-Engine-Lib | Git submodule: RE ENGINE formats |
| GDeflateNet | Git submodule: decompression |
| configs | Per-game configuration |
| docs | Documentation, including localization-zh-CN.md |
| tools | Developer tools, including the Roslyn localization audit |
| .github | CI workflows and project images |

## Dependencies
- Git with initialized submodules; .NET 10 SDK.
- Windows application target: net10.0-windows; .NET 10 Desktop Runtime.
- Package versions remain in project files; localization introduces no new packages.
- The upstream merge pins the editor's RE-Engine-Lib submodule to `61f0cceeb81aca706b033f61b924d434a7d69297`; keep this gitlink independent of the newer standalone sibling reference checkout.

## For AI Agents
- Preserve upstream history; origin is the user's cn fork and upstream is the original repository.
- Use the existing YAML localization mechanism. Do not translate game identifiers or paths.
- Synchronize relevant AGENTS.md on structural changes; preserve sections between `<!-- MANUAL -->` markers.
- Build the Windows target and run LocalizationTests after localization logic changes.
- Audit language coverage with tools/LocalizationAudit; review remaining-source.json manually because it includes technical identifiers and application data.
