# ai_router/ai_core/session_manager.py

import importlib
from django.conf import settings
from rest_framework.response import Response

# Load the session engine defined in settings.py
engine = importlib.import_module(settings.SESSION_ENGINE)

def debug(text):
    """Simple console print wrapper."""
    print("DEBUG:", text)

def debug_session(request, text):
    """Debug session information with Session ID."""
    try:
        session_key = request.session.session_key
        debug(f"[Session:{session_key[:8] if session_key else 'None'}] {text}")
    except:
        debug(f"[Session:Unknown] {text}")

def initialize_session(request):
    """
    Ensures the current user's private session is ready.
    Sets all default keys if they don't exist.
    """
    if "ai_pending_intent" not in request.session:
        request.session.update({
            "ai_pending_intent": None,
            "ai_pending_view_type": None,
            "ai_pending_levels_raw": None,
            "ai_pending_levels_parsed": None,
            "ai_pending_stage": None,
            "ai_pending_template": None,
            "ai_pending_sheet_category": None,
            "ai_pending_titleblock": None,
            "ai_pending_user_provided_name": None,
            "ai_pending_batch_count": None,
            "ai_pending_rename_target": None,
            "ai_pending_rename_value": None,
            "ai_pending_target_sheet": None,
            "ai_pending_alignment_mode": None, # "CENTER" or "MATCH"
            "ai_pending_reference_sheet": None,
            "ai_last_known_sheets": [],
            "ai_last_known_views": [],
            "ai_pending_scope_box_id": None,
            "ai_last_known_scope_boxes": [],
            "ai_scope_box_checked": False, # Flag to prevent infinite fetching loops
            
            "pending_request_data": None
        })
        request.session.modified = True

def reset_pending(request):
    """
    Resets all 'pending' slots to None.
    Used after a command completes or when context switching.
    """
    keys = [
        "ai_pending_intent", "ai_pending_view_type", "ai_pending_levels_raw",
        "ai_pending_levels_parsed", "ai_pending_stage", "ai_pending_template",
        "ai_pending_sheet_category", "ai_pending_titleblock",
        "ai_pending_custom_mode", "ai_pending_sheet_type",
        "ai_pending_user_provided_name", "ai_pending_batch_count",
        "ai_pending_suffix_proposal", "ai_pending_suffix_base",
        "ai_pending_suffix_confirm_needed",
        "ai_pending_rename_target", "ai_pending_rename_value",
        "ai_pending_duplicate_mode", "ai_pending_target_sheet",
        "ai_pending_alignment_mode",
        "ai_pending_reference_sheet",
        "titleblock_family", "titleblock_type",
        "ai_pending_request_data",
        "ai_list_mode",
        
        "ai_pending_scope_box_id",
        "ai_scope_box_checked",

        # Loop guard for the one-shot get_levels round-trip kicked off by
        # view_creator / sheet_creator / batch_processor when a chat-typed
        # command needs to resolve Thai/special tokens (SITE, TOP, RF) but
        # ai_last_known_levels has not been cached yet.
        "ai_levels_fetch_attempted",

        # Reset Flags
        "ai_expecting_reference_sheet",
        "ai_expecting_alignment_selection",
        "ai_expecting_titleblock_selection",
        "ai_expecting_template_selection",
        "ai_pending_template_options",
        "ai_pending_titleblock_options",
    ]
    
    for k in keys:
        if k in request.session:
            request.session[k] = None
    
    request.session.modified = True
    try: request.session.save()
    except: pass

def reset_session_completely(request):
    """Completely reset all session data, including caches."""
    reset_pending(request)
    
    # Clear cached data
    cache_keys = [
        "ai_last_known_views", 
        "ai_last_known_sheets", 
        "ai_last_known_scope_boxes" 
    ]
    for key in cache_keys:
        if key in request.session:
            del request.session[key]
    
    request.session.modified = True
    debug("COMPLETE SESSION RESET")

# =====================================================================
# 💥 HELPERS (MOVED FROM GPT_INTEGRATION)
# =====================================================================

def message(text):
    return Response({"message": text})

