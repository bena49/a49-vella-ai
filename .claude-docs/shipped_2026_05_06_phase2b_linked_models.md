---
name: Shipped 2026-05-06 — Phase 2b rename wizard + linked-model support
description: Pattern-based renumber/rename wizard, basement sub-parts in iso19650_5digit, sub-sheets cosmetic prompt, CreateAndPlaceWizard reference-sheet UX, full linked-model awareness for tagging + room elevation
type: project
---

Second large milestone of 2026-05-06 (a separate session from the earlier Phase 2c + a49_dotted finalization captured in `shipped_2026_05_06_phase2c_polish.md`). Themes were: ship the redesigned rename wizard, close out a few small numbering/UX gaps, then tackle linked-model support across the C# tag and room-elevation automations.

**Shipped:**

### Phase 2b — Pattern-based Renumber/Rename wizard
- New backend engine `backend/ai_router/ai_engines/rename_pattern_engine.py` with 8 operations: `find_replace`, `case_transform`, `prefix_suffix`, `add_stage_prefix`, `translate_en_to_th`, `translate_th_to_en`, `offset_renumber`, `scheme_convert`. 50/50 unit tests.
- New backend handler `backend/ai_router/ai_commands/rename_preview.py` exposed as a stateless immediate command (`rename_preview`, `list_rename_operations`). Wizard fetches previews via `useChat.submitDirect` so chat state isn't touched.
- Frontend `frontend/components/wizards/RenameWizard.vue` fully rewritten — three-step stepper (Filter → Operation → Preview & Apply) with per-row deselect checkboxes in the diff table.
- `frontend/composables/useWizards.ts` accepts `submitDirect` and exposes `requestRenamePreview` injected into `renameWizardProps` from both entry paths.
- `handleBatchSubmit` fires `clear_sheet_cache` immediately after `execute_batch_update` (new backend immediate command in `intent_router.py`) so the next sheet-needing command re-fetches fresh data.
- Wizard header shows the auto-detected numbering scheme as a badge; new `deriveCategory()` correctly groups pure-numeric sheets like `10100` under A1 (the C# regex `^([A-Z]+[0-9]?)` only categorises letter-prefixed numbers).

### Basement sub-parts for `iso19650_5digit`
- New optional `basement_sub_increment` field on A1/A5 categories — present (=1) only on iso19650_5digit. When set, basement primaries (e.g. 10080 / 10090) become eligible parents for sub-parts using the units digit (10081…10089, 10091…10099). 4-digit retains the existing skip-with-warning because its basements are 1 apart.
- New helper `_iso_sub_stride(parent_slot, scheme, category)` is the single source of truth for the stride and upper bound. Both `_next_iso_sub_slot` and `build_subpart_sheets` delegate to it so the `(Part N)` ordinal naming lines up with the actual stride.
- Skip reason updated from "basement has no sub-slot room in this scheme" → "this scheme doesn't support basement sub-parts (or all sub-slots are taken)" for accuracy.
- 8 new test cases in `test_sheet_numbering.py` (now 379 cases, all green).

### Cosmetic prompt update — sub-parts → sub-sheets
Three places display the duplicate-handling prompt; all now read identically:
```
What would you like to do? Reply with one of:
  • ** cancel ** — abort, no sheets created
  • ** skip ** — only create sheets for new (non-duplicate) levels
  • ** sub-sheets ** — create duplicates as sub-parts of the existing sheets
```
- `sheet_creator.py`, `batch_processor.py`, `views.py` re-prompt fallback all aligned. Regex extended in all three to accept BOTH `sub[\s\-]?parts?` and `sub[\s\-]?sheets?` so old quick-reply buttons still work. Internal session value `ai_duplicate_choice = "subparts"` left unchanged.

### CreateAndPlaceWizard "Match Reference" UX fix
- The reference-sheet dropdown wouldn't open on first click — the `await nextTick() × 2` racing-the-mount approach was unreliable. Replaced with a `watch(refSheetInput, …)` that fires the moment the input ref becomes non-null. Added `@click` and `@input` safety nets on the input so subsequent clicks always re-open.

### Item 1 — Linked-model support for tag automation
New file `revit-addin/Executor/Commands/TagStrategies/LinkedTagHelpers.cs` provides:
- `EnumerateLinks(hostDoc, view)` — yields `(RevitLinkInstance, Document, Transform)` tuples for loaded, view-visible links.
- `CollectAlreadyTaggedLinkedIds(hostDoc, view, linkInstanceId, category)` — link-aware dedup via `IndependentTag.GetTaggedReferences()` + `Reference.LinkedElementId`.
- `BuildLinkedReference(elem, link)` — wraps `Reference.CreateLinkReference`.

All 5 strategies got an additive **linked pass** (host pass unchanged):
- `DoorTagStrategy`, `WindowTagStrategy`, `CeilingTagStrategy` — straightforward bbox/centroid + `link.Transform.OfPoint`.
- `WallTagStrategy` — segments linked walls using the link's own rooms, collects openings hosted on linked walls from the same link doc, transforms tag head perpendicular through the link transform.
- `RoomTagStrategy` — plan path uses `NewRoomTag(LinkElementId(linkInst, roomId), uv, viewId)`, elev/section uses `IndependentTag.Create` with a link reference. Already-tagged check inspects `RoomTag.TaggedRoomId` (LinkElementId).

Spot-elevation in `AutoTagCommand.cs` got the deepest treatment:
- New `FindFloorRefForRoom(hostDoc, view, roomLink_or_null, room, roomCenter)` searches three docs in priority: room's own doc, host doc, every other loaded link. Handles BOTH "rooms in host, slab in linked structural" AND "rooms in linked arch, slab anywhere" patterns.
- Bbox transform uses `searchToHost.Inverse * roomToHost` so the floor lookup hits even when host and link have different coordinate systems.
- Face references from a link are wrapped via `CreateLinkReference()`; host face references are used as-is.
- New `CollectTaggedFloorKeys()` builds composite dedup keys: `"F<floorId>"` for host floors, `"L<linkInstId>:F<floorId>"` for linked. Fixed the previous bug where all linked-floor spots false-collapsed under the LinkInstance ID.
- The dedup set is seeded ONCE from pre-existing tags — `taggedFloorKeys.Add(dedup)` is **deliberately NOT called inside the loop** so multiple rooms over the same slab each get their own spot (the typical "many rooms, one slab" case).

### Item 2 — Linked-model support for room elevation
Single file: `InteractiveRoomPackageCommand.cs`.
- `RoomSelectionFilter` now takes the host `Document` in its constructor. `AllowElement` admits `RevitLinkInstance` (so the picker doesn't reject linked clicks); `AllowReference` resolves linked references through the link's doc and only returns true when the linked element is a `Room` or `RoomTag`.
- New helper `ResolveRoomFromReference(hostDoc, roomRef)` returns `(Room, Transform)` — `Transform.Identity` for host picks, `link.GetTotalTransform()` for linked. Covers all four cases (host Room, host RoomTag, linked Room, linked RoomTag).
- Bbox in the callout-creation step uses `targetRoom.get_BoundingBox(activePlan) ?? targetRoom.get_BoundingBox(null)` — the `?? null` fallback catches linked rooms where the view-scoped overload returns null. Corners run through `roomToHost.OfPoint` with per-axis min/max re-normalization for links rotated about Z.
- Stashes `_roomBboxFullHost` (full 3D bbox in host coords) on a private field so it survives the gap between the first transaction and the deferred `ContinueAfterMarkerPick` call.
- New helper `SetElevationCropToRoom(elevView, bboxHost)` projects the 8 corners through `elevView.CropBox.Transform.Inverse`, takes per-axis min/max in elevation-local coords, applies 200mm XY / 300mm Z padding, and writes back as the new `CropBox`. Called after each `marker.CreateElevation` so the four elevations crop tightly to the room — fixes the staff report of "elevation height crop and view limits are off." Benefits host rooms too (Revit's default crop was a heuristic guess; this is deterministic).

**What's still pending:**
- **Phase 2d** — rename session log + undo button. Plan still at `next_release_phase_2d_session_log_undo.md`. Phase 2c is shipped, so unblocked.
- **Item 3 from this session** (deferred when item 2 was confirmed working) — survey other automations (AutoDimCommand, AutomateTagNlp flows, refresh_project_info / cache populators) for the same host-only collector pattern.
- The earlier next-release queue (formula helper, bilingual sheet names, insert-between-slots wizard) is still open.

**How to apply:** `git log --since=2026-05-06` is the authoritative changelog from this date forward. Commit messages in this session follow the pattern `<feature> - <Edit/Fix> NN` (e.g. `Linked file - Edit 02`, `Match Reference - Click 01`).
