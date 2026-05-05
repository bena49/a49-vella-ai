---
name: Phase 2d — Rename session log + undo button
description: Deferred follow-up to Phase 2c — capture each batch update's before-state and surface an in-app undo so users can roll back rename runs without relying on Revit's Ctrl+Z stack
type: project
---

Deferred until after Phase 2c. Adds a per-session log of rename operations and a one-click undo path so the user can revert a bulk rename even after they've done other Revit work that would otherwise bury the operation in Revit's undo stack.

**Why:** Today the only way to undo a wizard-driven batch rename is Ctrl+Z inside Revit. As soon as the user does anything else (place a view, edit a parameter), the rename slides down the undo stack and reverting it becomes destructive. Bulk renames affect hundreds of sheets — losing the ability to revert is a real risk. The session log gives us a safety net independent of Revit's transaction history.

**How to apply:**
1. **Backend session storage** — extend the rename_preview / batch_update flow to keep the last N operations in `request.session["ai_rename_history"]` as a list of:
   ```python
   {
     "timestamp": "2026-05-05T19:30:00",
     "operation": "translate_en_to_th",
     "params":    { ... },
     "before":    [{"unique_id": "...", "number": "1010", "name": "L1 PLAN"}, ...],
     "after":     [{"unique_id": "...", "number": "1010", "name": "ผังพื้น..."}, ...],
   }
   ```
   The `before` snapshot is the slice of the inventory that was actually changed (filter to selected+changed rows). Capture it in `handle_rename_preview` OR in a new `handle_rename_apply` step — leaning toward the latter so we only log committed operations, not previews the user backed out of.

2. **New backend immediate command** — `undo_last_rename`:
   - Pops the last entry from `ai_rename_history`.
   - Rebuilds an `execute_batch_update` envelope from the `before` snapshot (swap roles: `before.number` becomes the new `number`, `after` becomes the old).
   - Returns `revit_command: {command: "execute_batch_update", raw: {updates: [...]}}` — same path the wizard uses, so swap-chain handling from Phase 2c automatically covers undo too.
   - Also fires `clear_sheet_cache` after to keep `ai_last_known_sheets` honest.

3. **Frontend UI** — small toast or inline button that appears for ~30s after a rename completes ("↶ Undo 11 sheet renames"). Implementation hooks into [useWizards.ts](frontend/composables/useWizards.ts) `handleBatchSubmit` — after the batch fires, push a transient action message that calls `sendToBackend({message: "undo_last_rename"})` when clicked.

4. **History cap** — keep last 5–10 operations only. Store inline in the Django session (no DB schema change).

**Out of scope for 2d:**
- Multi-step undo (just last-one-back is enough for first ship).
- Persistence across sessions — if Django logs the user out, history clears. Acceptable.
- Undo for non-rename batch updates (Phase 2d only covers operations that came through the rename wizard).

**Reference patterns:**
- `ai_pending_request_data` stash/restore pattern in [callback_handler.py](backend/ai_router/ai_core/callback_handler.py) — same idea, just persisting before-state instead of pending-command-state.
- `clear_sheet_cache` immediate command shipped in Phase 2b — minimal stateless command pattern to mirror for `undo_last_rename`.
