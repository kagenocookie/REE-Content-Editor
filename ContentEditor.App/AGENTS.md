# Desktop editor

## Key Files
| File | Purpose |
| --- | --- |
| ContentEditor.App.csproj | .NET targets, package references, resource copying |
| Program.cs | Startup and configuration loading |
| UI.cs | ImGui setup and merged fonts |
| Configuration/AppConfig.cs | Persisted language and editor settings |

## Subdirectories
| Directory | Purpose |
| --- | --- |
| i18n | Deployable YAML language packs |
| Imgui/i18n | Translation registry and English source strings |
| fonts | Bundled fonts, including Noto Sans SC |

## Common Patterns
- Language packs load from AppContext.BaseDirectory/i18n and are copied by the project file.
- TextTooltip keys end in .Text or .Tooltip; icon strings retain their format placeholders.
- Keep English fallbacks for untranslated editor functionality.
- MeshViewer owns the shared object/animation Outliner, rendering controls and embedded mesh-editing options; MeshEditor retains selection and editing state. Preserve this upstream ownership when maintaining localization.
- New Outliner labels use Lang.MeshViewer registry entries; dynamic group labels use UiText.FormatLabel to preserve ImGui identity across language changes. Keep the YAML pack in sync with registered keys after upstream renames.
