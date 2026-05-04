---
name: Next-release plan — Sheet Numbering Format Refactor
description: Deferred from 2026-05-04 launch. User wants new sheet numbering format spec; affects naming_engine.py, sheet_creator.py, and the C# Revit add-in.
type: project
originSessionId: f7663b11-c02d-4028-87ee-bee5ede39cd8
---
Deferred to the release after 2026-05-04 (Spot Elevations + Help redesign + Comment form + bilingual level support launch).

**User's intent**: Change A49 office sheet numbering format to a new convention. Spec to be provided when work begins.

**Current format (for reference)**:
- A0 series: `A0.00`, `A0.01`, `A0.02` … (starts at 00 for cover)
- A1 series (level-based): `A1.01`, `A1.02`, `A1.B1`, `A1.B1M`, `A1.SITE` …
- A5 series (level-based): `A5.01`, `A5.02` … (Site level skipped)
- A2-A9 series (sequence-based): `A2.01`, `A2.02`, …, `A9.01`, etc.
- X0 series (custom): `X0.01`, `X0.02` …

**Files that touch sheet numbering**:
- `backend/ai_router/ai_engines/naming_engine.py`:
  - `get_next_sheet_number(sheet_type, existing_numbers, requested_index)`
  - `build_sheets_payload(...)` — the level-based vs sequence-based branching at line ~331
  - `level_to_code(level)` — what gets appended after the dot
  - `SHEET_SET_MAP`, `DEFAULT_SHEET_NAMES`, `PROJECT_PHASE_MAP`
  - `generate_smart_name(sheet_type, level_code, sequence_index)` — sheet display names
- `backend/ai_router/ai_commands/sheet_creator.py`:
  - `finalize_create_sheets(request)` — consumes the cat + levels + count
  - `execute_sheet_creation(request)` — calls naming engine
  - The smart-inference keyword block at line ~107 (cover/site/ceiling → A-code mapping)
  - The auto-detect-from-levels fallback at line ~152 (defaults to A1 — could become more conservative)
- `revit-addin/Executor/Commands/` — C# side that consumes sheet payload and writes to Revit
- `frontend/utils/a49Standards.ts` — currently has shorter A6/A8 names than backend (A6_ENLARGED PLANS vs A6_ENLARGED PLANS AND INTERIOR ELEVATIONS; A8_DOOR AND WINDOW vs A8_DOOR AND WINDOW SCHEDULE). Sync at the same time.

**Approach when starting**:
1. Get spec from user (new format examples for each category)
2. Write test fixtures FIRST (pattern of test_level_matcher.py) — input → expected sheet number, across all categories
3. Update naming_engine.py + sheet_creator.py to produce new format
4. Run tests
5. Coordinate any C# side changes (probably read sheet_number from envelope as-is, no logic change needed there)
6. Pilot to one staff member before broad rollout

**Out of scope for the format refactor**:
- The auto-detect-from-levels fallback at sheet_creator.py:152 silently downgrades unspecified categories to A1 when no "ceiling" keyword present. The regex fallback in gpt_integration.py now covers most "Sheets A0-A9" cases, so this fallback is less of an issue, but worth making more conservative (e.g., refuse to default + ask user) at the same time.

**Effort estimate**: ~1 day including tests + verification. Not safe for the 2026-05-04 launch.
