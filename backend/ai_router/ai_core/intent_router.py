# ai_router/ai_core/intent_router.py
# Extracted from views.py — finalize_router, immediate commands, and GPT dispatch

import re
from rest_framework.response import Response
from django.http import JsonResponse

from .session_manager import (
    debug_session, reset_pending,
    ask_for_missing_info, check_template_requirements, has_minimum_requirements
)
from .gpt_integration import build_prompt, route_gpt_fields, fast_route_intent
from ..ai_utils.envelope_builder import send_envelope
from ..ai_utils.validators import validate_view_support
from ..ai_engines.naming_engine import get_view_abbrev
from ..ai_engines.titleblock_engine import get_smart_titleblock_options
from ..ai_engines.conversation_engine import get_fallback_response
from ..ai_commands.preflight import handle_preflight_check
from ..ai_commands.insert_standard_details import handle_insert_standard_details
from ..ai_commands.comment import handle_send_comment
from ..ai_commands.automate_tag import handle_automate_tag
from ..ai_commands.automate_tag_nlp import handle_automate_tag_nlp, handle_nlp_tag_conversation, resume_pending_nlp_tag
from ..ai_commands.automate_dim import handle_automate_dim


# =====================================================================
# THE ROUTER (TRAFFIC COP)
# =====================================================================

