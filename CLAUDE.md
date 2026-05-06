Vella AI — current release: **v1.2.0** (dual-scheme sheet numbering: ISO19650 4-digit / 5-digit). Linked-model support added across tag automation + room elevation on 2026-05-06.

See `.claude-docs/` for detailed feature and project notes:
- `project_overview.md` — architecture, tech stack, repo layout
- `numbering_schemes.md` — ISO19650 4-digit vs 5-digit sheet numbering reference (added in v1.2.0)
- `shipped_*.md` — what landed in past sessions, grouped by date + theme. Read the most-recent ones first to understand current state.
- `next_release_*.md` — deferred features still in the pipeline
- `feedback_*.md` — user working-style preferences (commit workflow, methodical phasing, post-commit memory updates)

User-facing terminology and internal keys are aligned: **ISO19650 4-digit** (`iso19650_4digit`) / **ISO19650 5-digit** (`iso19650_5digit`). Sessions stored under the earlier `v1_small` / `v2_large` keys are auto-migrated by `resolve_scheme_for_request()`.

**Commit workflow:** For Phase 2 (Renumber/Rename wizard pipeline) work, the user creates and names all commits personally — do not run `git commit` unless they explicitly ask.

**Post-commit practice:** After the user finishes a feature and commits, proactively offer to write a `shipped_YYYY_MM_DD_<theme>.md` doc capturing what landed. The user works across multiple machines and pulls from GitHub; these docs are how the next session starts already aligned. See `feedback_post_commit_memory_update.md` for the full pattern.
