# ai_router/ai_commands/modifier.py

import re
from rest_framework.response import Response
from ..ai_core.session_manager import reset_pending, debug_session, ask_for_missing_info
from ..ai_utils.envelope_builder import (
    send_envelope, 
    envelope_rename_view, 
    envelope_rename_sheet,
    envelope_duplicate_view, 
    envelope_apply_template,
    envelope_place_view_on_sheet, 
    envelope_remove_view_from_sheet
)

def message(text): return Response({"message": text})

def finalize_modification_command(request):
    intent = request.session.get("ai_pending_intent")
    target_raw = request.session.get("ai_pending_rename_target")
    value_raw = request.session.get("ai_pending_rename_value")
    
    debug_session(request, f"MODIFIER | Intent: {intent} | Target: {target_raw} | Value: {value_raw}")

    # =================================================================
    # 1. RENAME SHEET (Range & Smart Renumbering)
    # =================================================================
    if intent == "rename_sheet":
        
        # 💥 STRATEGY A: RANGE DETECTION ("Renumber A9.01-A9.04 to A9.05")
        is_range_request = False
        start_num_destination = None

        # 1. Check Keywords
        if target_raw and any(k in target_raw.lower() for k in ["start", "from", "renumber"]):
            is_range_request = True
        
        # 2. Check Regex (Strict: Must contain digits to avoid matching "Change sheet to X")
        # Matches: "A101-A105", "A101 to A105", "101-105"
        if target_raw:
             # Look for: (Word+Digit) space/dash/to (Word+Digit)
             range_pattern = r"[A-Z\.]*\d+[A-Z\.]*\s*[-to]+\s*[A-Z\.]*\d+[A-Z\.]*"
             if re.search(range_pattern, target_raw, re.IGNORECASE):
                 is_range_request = True

        if is_range_request:
            # We need to find the DESTINATION Start Number.
            search_text = (value_raw or "") + " " + (target_raw or "")
            match = re.search(r"([A-Z0-9]+[\.\-]?\d+(?:\.\d+)?)", value_raw or "", re.IGNORECASE)
            
            if not match:
                match = re.search(r"(?:start|from|to)\s*([A-Z0-9]+[\.\-]?\d+(?:\.\d+)?)", target_raw or "", re.IGNORECASE)

            if match:
                start_num_destination = match.group(1).upper()
                
                # 🛡️ A49 STANDARD SAFEGUARD
                if start_num_destination.startswith("A1") or start_num_destination.startswith("A5"):
                    if not request.session.get("ai_renumber_confirmed"):
                        request.session["ai_expecting_renumber_confirmation"] = True
                        request.session.modified = True
                        return Response({
                            "message": (
                                f"⚠️ **Standard Warning**: A49 Standards link {start_num_destination[:2]} sheets directly to floor levels.\n"
                                "Renumbering them will break this correlation.\n\n"
                                "**Are you sure you want to proceed?** (Reply 'Yes' or 'No')"
                            )
                        })

                request.session["ai_expecting_renumber_confirmation"] = False
                reset_pending(request)
                
                return send_envelope(request, {
                    "command": "renumber_sheets",
                    "start_number": start_num_destination,
                    "sheet_set": target_raw
                })
            else:
                # Only show error if we are SURE it's a range request
                return message("I see you want to renumber a range, but I couldn't figure out the **Start Number**. (e.g., 'to A9.05')")

        # 💥 STRATEGY B: SIMPLE RENAME (Singular)
        if not target_raw: return ask_for_missing_info(request, "rename_sheet")
        if not value_raw: return message("What should the new Number or Name be?")
        
        clean_val = value_raw.replace("sheet", "").replace("Sheet", "").strip()
        reset_pending(request)
        return send_envelope(request, envelope_rename_sheet(target_raw, clean_val))

    # =================================================================
    # 2. RENAME VIEW (Batch, Title, & Singular)
    # =================================================================
    if intent == "rename_view":
        
        # 💥 STRATEGY A: SET TITLE ON SHEET ("Change title to 'ผังพื้นชั้นที่ 1'")
        if target_raw and "title" in target_raw.lower():
             # 1. EXTRACT NEW TITLE
             final_title = value_raw
             if not final_title:
                 # 💥 IMPROVED REGEX:
                 # 1. Matches ANY character (including Thai) using .+?
                 # 2. Tolerates trailing spaces with \s*
                 # 3. Handles ' or " quotes
                 match = re.search(r"(?:to|as)\s+['\"]?(.+?)['\"]?\s*$", target_raw, re.IGNORECASE)
                 if match:
                     final_title = match.group(1)
            
             if not final_title:
                 return message("I understood you want to change the Title, but I couldn't catch the new name.")

             reset_pending(request)
             return send_envelope(request, {
                 "command": "batch_rename_views",
                 "strategy": "set_title_on_sheet",
                 "replace": final_title,
                 "target": "ACTIVE_VIEW"
             })

        # 💥 STRATEGY B: SYNC
        if target_raw and "all" in target_raw.lower() and "sheet" in target_raw.lower():
             target_sheet = None
             sheet_match = re.search(r"sheet\s+([A-Z0-9\-\.]+)", target_raw, re.IGNORECASE)
             if sheet_match: target_sheet = sheet_match.group(1).upper()

             reset_pending(request)
             return send_envelope(request, {
                 "command": "batch_rename_views", 
                 "strategy": "match_titleblock",
                 "target": target_sheet 
             })

        # 💥 STRATEGY C: FIND & REPLACE
        if target_raw and value_raw and ("all" in target_raw.lower() or "views" in target_raw.lower()):
             find_str = target_raw.replace("all views", "").replace("change", "").replace(" in ", " ").strip()
             find_str = find_str.replace("'", "").replace('"', "")
             
             reset_pending(request)
             return send_envelope(request, {
                 "command": "batch_rename_views",
                 "strategy": "FIND_REPLACE",
                 "find": find_str,
                 "replace": value_raw
             })

        # 💥 STRATEGY D: SIMPLE RENAME
        if not target_raw: return ask_for_missing_info(request, "rename_view")
        if not value_raw: return message("What is the new name?")
        
        reset_pending(request)
        return send_envelope(request, envelope_rename_view(target_raw, value_raw))

    # =================================================================
    # 3. DUPLICATE VIEW
    # =================================================================
    if intent == "duplicate_view":
        tgt = request.session.get("ai_pending_rename_target")
        mode = request.session.get("ai_pending_duplicate_mode") or "DEPENDENT"
        reset_pending(request)
        return send_envelope(request, envelope_duplicate_view(tgt, mode.upper()))

    # =================================================================
    # 4. APPLY TEMPLATE
    # =================================================================
    if intent == "apply_template":
        tgt = request.session.get("ai_pending_rename_target")
        tpl = request.session.get("ai_pending_template")
        if not tpl: return message("Which view template should I apply?")
        reset_pending(request)
        return send_envelope(request, envelope_apply_template(tgt, tpl))

    # =================================================================
    # 5. PLACEMENT
    # =================================================================
    if intent == "place_view_on_sheet":
        tgt = request.session.get("ai_pending_rename_target")
        sht = request.session.get("ai_pending_target_sheet")
        if not sht: return message("Which sheet should I place it on?")
        reset_pending(request)
        return send_envelope(request, envelope_place_view_on_sheet(tgt, sht))
    
    if intent == "remove_view_from_sheet":
        tgt = request.session.get("ai_pending_rename_target")
        sht = request.session.get("ai_pending_target_sheet")
        reset_pending(request)
        return send_envelope(request, envelope_remove_view_from_sheet(tgt, sht))

    return Response({"message": f"Intent '{intent}' not handled by modifier."})