def finalize_router(request):
    from ..ai_commands.view_creator import finalize_create_views
    from ..ai_commands.sheet_creator import finalize_create_sheets
    from ..ai_commands.batch_processor import finalize_create_and_place
    from ..ai_commands.modifier import finalize_modification_command

    intent = request.session.get("ai_pending_intent")

    # INTENT UPGRADE
    raw_msg = request.data.get("message", "").lower()
    if intent == "create_view" and ("place" in raw_msg and "sheet" in raw_msg):
        debug_session(request, "🚀 Upgrading Intent via Router: create_view -> create_and_place")
        intent = "create_and_place"
        request.session["ai_pending_intent"] = intent
        request.session.modified = True

    # WIZARD FLOW
    if intent == "create_view_and_sheet_wizard":
        if not request.session.get("ai_pending_view_type"):
            return Response({"message": "Wizard: What type of view would you like to create? (e.g. Floor Plan, RCP)"})
        
        vtype = request.session.get("ai_pending_view_type")
        abbrev = get_view_abbrev(vtype)
        if abbrev not in ["D1", "SC", "AD"] and not request.session.get("ai_pending_levels_parsed"):
            return ask_for_missing_info(request, "create_view")
            
        if not request.session.get("ai_pending_stage"): return ask_for_missing_info(request, "create_view")
        if not request.session.get("ai_pending_template"):
             tpl_resp = check_template_requirements(request, "create_view")
             if tpl_resp: return tpl_resp

        if not request.session.get("ai_pending_sheet_category"):
            request.session["ai_pending_sheet_category"] = "A1"
            request.session.modified = True
            
        if not request.session.get("ai_pending_titleblock"):
             cat = request.session.get("ai_pending_sheet_category")
             options = get_smart_titleblock_options(cat)
             opt_str = "\n".join([f"{i+1}. {opt}" for i, opt in enumerate(options)])
             msg = f"Wizard: Which titleblock should I use?\n{opt_str}"
             request.session["ai_expecting_titleblock_selection"] = True
             request.session["ai_pending_titleblock_options"] = options
             request.session.modified = True
             return JsonResponse({"message": msg, "options": options, "status": "success"})

        align_mode = request.session.get("ai_pending_alignment_mode")
        if not align_mode:
            msg = "Wizard: How should I align these views on the sheets?"
            options = ["Center of Sheet", "Match another Sheet (Reference)"]
            request.session["ai_expecting_alignment_selection"] = True
            request.session.modified = True
            return JsonResponse({"message": msg + "\n1. Center\n2. Match Reference", "options": options, "status": "success"})

        if align_mode == "MATCH" and not request.session.get("ai_pending_reference_sheet"):
             return Response({"message": "Which sheet should I use as a reference? (e.g. A1.01)"})

        request.session["ai_pending_intent"] = "create_and_place"
        request.session.modified = True
        return finalize_router(request)

    # 1. CREATE VIEW
    if intent == "create_view":
        support_error = validate_view_support(request)
        if support_error: return support_error
        
        # 💥 FIX: STRICT STAGE CHECK
        if not request.session.get("ai_pending_stage"):
             return ask_for_missing_info(request, "create_view")
        
        if not has_minimum_requirements(request, intent):
             return ask_for_missing_info(request, intent)

        tpl_resp = check_template_requirements(request, intent)
        if tpl_resp: return tpl_resp

        # Scope Box Check
        vtype = request.session.get("ai_pending_view_type")
        abbrev = get_view_abbrev(vtype)
        is_drafting = abbrev in ["D1", "SC", "AD", "AW"]
        if not is_drafting:
            has_cached = bool(request.session.get("ai_last_known_scope_boxes"))
            is_checked = request.session.get("ai_scope_box_checked")
            has_pending = bool(request.session.get("ai_pending_scope_box_id"))

            if not is_checked and not has_cached and not has_pending:
                request.session["ai_pending_request_data"] = {
                    "intent": intent, "view_type": vtype,
                    "levels": request.session.get("ai_pending_levels_parsed"),
                    "stage": request.session.get("ai_pending_stage"),
                    "template": request.session.get("ai_pending_template"),
                    "scope_box_id": request.session.get("ai_pending_scope_box_id")
                }
                request.session.modified = True
                return send_envelope(request, {"command": "list_scope_boxes"})
            
            if has_cached and not has_pending: return ask_for_missing_info(request, intent)
        
        return finalize_create_views(request)

    # 2. CREATE SHEET
    elif intent in ["create_sheet", "batch_create"]:
        return finalize_create_sheets(request)

    # 3. CREATE & PLACE
    elif intent == "create_and_place":
        support_error = validate_view_support(request)
        if support_error: return support_error

        if not request.session.get("ai_pending_sheet_category") and request.session.get("ai_pending_view_type"):
            vt = request.session.get("ai_pending_view_type").lower()
            inferred = "A1"
            if "ceiling" in vt: inferred = "A5"
            elif "elevation" in vt: inferred = "A2"
            elif "detail" in vt: inferred = "A9"
            elif "section" in vt: inferred = "A3"
            request.session["ai_pending_sheet_category"] = inferred
            request.session.modified = True

        reqs = ["ai_pending_view_type", "ai_pending_levels_parsed", "ai_pending_sheet_category", "ai_pending_stage"]
        vtype_check = request.session.get("ai_pending_view_type")
        abbrev_check = get_view_abbrev(vtype_check) if vtype_check else None
        is_batch_req = abbrev_check in ["D1", "SC", "AD", "AW"]

        if not all(request.session.get(k) for k in reqs):
            if not request.session.get("ai_pending_view_type"): return ask_for_missing_info(request, "create_view")
            if not is_batch_req and not request.session.get("ai_pending_levels_parsed"): return ask_for_missing_info(request, "create_view")
            if not request.session.get("ai_pending_stage"): return ask_for_missing_info(request, "create_view")
            if not request.session.get("ai_pending_sheet_category"): return ask_for_missing_info(request, "create_sheet")

        tpl_resp = check_template_requirements(request, "create_view")
        if tpl_resp: return tpl_resp
        
        if not is_batch_req and not request.session.get("ai_pending_titleblock"):
             cat = request.session.get("ai_pending_sheet_category", "A1").upper()
             options = get_smart_titleblock_options(cat)
             opt_str = "\n".join([f"{i+1}. {opt}" for i, opt in enumerate(options)])
             msg = f"Please choose a titleblock for this {cat} sheet:\n{opt_str}"
             request.session["ai_expecting_titleblock_selection"] = True
             request.session["ai_pending_titleblock_options"] = options
             request.session.modified = True
             return JsonResponse({"message": msg, "options": options, "status": "success"})

        if not is_batch_req:
             if not request.session.get("ai_scope_box_checked") and not request.session.get("ai_pending_scope_box_id"):
                 request.session["ai_pending_request_data"] = {
                    "intent": intent,
                    "view_type": request.session.get("ai_pending_view_type"),
                    "levels": request.session.get("ai_pending_levels_parsed"),
                    "stage": request.session.get("ai_pending_stage"),
                    "template": request.session.get("ai_pending_template"),
                    "sheet_category": request.session.get("ai_pending_sheet_category"),
                    "titleblock": request.session.get("ai_pending_titleblock"),
                    "alignment_mode": request.session.get("ai_pending_alignment_mode"),
                    "reference_sheet": request.session.get("ai_pending_reference_sheet")
                 }
                 request.session.modified = True
                 return send_envelope(request, {"command": "list_scope_boxes"})

             current_choice = request.session.get("ai_pending_scope_box_id")
             cached_boxes = request.session.get("ai_last_known_scope_boxes")
             if cached_boxes and current_choice is None:
                  from ..ai_engines.scope_box_engine import format_scope_boxes_for_chat
                  msg = format_scope_boxes_for_chat(cached_boxes)
                  return Response({"message": msg}) 

        align_mode = request.session.get("ai_pending_alignment_mode")
        ref_sheet = request.session.get("ai_pending_reference_sheet")
        
        if not align_mode:
            request.session["ai_expecting_alignment_selection"] = True 
            request.session.modified = True
            return JsonResponse({
                "message": "How should the views be aligned on the sheets?\n1. Center of Sheet\n2. Match Reference",
                "options": ["Center", "Match Reference"],
                "status": "success"
            })

        if align_mode == "MATCH" and not ref_sheet:
            request.session["ai_expecting_reference_sheet"] = True 
            request.session.modified = True
            return Response({"message": "Please provide a reference Sheet Number (e.g. A1.01)."})

        if align_mode == "MATCH" and ref_sheet:
            cached_sheets = request.session.get("ai_last_known_sheets") or []
            if not cached_sheets:
                request.session["ai_pending_request_data"] = {
                    "intent": intent, 
                    "alignment_mode": "MATCH", 
                    "reference_sheet": ref_sheet,
                    "last_action": "validating_ref_sheet" 
                }
                request.session.modified = True
                return send_envelope(request, {"command": "list_sheets"})

            clean_ref = ref_sheet.strip().upper()
            found = any(str(s.get("number") if isinstance(s, dict) else s).split(" - ")[0].strip().upper() == clean_ref for s in cached_sheets)
            if not found:
                request.session["ai_pending_reference_sheet"] = None
                request.session.modified = True
                return Response({"message": f"⚠️ Sheet '{ref_sheet}' not found. Please re-enter."})

        return finalize_create_and_place(request)

    elif intent in ["rename_view", "rename_sheet", "duplicate_view", "apply_template", "place_view_on_sheet", "remove_view_from_sheet"]:
        return finalize_modification_command(request)

    # 💥 THE CLEANED UP FALLBACK
    if not intent: 
        return Response({"message": get_fallback_response()})
        
    return ask_for_missing_info(request, intent)


