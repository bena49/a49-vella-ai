Vella AI — current release: **v1.2.0** (dual-scheme sheet numbering: ISO19650 4-digit / 5-digit).

See `.claude-docs/` for detailed feature and project notes:
- `project_overview.md` — architecture, tech stack, repo layout
- `numbering_schemes.md` — ISO19650 4-digit vs 5-digit sheet numbering reference (added in v1.2.0)
- `next_release_*.md` — deferred features still in the pipeline

User-facing terminology and internal keys are aligned: **ISO19650 4-digit** (`iso19650_4digit`) / **ISO19650 5-digit** (`iso19650_5digit`). Sessions stored under the earlier `v1_small` / `v2_large` keys are auto-migrated by `resolve_scheme_for_request()`.
