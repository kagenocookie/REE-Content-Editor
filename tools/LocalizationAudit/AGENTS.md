# Localization audit

## Key Files
| File | Purpose |
| --- | --- |
| LocalizationAudit.csproj | Console tool and SDK Roslyn references |
| Program.cs | Syntax-based source extraction and optional mechanical wrapping |

## Common Patterns
- Run with repository root and an output folder as the first two arguments. Default mode only reads source and writes reports.
- `--rewrite` changes source at known presentation sites; review every resulting diff. Do not translate serialization keys, paths, programmatic identifiers or user arguments.
- `ui-source.json` identifies templates with source locations; `lang-source.json` lists reflection-registered formats; `remaining-source.json` includes unclassified strings requiring human review.
- `--check` fails on missing registered or extracted UI translations; `coverage.json` also reports unchanged terms for manual review (format names and internal identifiers may be intentionally unchanged).
- Syntax matching is a review aid, not proof that every runtime path has been visually tested.
