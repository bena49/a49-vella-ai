---
name: A49 Sheet Numbering — Dual-Scheme Reference
description: V1 (4-digit small project) and V2 (5-digit large project) numbering schemes shipped in v1.2.0. Single source of truth lives in naming_engine.SCHEMES.
type: project
---

The naming engine supports **two numbering schemes** selected per project. Both are config-driven from `SCHEMES` in [`backend/ai_router/ai_engines/naming_engine.py`](backend/ai_router/ai_engines/naming_engine.py). Adding a third scheme is a config addition, not a code change.

## Scheme selection (per request)

`resolve_scheme_for_request(request)` picks the active scheme using this priority:

1. **Auto-detect** from `request.session["ai_last_known_sheets"]` — if any A49-shaped sheet number (pure digits or `X`+digits) is 5+ chars, scheme = `v2_large`. Else if any are present and 4 chars, scheme = `v1_small`. Decisive — **wins over the override** so a v2 project is never accidentally written to v1 (mixed-scheme projects are explicitly disallowed).
2. **Session override** `request.session["ai_numbering_scheme"] = "v1_small" | "v2_large"` — used for new/empty projects where auto-detect can't decide.
3. **Default** `v1_small`.

Set the override via chat: `use v2 numbering`, `use v1 numbering`, `what numbering scheme`. Handlers live in [`conversation_engine.py`](backend/ai_router/ai_engines/conversation_engine.py) under section 5.

## v1_small (4-digit, default)

Used for typical projects. Format: 4-digit numeric or `X`+3-digit.

| Category | Base | Primary +inc | Sub +inc | Example slots |
|---|---|---|---|---|
| A0 General | 0 | 10 | 1 | 0000 (Cover), 0010 (Index), 0020 (Site/Vicinity) |
| A1 Floor Plans | 1000 | 10 | 1 | 1000 (SITE), 1010 (L1), 1011 (L1M), 1009 (B1), 1001 (B9) |
| A2 Elevations | 2000 | 10 | 1 | 2010, 2020, 2030 |
| A3 Building Sections | 3000 | 10 | 1 | 3010, 3020, 3030 |
| A4 Wall Sections | 4000 | 10 | 1 | 4010, 4020 |
| A5 Ceiling Plans | 5000 | 10 | 1 | 5010 (L1), 5009 (B1), no SITE |
| A6 Enlarged | 6000 | 10 | 1 | 6010 (FLOOR PATTERN PLAN), 6020 (TOILET), 6030 (CANOPY) |
| A7 Vertical Circ | 7000 | 10 | 1 | 7010 (STAIR PLAN), 7020 (STAIR SECTION) |
| A8 Door/Window Sched | 8000 | 10 | 1 | 8010 (DOOR), 8020 (WINDOW) |
| A9 Details | 9000 | 10 | 1 | 9010, 9020 |
| X0 Custom | n/a | 10 | 1 | X000, X010, X020 |

Constraints: 1 SITE slot in A1, basement count = 9 max (B1-B9), level cap = L99.

## v2_large (5-digit)

Used for large projects that need more slot density. Format: 5-digit numeric or `X`+4-digit. Every increment is ×10 the v1 value.

| Category | Base | Primary +inc | Sub +inc | Example slots |
|---|---|---|---|---|
| A0 General | 0 | 100 | 10 | 00000 (Cover), 00100 (Index), 00200 (Site/Vicinity) |
| A1 Floor Plans | 10000 | 100 | 10 | 10000-10009 (10 SITE slots), 10100 (L1), 10110 (L1M), 10120 (L1T), 10090 (B1), 10010 (B9) |
| A2 Elevations | 20000 | 100 | 10 | 20100, 20200, 20300 |
| A3 Building Sections | 30000 | 100 | 10 | 30100, 30200 |
| A4 Wall Sections | 40000 | 100 | 10 | 40100, 40200 |
| A5 Ceiling Plans | 50000 | 100 | 10 | 50100 (L1), 50090 (B1), no SITE |
| A6 Enlarged | 60000 | 100 | 10 | 60100 (FLOOR PATTERN PLAN), 60200 (TOILET), 60300 (CANOPY) |
| A7 Vertical Circ | 70000 | 100 | 10 | 70100, 70200 |
| A8 Door/Window Sched | 80000 | 100 | 10 | 80100, 80200 |
| A9 Details | 90000 | 100 | 10 | 90100, 90200 |
| X0 Custom | n/a | 100 | 10 | X0000, X0100, X0200 |