# =====================================================================
# IMMEDIATE COMMAND DISPATCHER
# =====================================================================

ALLOWED_IMMEDIATE_COMMANDS = [
    "list_views", 
    "list_sheets", 
    "list_scope_boxes", 
    "list_views_on_sheet", 
    "fetch_project_inventory", 
    "execute_batch_update",
    "preflight_check",
    "insert_standard_details",
    "send_comment",
    "refresh_project_info",
    "automate_tag",
    "automate_tag_nlp",
    "automate_dim",
    "cache_dim_inventory",
    "cache_tag_inventory",
    "cache_level_inventory",
    "ui:help",
    "wizard:create_views",
    "wizard:create_sheets",
    "wizard:create_and_place",
    "wizard:room_elevations",
    "wizard:automate_tag",
    "wizard:automate_dim",
    "wizard:insert_standard_details",
    "start_interactive_room_package"
]

def dispatch_immediate_command(request, intent, gpt_json):
    """
    Handles immediate commands that bypass the finalize_router flow.
    
    Returns:
        Response if handled, None if not an immediate command.
    """
    if intent not in ALLOWED_IMMEDIATE_COMMANDS:
        return None

    # Set Flag for lists
    if intent.startswith("list_"):
        request.session["ai_list_mode"] = True
        request.session.modified = True

    # 💥 PREFLIGHT CHECK
    if intent == "preflight_check":
        return handle_preflight_check(request)

    # 💥 INSERT STANDARD DETAILS (wizard for A49 standard / EIA detail packages)
    if intent == "insert_standard_details":
        return handle_insert_standard_details(request)

    # 💥 SEND COMMENT (Help > Comment form → SMTP email)
    if intent == "send_comment":
        return handle_send_comment(request)

    # 💥 REFRESH PROJECT INFO — conversational cache refresh.
    # Returns a fetch_project_info revit_command + a confirmation message.
    # The frontend's existing useChat plumbing routes the revit_command to
    # Revit, the response goes through useRevitHandler → updateWizardProps,
    # which re-fires cache_level_inventory (and the tag/dim caches).
    if intent == "refresh_project_info":
        env = {"command": "fetch_project_info"}
        if request.session.session_key:
            env["session_key"] = request.session.session_key
        return Response({
            "message": "🔄 Refreshing project info from Revit...",
            "revit_command": env
        })

    # 💥 AUTOMATE TAG (unified tagging - doors, windows, walls, rooms, ceilings)
    if intent == "automate_tag":
        return handle_automate_tag(request)

    # 💥 AUTOMATE TAG NLP (natural language tagging - "tag doors in CD floor plans")
    if intent == "automate_tag_nlp":
        return handle_automate_tag_nlp(request, gpt_json)
    
    # 💥 AUTOMATE DIM (wizard payload — direct execution)
    if intent == "automate_dim":
        return handle_automate_dim(request)

    # 💥 AUTOMATE DIM WIZARD — clear any stale NLP dim state before opening,
    # then fall through to the generic envelope dispatch below so the response
    # uses the {revit_command: {command: "wizard:..."}} shape that the frontend
    # opens wizards from. Returning {message, intent} alone never opens it.
    if intent == "wizard:automate_dim":
        request.session.pop("ai_nlp_dim_state", None)
        request.session.modified = True
        # Don't return — let the generic fallthrough below build the envelope.

    # 💥 CACHE DIM INVENTORY (silent — caches floor plan views in session for NLP)
    if intent == "cache_dim_inventory":
        request.session["ai_last_known_floor_plan_views"] = request.data.get("floor_plan_views", [])
        request.session.modified = True
        debug_session(request, f"📦 Cached dim inventory: {len(request.data.get('floor_plan_views', []))} floor plan views")
        return Response({"message": "", "status": "silent"})

    # 💥 CACHE LEVEL INVENTORY (silent — caches actual project level names so
    # the level_matcher can resolve user input regardless of naming convention)
    if intent == "cache_level_inventory":
        levels = request.data.get("levels", []) or []
        request.session["ai_last_known_levels"] = levels
        request.session.modified = True
        debug_session(request, f"📦 Cached level inventory: {len(levels)} levels")
        return Response({"message": "", "status": "silent"})

    # 💥 CACHE TAG INVENTORY (silent — caches tag families + views in session for NLP)
    if intent == "cache_tag_inventory":
        request.session["ai_last_known_taggable_views"] = request.data.get("taggable_views", [])
        request.session["ai_last_known_door_tags"] = request.data.get("door_tags", [])
        request.session["ai_last_known_window_tags"] = request.data.get("window_tags", [])
        request.session["ai_last_known_wall_tags"] = request.data.get("wall_tags", [])
        request.session["ai_last_known_room_tags"] = request.data.get("room_tags", [])
        request.session["ai_last_known_ceiling_tags"] = request.data.get("ceiling_tags", [])
        request.session["ai_last_known_spot_elevation_tags"] = request.data.get("spot_elevation_tags", [])
        request.session.modified = True
        debug_session(request, f"📦 Cached tag inventory: {len(request.data.get('taggable_views', []))} views")
        
        # Auto-resume any pending NLP tag request that was waiting for this data
        resume_resp = resume_pending_nlp_tag(request)
        if resume_resp:
            return resume_resp
        
        return Response({"message": "", "status": "silent"})

    # 💥 INTERACTIVE ROOM PACKAGE
    if intent == "start_interactive_room_package":
        stage = gpt_json.get("stage_raw") or request.session.get("ai_pending_stage") or "CD"
        stage_upper = stage.upper()
        reset_pending(request)
        
        # Dynamic Template Routing based on Stage
        plan_tpl = "A49_DD_INTERIOR ENLARGED PLAN" if stage_upper == "DD" else "A49_CD_A6_INTERIOR ENLARGED PLAN"
        elev_tpl = "A49_DD_INTERIOR ELEVATION" if stage_upper == "DD" else "A49_CD_A6_INTERIOR ELEVATION"
        
        payload = {
            "command": "start_interactive_room_package",
            "message": f"Interactive Mode Started! Please click on the target room in your active Revit floor plan... (Stage: {stage_upper})",
            "raw": {
                "stage": stage_upper,
                "offset": 600,
                "plan_template": plan_tpl,
                "elev_template": elev_tpl,
                "create_sheets": True,
                "titleblock": "A49_TB_A1_Horizontal"
            }
        }
        
        return send_envelope(request, payload)

    # Generic envelope command
    payload = {"command": intent}

    # Smart Extract for list_views_on_sheet
    if intent == "list_views_on_sheet":
        raw_query = gpt_json.get("list_query_raw", "")
        match = re.search(r"(?:on|sheet)\s+([A-Z0-9]+[-._]?[A-Z0-9]+(?:\.[A-Z0-9]+)?)", raw_query, re.IGNORECASE)
        if not match:
            match = re.search(r"\b([A-Z0-9]{1,3}[-._]?[A-Z0-9]+(?:\.[A-Z0-9]+)?)\b", raw_query, re.IGNORECASE)
        if match:
            payload["sheet"] = match.group(1).upper()
    
    # Send immediately back to Vue/Revit
    return send_envelope(request, payload)


