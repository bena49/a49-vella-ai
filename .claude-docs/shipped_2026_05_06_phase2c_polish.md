---
name: Shipped 2026-05-06 — Phase 2c + a49_dotted finalization
description: Major release session — swap-chain rename, mezz sub-parts, duplicate-handling 3-option flow, scheme-aware RoomElevation C#, C# auto-deploy, Revit 2021 build
type: project
---

Closed out the a49_dotted feature sweep + Phase 2c (swap-chain rename) + Revit 2021 standalone build in one long session. User confirmed each piece live in Revit.

**Shipped:**
- **Phase 2c** swap-chain rename in `ExecuteBatchUpdateCommand` (revit-addin only; original plan was at `.claude-docs/next_release_phase_2c_swap_chains.md`)
- **Mezzanine sub-parts** (e.g. A1.01.1 = B1M under B1 in a49_dotted) — `_next_sub_slot` + mezz routing in `build_sheets_payload`
- **Level elevation sort** upstream of view+sheet allocation — `sort_levels_for_sheet_creation` in `level_engine`
- **Ordinal floor naming** for a49_dotted (1ST/2ND/3RD vs LEVEL N), basements drop "LEVEL " prefix — `_level_label_dotted` in `naming_engine`
- **iso19650 sub-slot allocation** per spec (4-digit last-digit `1011`, 5-digit tens-digit `10110`) — `_next_iso_sub_slot`
- **Duplicate-handling 3-option flow** (Cancel/Skip/Sub-parts) — `detect_duplicate_levels` + `build_subpart_sheets` + wiring in `sheet_creator` + `batch_processor` + `views.py` interceptor + 3-layer defensive fallback
- **CreateAndPlaceWizard** view↔sheet pairing fixed (level-based via `_level` tag, was index-based)
- **RoomElevationWizard** scheme-aware (frontend computes detectedScheme + passes to C# `GetSafeSheetNumber`) — fixes a49_dotted projects getting iso19650 sheet numbers
- **Reference-sheet dropdown** filter recognises a49_dotted (`A1.NN`/`A5.NN`/`X0.NN`) + filters by `form.sheetCategory`
- **A0.06 = "CUSTOM"** (named slot fix), `_DOTTED_SHEET_RE` accepts 3-component sub-parts (A1.03.1)
- **Manual Edit** operation in RenameWizard (replaced auto `scheme_convert` per user preference); `new_number` cell now editable; reference parser regex extended to dotted format
- **C# auto-deploy** post-build target in `A49AIRevitAssistant.csproj` — RR2024→`Addins/2024/`, RR2025→`Addins/2025/`, framework-filtered so the right runtime lands at each target
- **Revit 2021 standalone build** at `revit-addin-2021/` (separate from `revit-addin/` for 2024/2025) — fixed 47 Revit-API-version errors + WebView2 native DLL flatten

**Test count at end of session:** 475 backend tests passing across `test_sheet_numbering`, `test_level_matcher`, `test_rename_pattern_engine`. All 3 schemes covered (a49_dotted, iso19650_4digit, iso19650_5digit).

**What's still pending:**
- **Phase 2d** — rename session log + undo button. Plan at `.claude-docs/next_release_phase_2d_session_log_undo.md`. Depends on Phase 2c which is now shipped, so unblocked.
- A few polish items from earlier next-release notes still in the queue (formula helper, bilingual sheet names, insert-between-slots wizard).

**How to apply:** When the user asks about any of the shipped features above, the implementation is in code — `git log --since=2026-05-06` is the authoritative changelog.
