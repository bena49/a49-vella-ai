---
name: Phase 2c — Two-phase rename for sheet number swap chains
description: Next planned change to ExecuteBatchUpdateCommand.cs — detect swap cycles in batch sheet renumber, do a two-phase apply (TEMP_xxx, then final) to satisfy Revit's uniqueness constraint
type: project
---

Phase 2b shipped (pattern-based rename wizard, backend `rename_preview` handler, `clear_sheet_cache` invalidation, category-derivation fix for pure-numeric sheets, scheme-detection badge in the wizard header). The next planned increment is Phase 2c: handle sheet-number swap chains inside [revit-addin/Executor/Commands/ExecuteBatchUpdateCommand.cs](revit-addin/Executor/Commands/ExecuteBatchUpdateCommand.cs).

**Why:** When the user uses the rename wizard to swap sheet numbers (e.g. `1010 → 1020` and `1020 → 1010` in the same batch, or longer rotations like `A → B → C → A`), Revit rejects the second rename because two sheets briefly hold the same number — Revit's `SheetNumber` parameter has a uniqueness constraint enforced inside the transaction. Today the whole batch fails with a transaction error; the user has to manually rename one sheet to a temp value first.

**How to apply:**
1. Inside `ExecuteBatchUpdateCommand.Execute()`, before applying updates, build a graph of `old_number → new_number` mappings from the `updates` array.
2. Detect cycles / collisions: any case where a `new_number` appears as another row's `old_number`.
3. For colliding sheets, do a two-phase rename in a single transaction:
   - **Phase 1:** rename the colliding sheets to `TEMP_<unique_id_suffix>` (or any prefix guaranteed not to clash with any existing sheet number).
   - **Phase 2:** rename them to their final `new_number`.
4. Non-colliding sheets and name-only changes go through the existing single-pass path — no need to wrap them in two phases.
5. Keep it inside ONE Revit transaction so Ctrl+Z still rolls back the entire operation atomically (Phase 2b preserved this — don't break it).

**Reference patterns already in repo:**
- The current single-phase apply loop in [ExecuteBatchUpdateCommand.cs](revit-addin/Executor/Commands/ExecuteBatchUpdateCommand.cs) — keep its element lookup + transaction wrapper, just add the swap-detection pre-pass.
- Frontend already produces clean `{unique_id, element_type, changes: {number?, name?}}` updates from `preview_to_updates()` in [backend/ai_router/ai_engines/rename_pattern_engine.py](backend/ai_router/ai_engines/rename_pattern_engine.py) — no backend changes needed.

**Verification:**
- Pure swap: rename A=1010 ↔ B=1020. Expected: both succeed in one Ctrl+Z step.
- 3-cycle: A=1010 → B=1020 → C=1030 → A=1010. Expected: all three succeed.
- Mixed: half the batch swaps, half are pure-name renames. Expected: only the swap subset uses the temp pass; rest go through the fast single-pass path.
- Failure mode: if a final `new_number` matches an EXISTING (untouched) sheet outside the batch, surface a clear error and roll back — don't silently leave TEMP_xxx names behind.
