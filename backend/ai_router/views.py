# =====================================================================
# Vella Backend (views.py) — The "Traffic Cop"
# ai_router/views.py
# =====================================================================

import re, sys, io, importlib, json
from rest_framework.decorators import api_view
from rest_framework.response import Response
from rest_framework import status
from django.views.decorators.csrf import csrf_exempt
from django.conf import settings
from django.http import JsonResponse
from .auth import require_azure_token

# Force UTF-8 encoding
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8', errors='replace')

# ---------------------------------------------------------------------
# MODULE IMPORTS
# ---------------------------------------------------------------------
from .ai_core.session_manager import (
    initialize_session, debug_session, reset_session_completely, reset_pending,
    ask_for_missing_info, check_template_requirements, has_minimum_requirements
)
from .ai_core.gpt_integration import (
    build_prompt, route_gpt_fields, fast_route_intent
)
from .ai_utils.envelope_builder import send_envelope, envelope_preflight_check, envelope_preflight_repair
from .ai_utils.formatters import (
    format_views_for_display, format_sheets_for_display,
    normalize_view_list, normalize_sheet_list,
    update_last_known_views, update_last_known_sheets
)
from .ai_utils.validators import validate_view_support

from .ai_engines.template_engine import get_abbrev
from .ai_engines.naming_engine import normalize_sheet_type, get_view_abbrev
from .ai_engines.titleblock_engine import get_smart_titleblock_options
from .ai_engines.math_engine import process_math_and_conversions
from .ai_engines.conversation_engine import process_conversational_intent, get_fallback_response

engine = importlib.import_module(settings.SESSION_ENGINE)


# =====================================================================
# THE ROUTER (TRAFFIC COP)
# =====================================================================

def finalize_router(request):
    from .ai_commands.view_creator import finalize_create_views
    from .ai_commands.sheet_creator import finalize_create_sheets
    from .ai_commands.batch_processor import finalize_create_and_place
    from .ai_commands.modifier import finalize_modification_command

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
        # We explicitly check for Stage before anything else.
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
                  from .ai_engines.scope_box_engine import format_scope_boxes_for_chat
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
# MAIN API ENTRYPOINT
# =====================================================================

