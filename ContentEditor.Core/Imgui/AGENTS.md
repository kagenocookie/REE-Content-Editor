# Shared UI primitives

## Key Files
| File | Purpose |
| --- | --- |
| LangHelpers.cs | Translatable strings, icons, tooltips and formatting helpers |
| UiText.cs | Presentation dictionary, interpolated text, UTF-8 caching and stable ImGui labels |
| ImguiHelpers.cs | Common controls; translate option labels while preserving original values |

## Common Patterns
- Updating a fixed interpolated format must refresh both String and UTF8 representations.
- Native text buffers must be null terminated. Language changes invalidate string and enum display caches.
- InterpolatedString<T> and InterpolatedString<T1,T2> inherit TranslatableBase; reset cached argument results when their format changes.
- UiText.F translates the format only; never translate a user-supplied interpolation argument. Label/FormatLabel preserve a source-derived ImGui identity across languages.
- Keep UIContext lookup keys, serialized field names, resource paths and enum values unchanged.
- Localization regression tests live in ContentEditor.App.Tests/LocalizationTests.cs.