Constraints: 10 SITE slots in A1 (10000-10009), basement count = 9 max (B9-B1 spaced +10 apart at 10010-10090), level cap = L99, 9 sub-slots per level for mezzanine/transfer/etc.

## Sub-level encoding (M, T, etc.)

User-assigned, free-form. The engine just allocates the next available sub-slot via the collision loop:
- v1: L1 = 1010 → L1M = 1011 → L1T = 1012 (collision pushes +1)
- v2: L1 = 10100 → L1M = 10110 → L1T = 10120 (collision pushes +10)

No hardcoded "M = +1" or "T = +20" rule — whichever sub-level is requested first gets the lowest free sub-slot.

## Basement encoding

`B<N>` slots descend from the top: closer to grade = larger N is closest to L1.
- v1: B<n> = base + (10 - n) → B1=1009, B2=1008, … B9=1001
- v2: B<n> = base + (10 - n) × 10 → B1=10090, B2=10080, … B9=10010

When both `B<N>` and `B<N>M`/`B<N>T` exist in the same request, the suffix variant takes the parent's natural slot (closer to grade) and the bare basement cascades down by `sub_increment`. See `compute_sheet_slot()` for the cascade logic.

## Roof / TOP encoding

`roof_offset = "auto"` (both schemes) → `base + (max_above_grade_level + 1) × level_increment`. Roof always lands one primary slot above the project's highest L<N>.
- v1, project max L5 → ROOF = 1060
- v2, project max L5 → ROOF = 10600

## Backwards compatibility

- All existing v1 projects continue working unchanged. Auto-detect picks v1 from existing 4-digit sheets.
- Frontend Match Reference filter accepts both 4-digit (v1) and 5-digit (v2) sheets via `startsWith('1') || startsWith('5') || startsWith('X')` — see [`CreateAndPlaceWizard.vue`](frontend/components/wizards/CreateAndPlaceWizard.vue).
- Backend reference-sheet regex in [`gpt_integration.py`](backend/ai_router/ai_core/gpt_integration.py) accepts 4-8 char sheet numbers (covers both schemes plus master/placeholder sheets like `10XX` or `1010XX`).
- Cover titleblock auto-assignment in [`titleblock_engine.py`](backend/ai_router/ai_engines/titleblock_engine.py) recognises both `"0000"` (v1) and `"00000"` (v2).

## Adding a new scheme

1. Add an entry to `SCHEMES` in `naming_engine.py` with `digit_count`, per-category `base`/`primary_increment`/`sub_increment`/`format`, plus level-specific fields (`level_increment`, `sub_level_increment`, `site_slots`, `basement_count`, `roof_offset`) for A1/A5.
2. Add fixture cases to [`test_sheet_numbering.py`](backend/ai_router/ai_engines/test_sheet_numbering.py) — mirror the V1/V2 pattern.
3. Add a chat-command label in [`conversation_engine.py`](backend/ai_router/ai_engines/conversation_engine.py) section 5 if you want a user-facing toggle.
4. Update auto-detect in `_detect_scheme_from_sheets` if the new scheme has a distinguishable shape (e.g. different digit count or prefix).

## Test coverage

Run via `cd backend && python -m ai_router.ai_engines.test_sheet_numbering`. Current count: 235 cases (111 V1 + 101 V2 + 23 scheme-detection).