@csrf_exempt
@api_view(['POST'])
@require_azure_token
def ai_router(request):
    passed_session_key = request.data.get("session_key")
    if passed_session_key and request.session.session_key != passed_session_key:
        from django.contrib.sessions.models import Session
        if Session.objects.filter(session_key=passed_session_key).exists():
            request.session = engine.SessionStore(session_key=passed_session_key)
            debug_session(request, f"🔄 Session recovered: {passed_session_key[:8]}")

    initialize_session(request)
    if not request.session.session_key: request.session.create()

    try:
        raw_text_original = request.data.get("message", "").strip()
        raw_text_lower = raw_text_original.lower()
        
        # ==========================================================
        # 0. CONFIRMATION INTERCEPTOR (Safeguard)
        # ==========================================================
        if request.session.get("ai_expecting_renumber_confirmation"):
            if raw_text_lower in ["yes", "y", "confirm", "proceed", "sure", "ok"]:
                debug_session(request, "🛡️ Safeguard: User CONFIRMED renumbering.")
                request.session["ai_renumber_confirmed"] = True
                request.session["ai_expecting_renumber_confirmation"] = False
                request.session.modified = True
                from .ai_commands.modifier import finalize_modification_command
                return finalize_modification_command(request)
            
            elif raw_text_lower in ["no", "n", "cancel", "stop", "don't"]:
                debug_session(request, "🛡️ Safeguard: User CANCELLED renumbering.")
                reset_pending(request)
                request.session["ai_expecting_renumber_confirmation"] = False
                return Response({"message": "❌ Cancelled. The sheets were not renumbered."})
        
        # 💥 CLEAN TEXT (used by interceptors below)
        clean_text = raw_text_lower.strip(".!, ")

        # ==========================================================
        # 0.6) PREFLIGHT REPAIR INTERCEPTOR
        # ==========================================================
        if request.session.get("ai_expecting_preflight_repair"):
            if clean_text in ["yes", "y", "confirm", "fix", "repair", "proceed", "sure", "ok"]:
                debug_session(request, "🔧 User confirmed Preflight Repair.")
                request.session["ai_expecting_preflight_repair"] = False
                request.session.modified = True
                
                standards_data = request.session.get("ai_preflight_standards")
                if not standards_data:
                    return Response({"message": "❌ No preflight data found. Please run Preflight Check again."})
                
                env = envelope_preflight_repair(standards_data, request.data.get("preflight_result"))
                return send_envelope(request, env)
            
            elif clean_text in ["no", "n", "cancel", "skip", "later", "not now"]:
                debug_session(request, "⏭️ User skipped Preflight Repair.")
                request.session["ai_expecting_preflight_repair"] = False
                request.session["ai_preflight_standards"] = None
                request.session.modified = True
                return Response({"message": "No problem. You can run Preflight Check again anytime."})

        # 💥 BULLETPROOF "NO" INTERCEPTOR
        if clean_text in ["no", "none", "skip", "n", "nope", "cancel"]:
            current_intent = request.session.get("ai_pending_intent")
            if current_intent in ["create_view", "create_and_place"]:
                request.session["ai_pending_scope_box_id"] = "SKIP"
                request.session.modified = True
                return finalize_router(request)
            
        # ==========================================================
        # 0.5) CONVERSATIONAL INTERCEPTOR
        # ==========================================================
        conv_response = process_conversational_intent(raw_text_lower, request)
        if conv_response:
            return conv_response
        
        # ==========================================================
        # 1) Handle Revit Callbacks (WITH LIST MODE RESTORED!)
        # ==========================================================
        data_keys = request.data.keys()
        
        if "list_views_result" in data_keys:
            raw_list = request.data.get("list_views_result", [])
            list_result = normalize_view_list(raw_list)
            update_last_known_views(request, list_result)
            
            # 💥 1. Internal Request?
            pending_data = request.session.get("ai_pending_request_data")
            if pending_data:
                for k, v in pending_data.items():
                    if k == "intent": request.session["ai_pending_intent"] = v
                    elif k == "view_type": request.session["ai_pending_view_type"] = v
                    elif k == "levels": request.session["ai_pending_levels_parsed"] = v
                    elif k == "stage": request.session["ai_pending_stage"] = v
                    elif k == "template": request.session["ai_pending_template"] = v
                    elif k == "sheet_category": request.session["ai_pending_sheet_category"] = v
                    elif k == "titleblock": request.session["ai_pending_titleblock"] = v
                    elif k == "alignment_mode": request.session["ai_pending_alignment_mode"] = v
                    elif k == "reference_sheet": request.session["ai_pending_reference_sheet"] = v
                    elif k == "scope_box_id": request.session["ai_pending_scope_box_id"] = v
                request.session.modified = True
                return finalize_router(request)
            
            # 💥 2. Explicit List Request?
            if request.session.get("ai_list_mode"):
                request.session["ai_list_mode"] = False
                return Response({"message": format_views_for_display(list_result), "done": True, "session_key": request.session.session_key})

            return Response({"message": "Views cached.", "session_key": request.session.session_key})

        if "list_sheets_result" in data_keys:
            raw_list = request.data.get("list_sheets_result", [])
            list_result = normalize_sheet_list(raw_list)
            update_last_known_sheets(request, list_result)
            
            # 💥 1. Internal Request?
            pending_data = request.session.get("ai_pending_request_data")
            if pending_data:
                request.session["ai_pending_intent"] = pending_data.get("intent")
                request.session["ai_pending_alignment_mode"] = pending_data.get("alignment_mode")
                request.session["ai_pending_reference_sheet"] = pending_data.get("reference_sheet")
                request.session.modified = True
                return finalize_router(request)
            
            # 💥 2. Explicit List Request?
            if request.session.get("ai_list_mode"):
                request.session["ai_list_mode"] = False
                return Response({"message": format_sheets_for_display(list_result), "done": True, "session_key": request.session.session_key})

            return Response({"message": "Sheets cached.", "session_key": request.session.session_key})

        if "list_scope_boxes_result" in data_keys:
            raw_result = request.data.get("list_scope_boxes_result")
            scope_boxes = []
            try:
                if isinstance(raw_result, str):
                    parsed = json.loads(raw_result)
                    if isinstance(parsed, dict): scope_boxes = parsed.get("scope_boxes", [])
                    elif isinstance(parsed, list): scope_boxes = parsed
                elif isinstance(raw_result, list): scope_boxes = raw_result
                elif isinstance(raw_result, dict): scope_boxes = raw_result.get("scope_boxes", [])
            except: scope_boxes = []

            request.session["ai_last_known_scope_boxes"] = scope_boxes
            request.session["ai_scope_box_checked"] = True
            request.session.modified = True
            
            # 💥 1. Internal Request?
            pending_data = request.session.get("ai_pending_request_data")
            if pending_data:
                request.session["ai_pending_intent"] = pending_data.get("intent")
                request.session["ai_pending_view_type"] = pending_data.get("view_type")
                request.session["ai_pending_levels_parsed"] = pending_data.get("levels")
                request.session["ai_pending_stage"] = pending_data.get("stage")
                request.session["ai_pending_template"] = pending_data.get("template")
                request.session["ai_pending_sheet_category"] = pending_data.get("sheet_category")
                request.session["ai_pending_titleblock"] = pending_data.get("titleblock")
                request.session["ai_pending_alignment_mode"] = pending_data.get("alignment_mode")
                request.session["ai_pending_reference_sheet"] = pending_data.get("reference_sheet")
                request.session.modified = True
                return finalize_router(request)
            
            # 💥 2. Explicit List Request?
            if request.session.get("ai_list_mode"):
                request.session["ai_list_mode"] = False
                msg = "**Available Scope Boxes:**\n" + "\n".join([f"• {sb['name']}" for sb in scope_boxes]) if scope_boxes else "No Scope Boxes found."
                return Response({"message": msg, "done": True, "session_key": request.session.session_key})

            return Response({"message": "Scope boxes cached.", "session_key": request.session.session_key})

        # ==========================================================
        # 3) NONE Short-Circuit
        # ==========================================================
        if raw_text_original.strip().upper() == "NONE":
            request.session["ai_pending_stage"] = "NONE"
            request.session.modified = True
            return finalize_router(request)
        
        # ==========================================================
        # 3.5) ARCHITECTURAL MATH & CONVERSION SHORT-CIRCUIT
        # ==========================================================
        math_response = process_math_and_conversions(raw_text_lower, request.session.session_key)
        if math_response:
            return math_response

        # ==========================================================
        # 4) GPT Processing (OPTIMIZED WITH FAST ROUTER)
        # ==========================================================

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

        # 1️⃣ RUN THE ROUTER FIRST (This updates session state based on the intent)
        route_gpt_fields(request, gpt_json)

        # 2️⃣ CRITICAL: GRAB THE INTENT *AFTER* THE ROUTER RUNS
        # If you grab it before, it will still be "unknown"!
        intent = gpt_json.get("intent") 

        # 3️⃣ NOW CHECK THE LIST
        allowed_immediate_commands = [
            "list_views", 
            "list_sheets", 
            "list_scope_boxes", 
            "list_views_on_sheet", 
            "fetch_project_inventory", 
            "execute_batch_update",
            "preflight_check",
            "ui:help",
            "wizard:create_views",
            "wizard:create_sheets",
            "wizard:create_and_place",
            "wizard:room_elevations",
            "start_interactive_room_package"
        ]

        if intent in allowed_immediate_commands:
            # Set Flag for lists
            if intent.startswith("list_"):
                request.session["ai_list_mode"] = True
                request.session.modified = True
            
            # 💥 PREFLIGHT CHECK
            if intent == "preflight_check":
                import os
                standards_path = os.path.join(
                    os.path.dirname(__file__), "standards", "standards.json"
                )
                try:
                    with open(standards_path, "r", encoding="utf-8") as f:
                        standards_data = json.load(f)
                except Exception as e:
                    return Response({"message": f"❌ Could not load standards.json: {e}"})
                
                # Store standards in session for repair flow
                request.session["ai_preflight_standards"] = standards_data
                request.session["ai_expecting_preflight_repair"] = True
                request.session.modified = True
                
                reset_pending(request)
                env = envelope_preflight_check(standards_data)
                return send_envelope(request, env)
            
            # 💥 THE NATIVE FIX: Let send_envelope handle the routing!
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

        return finalize_router(request)

    except Exception as ex:
        print(f"CRITICAL ERROR: {ex}")
        return Response({"error": f"Backend exception: {ex}"}, status=status.HTTP_500_INTERNAL_SERVER_ERROR)