# =====================================================================
# MAIN PROCESSING ENTRY POINT
# =====================================================================

def process_intent(request, raw_text_original):
    """
    Runs GPT processing (fast route or OpenAI), routes fields into session,
    dispatches immediate commands, or falls through to finalize_router.

    Called from ai_router() after all interceptors and callbacks are handled.
    """
    # 💥 DEFENSIVE: clear any stale transient intent left in session before
    # we run classification on the new message. Transient intents (refresh,
    # send_comment, cache_*, automate_tag_nlp) are fire-and-forget and must
    # not carry over to the next user message — otherwise an ambiguous GPT
    # classification would land in finalize_router with the wrong intent.
    _STALE_TRANSIENTS = (
        "refresh_project_info",
        "send_comment",
        "cache_level_inventory", "cache_dim_inventory", "cache_tag_inventory",
        "automate_tag_nlp",
    )
    if request.session.get("ai_pending_intent") in _STALE_TRANSIENTS:
        debug_session(request, f"🧹 Cleared stale transient intent: {request.session.get('ai_pending_intent')}")
        request.session["ai_pending_intent"] = None
        request.session.modified = True

    # 💥 A. FAST ROUTE CHECK (0ms Latency)
    fast_json = fast_route_intent(raw_text_original)
    
    if fast_json:
        debug_session(request, f"⚡ FAST ROUTE HIT: {fast_json['intent']}")
        gpt_json = fast_json
    else:
        # 💥 B. SLOW ROUTE (OpenAI API)
        debug_session(request, "🧠 Routing to OpenAI...")
        prompt = build_prompt(raw_text_original)
        gpt_json = prompt.get("parsed", {})

    # 1️⃣ RUN THE ROUTER (updates session state based on intent)
    # SKIP route_gpt_fields for transient / silent commands — these shouldn't
    # mutate ai_pending_intent or other slot state. Without skipping, e.g.
    # typing "refresh" sets ai_pending_intent="refresh_project_info" which
    # then leaks into the user's next command if GPT classification is ambiguous.
    intent = gpt_json.get("intent")
    TRANSIENT_INTENTS = (
        "automate_tag_nlp",
        "cache_tag_inventory", "cache_dim_inventory", "cache_level_inventory",
        "refresh_project_info",
        "send_comment",
    )
    if intent not in TRANSIENT_INTENTS:
        route_gpt_fields(request, gpt_json)
        # Re-grab intent AFTER the router runs (it may have changed it)
        intent = gpt_json.get("intent")

    # 3️⃣ CHECK IMMEDIATE COMMANDS
    immediate_resp = dispatch_immediate_command(request, intent, gpt_json)
    if immediate_resp:
        return immediate_resp

    # 4️⃣ FALL THROUGH TO FINALIZE ROUTER
    return finalize_router(request)
