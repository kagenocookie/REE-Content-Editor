# Translation runtime

## Key Files
| File | Purpose |
| --- | --- |
| Lang.cs | Registry, YAML loading, built-in English fallback |

## Subdirectories
| Directory | Purpose |
| --- | --- |
| Translations | English UI definitions and language display names |

## Common Patterns
- Capture original formats once before the first language load. Reset to those formats before applying a different language pack.
- Missing packs must not leave the previous language's strings active.
- InterpolatedString types are registered through TranslatableBase and invalidate formatted caches when switching languages.
- Load <Language>.lang.yaml for registered fields and <Language>.ui.json for presentation strings. Clear both dictionaries when switching to a language without a pack.
- Keep context-specific translations separate; export each context's own entries.
