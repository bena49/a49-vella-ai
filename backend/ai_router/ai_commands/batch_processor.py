# ai_router/ai_commands/batch_processor.py (UNLOCKED VERSION)

from rest_framework.response import Response
from ..ai_core.session_manager import debug_session, reset_pending
from ..ai_utils.envelope_builder import (
    send_envelope, envelope_create_views, envelope_create_sheets, envelope_place_view_on_sheet
)
from ..ai_engines.naming_engine import apply, build_sheets_payload, sort_key_sheet_number, resolve_scheme_for_request
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

    # 2) EXECUTION - View Naming
    scope_box = request.session.get("ai_pending_scope_box_id") 

    # TRANSLATOR LINE:
    final_scope_box = None if scope_box == "SKIP" else scope_box
    
    view_req = {
        "command": "create_view", 
        "view_type": request.session.get("ai_pending_view_type"),
        "levels": request.session.get("ai_pending_levels_parsed"),
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
    # Resolve numbering scheme for this project before building sheets.
    scheme = resolve_scheme_for_request(request)
    sheet_req = {
        "command": "create_sheet",
        "sheet_category": request.session.get("ai_pending_sheet_category"),
        "stage": request.session.get("ai_pending_stage"),
        "titleblock_raw": request.session.get("ai_pending_titleblock"),
        "titleblock_family": request.session.get("titleblock_family"),
        "titleblock_type": request.session.get("titleblock_type"),
        "view_type": request.session.get("ai_pending_view_type"),
        "levels": request.session.get("ai_pending_levels_parsed"),
        # Project-wide level inventory for ROOF/TOP slot computation.
        "project_levels": request.session.get("ai_last_known_levels", []),
    }

    created_sheets = build_sheets_payload(sheet_req, sheets_cache, scheme=scheme)
    
    if not created_sheets:
        reset_pending(request)
        return message("Error: Sheet naming failed.")

    # 4) CONSTRUCT BATCH ENVELOPE
    steps = []

    # 🆕 Retrieve Alignment Data
    align_mode = request.session.get("ai_pending_alignment_mode", "CENTER")
    ref_sheet = request.session.get("ai_pending_reference_sheet")

    # Pair views with sheets and sort the pair list by sheet number ascending
    # so both the create-order (in Revit) and the success-report (in chat)
    # read low → high.
    pairs = []
    for i, view_item in enumerate(created_views):
        sheet_item = created_sheets[i] if i < len(created_sheets) else created_sheets[0]
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