def has_minimum_requirements(request, intent):
    from ..ai_engines.naming_engine import get_view_abbrev
    
    if intent == "create_view":
        vtype = request.session.get("ai_pending_view_type")
        levels = request.session.get("ai_pending_levels_parsed")
        
        if not vtype: return False
        
        # Skip level check for batch views
        abbrev = get_view_abbrev(vtype)
        if abbrev in ["D1", "SC", "AD", "AW"]:
            return True 
            
        return bool(levels)

    elif intent == "create_sheet":
        return request.session.get("ai_pending_sheet_category")
    
    elif intent == "batch_create":
        has_view = bool(request.session.get("ai_pending_view_type"))
        has_sheet = bool(request.session.get("ai_pending_sheet_category"))
        has_count = bool(request.session.get("ai_pending_batch_count") or request.session.get("ai_pending_levels_parsed"))
        return (has_view or has_sheet) and has_count
        
    return False

def ask_for_missing_info(request, intent):
    from ..ai_engines.naming_engine import get_view_abbrev
    
    if intent == "create_view":
        if not request.session.get("ai_pending_view_type"):
            return message("What type of view would you like to create?")
        
        vtype = request.session.get("ai_pending_view_type")
        abbrev = get_view_abbrev(vtype)
        needs_levels = abbrev not in ["D1", "SC", "AD", "AW"]
        
        if needs_levels and not request.session.get("ai_pending_levels_parsed"):
            return message("Which levels should I create the view on?")
            
        if not request.session.get("ai_pending_stage"):
            if abbrev == "AP":
                return message("Which design stage? (PD, DD)")
            return message("Which design stage? (WV, PD, DD, CD)")

        scope_choice = request.session.get("ai_pending_scope_box_id")
        cached_boxes = request.session.get("ai_last_known_scope_boxes")
        if cached_boxes and scope_choice is None:
            from ..ai_engines.scope_box_engine import format_scope_boxes_for_chat
            msg = format_scope_boxes_for_chat(cached_boxes)
            return message(msg)

        return message("I have the details for the view. Proceeding...")
    
    if intent == "create_sheet":
        if not request.session.get("ai_pending_sheet_category"):
            return message("What category of sheet? (A1, A2, etc.)")

    return message(f"I understand you want to {intent}, but I need more details.")

def check_template_requirements(request, intent):
    """
    Checks if a template is required. 
    Forces selection if multiple templates exist.
    """
    if intent not in ["create_view", "batch_create", "create_and_place"]:
        return None

    stage = request.session.get("ai_pending_stage")
    vtype_raw = request.session.get("ai_pending_view_type")
    user_tpl = request.session.get("ai_pending_template")

    if not stage or not vtype_raw:
        return None

    if stage == "NONE": return None

    # Priority Logic
    import re
    parts = re.split(r'\s+(?:and|&|,)\s+', vtype_raw, flags=re.IGNORECASE)
    primary_vtype = parts[0].strip() if parts else vtype_raw

    from ..ai_engines.template_engine import get_templates
    tpl_info = get_templates(stage, primary_vtype) 
    
    available_templates = tpl_info.get("available_templates", [])
    
    # Force ask if multiple options and none selected
    if available_templates and user_tpl is None:
        option_list = [f"{i+1}. {tpl}" for i, tpl in enumerate(available_templates)]
        # Flag so the next user message is intercepted as a template choice
        # rather than being re-routed through GPT (which often misclassifies
        # "A49_CD_A1_FLOOR PLAN" as a fresh "create floor plan" command).
        request.session["ai_expecting_template_selection"] = True
        request.session["ai_pending_template_options"] = available_templates
        request.session.modified = True
        return Response({
            "message": f"Multiple template/s found for {primary_vtype} in {stage}. Please choose one of the following:\n" + "\n".join(option_list)
        })

    # Auto-select default if none selected
    if user_tpl is None:
        request.session["ai_pending_template"] = tpl_info["default_template"]
        request.session.modified = True
        return None 

    # Validate User Selection
    canonical_tpl = user_tpl.upper() 

    if canonical_tpl not in available_templates:
        if tpl_info.get("default_template"):
            fallback = tpl_info["default_template"]
            request.session["ai_pending_template"] = fallback
            request.session.modified = True
            return message(f"Note: Previous template '{user_tpl}' is not valid for {stage}. Auto-switched to '{fallback}'.")
            
        option_list = [f"{i+1}. {tpl}" for i, tpl in enumerate(available_templates)]
        valid_options_str = "\n".join(option_list)
        return message(
            f"The template '{user_tpl}' is not valid for {primary_vtype} in {stage}. "
            f"Please choose the correct template from list below:\n{valid_options_str}"
        )

    request.session["ai_pending_template"] = canonical_tpl 
    request.session.modified = True
    return None