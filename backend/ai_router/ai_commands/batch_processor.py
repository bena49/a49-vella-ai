# ai_router/ai_commands/batch_processor.py (UNLOCKED VERSION)

from rest_framework.response import Response
from ..ai_core.session_manager import debug_session, reset_pending
from ..ai_utils.envelope_builder import (
    send_envelope, envelope_create_views, envelope_create_sheets, envelope_place_view_on_sheet
)
from ..ai_engines.naming_engine import (
    apply,
    build_sheets_payload,
    build_subpart_sheets,
    sort_key_sheet_number,
    resolve_scheme_for_request,
    detect_duplicate_levels,
)
from ..ai_engines.level_engine import sort_levels_for_sheet_creation
from ..ai_engines.titleblock_engine import parse_titleblock_from_user_text
from .sheet_creator import request_titleblock_choice

def message(text):
    return Response({"message": text})

def finalize_create_and_place(request):
    """
    Orchestrates: 1. Create View, 2. Create Sheet, 3. Place View on Sheet.
    Includes ANTI-LOOP safeguards.
    """
    debug_session(request, "ENTERING finalize_create_and_place() orchestration.")
    
    # 0) ENSURE TITLEBLOCK
    tb_raw = request.session.get("ai_pending_titleblock")
    if not request.session.get("titleblock_family") and tb_raw:
        fam, t_type = parse_titleblock_from_user_text(tb_raw)
        if fam and t_type:
            request.session["titleblock_family"] = fam
            request.session["titleblock_type"] = t_type
            request.session.modified = True
        else:
            request.session["ai_pending_titleblock"] = None
            request.session.modified = True
            return request_titleblock_choice(request)

    if not request.session.get("ai_pending_titleblock"):
        return request_titleblock_choice(request)

    # 1) CHECK CACHE (With Loop Protection)
    views_cache = request.session.get("ai_last_known_views") or []
    sheets_cache = request.session.get("ai_last_known_sheets") or []
    levels_cache = request.session.get("ai_last_known_levels") or []

    has_views = len(views_cache) > 0
    has_sheets = len(sheets_cache) > 0
    has_levels = len(levels_cache) > 0

    pending_levels = request.session.get("ai_pending_levels_parsed") or []
    pending_data = request.session.get("ai_pending_request_data") or {}
    last_action = pending_data.get("last_action")
    fetch_attempted = request.session.get("ai_levels_fetch_attempted")

    # A0. Need Levels? (Chat-typed commands skip the wizard's level cache step.
    # Without this, special tokens like SITE / TOP fail downstream because the
    # C# digit-fallback can't match them against Thai-named project levels.)
    if pending_levels and not has_levels and not fetch_attempted:
        request.session["ai_levels_fetch_attempted"] = True
        save_pending_state(request, "fetching_levels")
        return send_envelope(request, {"command": "get_levels"})

    # Sort levels per the a49_dotted spec (SITE → basements deepest-first →
    # numbered floors → ROOF). Critical here: views are created in level
    # order and sheets are allocated in level order, then paired by index
    # below. If the two orders differ, view↔sheet pairing is broken
    # (e.g. SITE_view ends up on the ROOF sheet). Sorting up-front means
    # both pipelines consume the same canonical order.
    if pending_levels:
        pending_levels = sort_levels_for_sheet_creation(pending_levels)
        request.session["ai_pending_levels_parsed"] = pending_levels
        request.session.modified = True

    # A. Need Views?
    if not has_views:
        if last_action == "fetching_views":
            reset_pending(request)
            return message("Error: Unable to fetch views from Revit. (Loop detected)")
        save_pending_state(request, "fetching_views")
        return send_envelope(request, {"command": "list_views"})

    # B. Need Sheets?
    if has_views and not has_sheets:
        if last_action == "fetching_sheets":
            reset_pending(request)
            return message("Error: Unable to fetch sheets from Revit. (Loop detected)")
        save_pending_state(request, "fetching_sheets")
        return send_envelope(request, {"command": "list_sheets"})

    # ────────────────────────────────────────────────────────────
    # DUPLICATE DETECTION — same 3-option contract as sheet_creator's
    # standalone Create Sheets flow. Detection runs against the project
    # inventory cache (NUMBER + NAME). If duplicates are found and we
    # haven't already resolved a choice in this session, prompt the user.
    # ────────────────────────────────────────────────────────────
    scheme = resolve_scheme_for_request(request)
    duplicate_choice = request.session.get("ai_duplicate_choice")
    duplicate_info = request.session.get("ai_pending_duplicate_info") or []
    primary_cat = request.session.get("ai_pending_sheet_category")

    # 🛡️ DEFENSIVE FALLBACK — same as sheet_creator. If we have stored
    # duplicate_info but no resolved choice, treat the user's raw message
    # text as the choice. Catches cases where the views-level interceptor
    # was bypassed (e.g. GPT misclassified the short reply).
    if duplicate_info and not duplicate_choice:
        import re as _re
        raw_msg_l = (request.data.get("message", "") or "").strip().lower()
        if _re.search(r'\b(cancel|abort|stop|nevermind|never\s*mind)\b', raw_msg_l):
            duplicate_choice = "cancel"
        elif _re.search(r'\b(sub[\s\-]?(?:parts?|sheets?))\b', raw_msg_l) or "create as sub" in raw_msg_l:
            duplicate_choice = "subparts"
        elif _re.search(r'\bskip(\s+dup\w*)?\b', raw_msg_l):
            duplicate_choice = "skip"
        if duplicate_choice:
            debug_session(request,
                f"🛡️ finalize_create_and_place fallback resolved duplicate-choice='{duplicate_choice}' from raw msg")
            request.session["ai_duplicate_choice"] = duplicate_choice
            request.session["ai_expecting_duplicate_choice"] = False
            request.session.modified = True

    if pending_levels and primary_cat and not duplicate_choice:
        existing_inventory = request.session.get("ai_last_known_sheets_full") or []
        # First category only — same scoping rule as sheet_creator.
        cat0 = primary_cat.split(",")[0].strip() if "," in primary_cat else primary_cat
        duplicates = detect_duplicate_levels(cat0, pending_levels, existing_inventory, scheme=scheme)
        if duplicates:
            request.session["ai_pending_duplicate_info"] = duplicates
            request.session["ai_expecting_duplicate_choice"] = True
            request.session.modified = True
            # Force-save now so the flag is durably stored before the prompt
            # response goes back to the client (defensive — see same comment
            # in sheet_creator.execute_sheet_creation).
            try: request.session.save()
            except Exception: pass
            lines = ["⚠ Sheets already exist for these levels:"]
            for d in duplicates:
                lines.append(f"  • {d['existing_number']} — {d['existing_name']}")
            lines.append("")
            lines.append("What would you like to do? Reply with one of:")
            lines.append("  • **cancel** — abort, no sheets created")
            lines.append("  • **skip** — only create sheets for new (non-duplicate) levels")
            lines.append("  • **sub-sheets** — create duplicates as sub-parts of the existing sheets")
            return Response({
                "message": "\n".join(lines),
                "options": ["Cancel", "Skip duplicates", "Create as sub-sheets"],
            })

    duplicate_levels_set = {d["level"] for d in duplicate_info}

    if duplicate_choice == "cancel":
        request.session["ai_duplicate_choice"] = None
        request.session["ai_pending_duplicate_info"] = None
        request.session["ai_expecting_duplicate_choice"] = False
        request.session.modified = True
        reset_pending(request)
        return message("✋ Cancelled. No sheets or views were created.")

    # 2) EXECUTION - View Naming
    scope_box = request.session.get("ai_pending_scope_box_id")

    # TRANSLATOR LINE:
    final_scope_box = None if scope_box == "SKIP" else scope_box

    # For Skip: drop duplicate levels entirely. For Sub-parts: keep all
    # levels because we still need a NEW VIEW per duplicate level (Revit
    # auto-suffixes duplicate view names with " Copy 1"). The DIFFERENCE
    # between primary and sub-part is the SHEET, not the view.
    view_levels = pending_levels
    if duplicate_choice == "skip" and duplicate_levels_set:
        view_levels = [l for l in pending_levels if l not in duplicate_levels_set]
        if not view_levels:
            request.session["ai_duplicate_choice"] = None
            request.session["ai_pending_duplicate_info"] = None
            request.session["ai_expecting_duplicate_choice"] = False
            request.session.modified = True
            reset_pending(request)
            return message("ℹ All requested levels already have sheets — nothing new to create.")

    view_req = {
        "command": "create_view",
        "view_type": request.session.get("ai_pending_view_type"),
        "levels": view_levels,
        "stage": request.session.get("ai_pending_stage"),
        "template": request.session.get("ai_pending_template"),
        "scope_box_id": final_scope_box
    }

    view_naming = apply(view_req, views_cache, sheets_cache)
    created_views = view_naming.get("views", [])

    if not created_views:
        reset_pending(request)
        return message("Error: View naming failed.")

    # 3) EXECUTION - Sheet Naming
    sheet_req = {
        "command": "create_sheet",
        "sheet_category": request.session.get("ai_pending_sheet_category"),
        "stage": request.session.get("ai_pending_stage"),
        "titleblock_raw": request.session.get("ai_pending_titleblock"),
        "titleblock_family": request.session.get("titleblock_family"),
        "titleblock_type": request.session.get("titleblock_type"),
        "view_type": request.session.get("ai_pending_view_type"),
        "levels": view_levels,
        # Project-wide level inventory for ROOF/TOP slot computation.
        "project_levels": request.session.get("ai_last_known_levels", []),
    }

    skipped_subpart_levels = []

    if duplicate_choice == "subparts" and duplicate_levels_set:
        # Allocate sub-parts for the duplicate levels and primaries for
        # the rest. Both lists are merged below; pairing with views uses
        # the _level tag on each sheet payload (added in naming_engine).
        cat0 = primary_cat.split(",")[0].strip() if "," in primary_cat else primary_cat
        sub_payloads, sub_skipped = build_subpart_sheets(
            cat0,
            [d for d in duplicate_info if d["level"] in duplicate_levels_set],
            list(sheets_cache),  # pass a copy so the helper mutates it locally
            scheme=scheme,
            stage=request.session.get("ai_pending_stage"),
            titleblock_family=request.session.get("titleblock_family"),
            titleblock_type=request.session.get("titleblock_type"),
        )
        skipped_subpart_levels.extend(sub_skipped)
        # Add the sub-part numbers to sheets_cache so build_sheets_payload
        # treats them as taken when allocating primaries.
        for p in sub_payloads:
            sheets_cache.append(p.get("sheet_number"))

        # Non-duplicate levels go through the regular allocator. If EVERY
        # input level was a duplicate, skip build_sheets_payload entirely —
        # otherwise the level-based path would fall through to the generic
        # batch-count path and create a spurious extra primary sheet.
        non_dup_levels = [l for l in pending_levels if l not in duplicate_levels_set]
        if non_dup_levels:
            sheet_req["levels"] = non_dup_levels
            primary_sheets = build_sheets_payload(sheet_req, sheets_cache, scheme=scheme)
        else:
            primary_sheets = []
        created_sheets = sub_payloads + primary_sheets
        # Filter views down to the levels we actually emitted sheets for —
        # build_subpart_sheets may have skipped basements; their views
        # would have nothing to pair with.
        emitted_levels = {p.get("_level") for p in created_sheets}
        created_views = [v for v in created_views if v.get("level") in emitted_levels]
    else:
        created_sheets = build_sheets_payload(sheet_req, sheets_cache, scheme=scheme)

    if not created_sheets:
        reset_pending(request)
        return message("Error: Sheet naming failed.")

    # 4) CONSTRUCT BATCH ENVELOPE
    steps = []

    # 🆕 Retrieve Alignment Data
    align_mode = request.session.get("ai_pending_alignment_mode", "CENTER")
    ref_sheet = request.session.get("ai_pending_reference_sheet")

    # Pair views with sheets BY LEVEL IDENTITY using the `_level` tag both
    # build_sheets_payload and build_subpart_sheets attach to every payload.
    # Falling back to index alignment was the source of the SITE-with-TOP-
    # view mismatch in the duplicate-handling rollout, so we avoid it here.
    sheets_by_level = {s.get("_level"): s for s in created_sheets if s.get("_level")}
    fallback_pool = [s for s in created_sheets if not s.get("_level")]
    pairs = []
    for view_item in created_views:
        sheet_item = sheets_by_level.pop(view_item.get("level"), None)
        if sheet_item is None and fallback_pool:
            sheet_item = fallback_pool.pop(0)
        if sheet_item is not None:
            pairs.append((view_item, sheet_item))
    pairs.sort(key=lambda p: sort_key_sheet_number(p[1].get("sheet_number")))

    for view_item, sheet_item in pairs:
        if final_scope_box: view_item["scope_box_id"] = final_scope_box

        steps.append(envelope_create_views([view_item]))
        steps.append(envelope_create_sheets([sheet_item]))

        # 🆕 Enhanced Placement Logic
        place_payload = envelope_place_view_on_sheet(view_item["name"], sheet_item["sheet_number"])
        place_payload["placement"] = align_mode
        if align_mode == "MATCH" and ref_sheet:
            place_payload["reference_sheet"] = ref_sheet

        steps.append(place_payload)

    # 5) CONSTRUCT SUCCESS MESSAGE
    msg_lines = []
    for view_item, sheet_item in pairs:
        line = f"• Sheet {sheet_item['sheet_number']} - {sheet_item['sheet_name']} with {view_item['name']}"
        msg_lines.append(line)

    if len(created_views) == 1:
        final_msg = msg_lines[0] + " is created successfully in your design stage."
    else:
        final_msg = "\n".join(msg_lines) + "\nare created successfully in your design stage."
        
    # Append Alignment Note
    if align_mode == "MATCH":
        final_msg += f"\n(Aligned to reference sheet: {ref_sheet})"

    # Append a note for any duplicate levels that couldn't be allocated as
    # sub-parts (basements in iso19650 schemes have no sub-slot room).
    if skipped_subpart_levels:
        final_msg += "\n\nℹ Skipped these levels (no sub-slot room in this scheme — try Manual Edit):"
        for s in skipped_subpart_levels:
            final_msg += f"\n  • {s.get('level')}: {s.get('reason')}"

    # Clear duplicate-handling pending state now that the choice is applied.
    request.session["ai_duplicate_choice"] = None
    request.session["ai_pending_duplicate_info"] = None
    request.session["ai_expecting_duplicate_choice"] = False

    multi_command_env = {
        "command": "execute_batch", 
        "steps": steps,
        "session_key": request.session.session_key,
        "message_override_data": {
            "view_name": "", 
            "sheet_number": "", 
            "sheet_name": final_msg, 
            "force_full_message": True 
        }
    }

    # Cleanup
    request.session["ai_last_known_views"] = [] 
    request.session["ai_last_known_sheets"] = [] 
    request.session.modified = True
    reset_pending(request)

    return Response({
        "message": "", 
        "revit_command": multi_command_env
    })

def save_pending_state(request, action_tag):
    """Helper to save state before a cache request."""
    request.session["ai_pending_request_data"] = {
        "intent": request.session.get("ai_pending_intent"),
        "last_action": action_tag,
        "view_type": request.session.get("ai_pending_view_type"), 
        "levels": request.session.get("ai_pending_levels_parsed"), 
        "stage": request.session.get("ai_pending_stage"), 
        "template": request.session.get("ai_pending_template"),
        "sheet_category": request.session.get("ai_pending_sheet_category"),
        "titleblock": request.session.get("ai_pending_titleblock"),
        "scope_box_id": request.session.get("ai_pending_scope_box_id"),
        # 🆕 PERSIST ALIGNMENT SETTINGS
        "alignment_mode": request.session.get("ai_pending_alignment_mode"),
        "reference_sheet": request.session.get("ai_pending_reference_sheet")
    }
    request.session.modified = True