---
name: Next-release wishlist — Bilingual Sheet Name Toggle (EN ↔ TH)
description: User wants to store EN + TH sheet names per sheet and toggle which one is active. Captured 2026-05-04 right after the sheet-numbering refactor shipped.
type: project
originSessionId: f7663b11-c02d-4028-87ee-bee5ede39cd8
---
User wishlist captured at the end of the 2026-05-04 sheet-numbering refactor session.

**Goal**: Each sheet should hold BOTH an English and a Thai name; user (or project setting) can toggle which one is active for display / printing.

**Why this matters at A49**:
- Some clients require Thai-language drawings; others want English; some want both on alternate sheets.
- Thai level support is already in place (level_engine + level_matcher handle "ระดับพื้นชั้น 1" → L1 etc.). Sheet *names* are the next missing piece.
- Pattern precedent: RoomElevationWizard already has an EN/TH toggle for instructional copy — extend the idea to sheet names themselves.

**Likely scope** (estimate before designing — confirm before building):
- Backend: extend `generate_smart_name` (and `DEFAULT_SHEET_NAMES` / `CATEGORY_DEFAULT_NAME` in naming_engine.py) to produce both names. Probably return a dict `{en, th}` instead of a single string.
- Storage: where does the second name live? Two options:
  (a) Concatenate both in the Revit `Sheet.Name` parameter (e.g. "LEVEL 1 FLOOR PLAN / ผังพื้นชั้น 1") — simple, no schema change, but ugly when only one language is needed.
  (b) Add a custom Revit shared parameter (e.g. `Sheet Name TH`) that holds the alternate language — clean separation, but requires a project-template update + a toggle mechanism (worksharing? phase? view template?).
- Frontend: a settings toggle "Default sheet language: EN / TH / Both".
- C# add-in: writes both names if option (b); needs to know about the new shared parameter.

**Translations needed for default sheet names**:
- "LEVEL 1 FLOOR PLAN" → "ผังพื้นชั้น 1"
- "LEVEL B1 FLOOR PLAN" → "ผังพื้นชั้นใต้ดิน B1"
- "LEVEL ROOF PLAN" → "ผังพื้นดาดฟ้า"
- "SITE PLAN" → "ผังบริเวณ"
- "LEVEL 1 CEILING PLAN" → "ผังฝ้าเพดานชั้น 1"
- "COVER" → "ปก"
- "DRAWING INDEX" → "สารบัญแบบ"
- "SITE AND VICINITY PLAN" → "ผังบริเวณและที่ตั้งโครงการ"
- "STANDARD SYMBOLS" → "สัญลักษณ์มาตรฐาน"
- "SAFETY PLAN" → "ผังความปลอดภัย"
- "WALL TYPES" → "ประเภทผนัง"
- "ELEVATIONS" → "รูปด้าน"
- "BUILDING SECTIONS" → "รูปตัด"
- "WALL SECTIONS" → "รูปตัดผนัง"
- (Translations above are illustrative — get from Ben before implementing)

**Approach when starting**:
1. Confirm with user which storage model (a or b) they prefer
2. Get authoritative Thai translations for all default sheet names (Ben has the official A49 list)
3. Decide toggle scope: per-project setting? per-sheet? global Vella preference?
4. Backend: update naming_engine.py to emit both names
5. Frontend: add a language toggle (probably in Help → Standards or a new "Settings" tab)
6. C# add-in: update sheet creation to write both names per chosen model
7. Migration: existing English-only sheets — leave alone, or auto-add TH name?

**Out of scope** (don't conflate with this work):
- Sheet *number* format (already done in 2026-05-04 launch — purely numeric, language-agnostic)
- Bilingual VIEW names (separate feature; views have different naming conventions)
- Bilingual UI translation (Vella's chat / Help — that's a much larger localization effort)
