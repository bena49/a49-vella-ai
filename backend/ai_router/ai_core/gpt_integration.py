import json
import re
from openai import OpenAI
from rest_framework.response import Response

from ..ai_engines.system_prompt import SYSTEM_PROMPT
from ..ai_engines.level_engine import parse_levels
from ..ai_engines.titleblock_engine import parse_titleblock_from_user_text
from .session_manager import reset_pending, debug_session
from ..ai_utils.validators import validate_json_with_ai

client = OpenAI()

# =====================================================================
# 💥 FAST ROUTER (0ms Latency Pre-Flight Check)
# =====================================================================
def fast_route_intent(user_text):
    """
    Analyzes text using simple string matching.
    Returns a simulated GPT JSON response immediately if matched.
    """
    txt = user_text.lower().strip()

    # --- WIZARDS & INTERACTIVE TOOLS ---
    if any(word in txt for word in ["renumber", "rename", "inventory"]):
        return {"intent": "fetch_project_inventory"}
    
    # Zero-UI Interactive Room Package (Smart Intercept)
    room_patterns = ["room elevation", "interior elevation", "enlarged room", "enlarged plan", "room package"]
    if any(pat in txt for pat in room_patterns) or ("elevation" in txt and "room" in txt):
        stage = "CD"
        if re.search(r"\b(wv)\b", txt): stage = "WV"
        elif re.search(r"\b(pd)\b", txt): stage = "PD"
        elif re.search(r"\b(dd)\b", txt): stage = "DD"
        
        return {
            "intent": "start_interactive_room_package",
            "stage_raw": stage
        }

    if "sheet" in txt and "wizard" in txt:
        return {"intent": "wizard:create_sheets"}
    if "view" in txt and "wizard" in txt:
        return {"intent": "wizard:create_views"}
    if "create & place" in txt or "create and place" in txt:
        return {"intent": "wizard:create_and_place"}

    # 💥 THE FIX: SAFE SCOPE BOX BYPASS
    # Only intercept if the user is JUST replying with a scope box (e.g., 1-3 words)
    # This allows long master commands to pass through to OpenAI safely.
    if "sb_" in txt and len(txt.split()) <= 3:
        return {"intent": "resume_wizard"}
    
    # --- PREFLIGHT CHECK ---
    if any(word in txt for word in ["preflight", "health check", "check standards"]):
        return {"intent": "preflight_check"}

    # --- AUTO-TAG ---
    if any(phrase in txt for phrase in ["tag doors", "auto tag", "door tags", "auto-tag doors", "tag all doors"]):
        return {"intent": "wizard:auto_tag_doors"}

    # --- BASIC LISTS ---
    if txt.startswith("list ") or txt.startswith("show "):
        if "sheet" in txt: return {"intent": "list_sheets"}
        if "view" in txt: return {"intent": "list_views"}
        if "scope" in txt: return {"intent": "list_scope_boxes"}

    # --- BATCH ACTIONS ---
    if "execute_batch_update" in txt or "test batch update" in txt:
        return {"intent": "execute_batch_update"}

    # --- UI COMMANDS ---
    if "help" in txt and len(txt) < 15:
        return {"intent": "ui:help"}

    # Return None to let gpt-4o-mini handle complex requests
    return None

# =====================================================================
# GPT WRAPPER
# =====================================================================

def build_prompt(user_text):
    try:
        response = client.chat.completions.create(
            model="gpt-4o-mini",
            temperature=0,
            messages=[
                {"role": "system", "content": SYSTEM_PROMPT},
                {"role": "user", "content": user_text}
            ]
        )
        content = response.choices[0].message.content.strip()
        parsed = validate_json_with_ai(content)
        
        if isinstance(parsed, dict) and "error" in parsed:
            return {"parsed": {}, "error": parsed["error"]}
        if not isinstance(parsed, dict):
            parsed = {}
        return {"parsed": parsed}
    except Exception as ex:
        return {"parsed": {}, "error": f"GPT error: {ex}"}

# =====================================================================
# HELPER: HARDCODED INTERCEPTORS (The "Nuclear Option")
# =====================================================================
def check_for_hardcoded_intercepts(request, raw_msg):
    """
    Checks for strict command patterns that should BYPASS GPT entirely.
    Returns True if an intercept occurred and we should stop processing.
    """
    # Pattern: "Change 'Floor' to 'General'" or "Replace X with Y"
    fr_match = re.search(r"\b(change|replace)\s+['\"]?(.+?)['\"]?\s+(?:to|with)\s+['\"]?(.+?)['\"]?(?:\s+in\s+all\s+views)?$", raw_msg, re.IGNORECASE)
    
    if fr_match:
        # Safety check: Don't trigger if user mentions titleblocks or templates
        if "titleblock" not in raw_msg.lower() and "template" not in raw_msg.lower():
            find_text = fr_match.group(2).strip()
            replace_text = fr_match.group(3).strip()
            
            debug_session(request, f"⚡ INTERCEPTOR: Forced Batch Rename '{find_text}' -> '{replace_text}'")
            
            # Force the session state directly
            reset_pending(request)
            request.session["ai_pending_intent"] = "rename_view"
            # specific format for modifier.py to parse
            request.session["ai_pending_rename_target"] = f"change {find_text} in all views"
            request.session["ai_pending_rename_value"] = replace_text
            request.session["ai_pending_view_type"] = None # CRITICAL: Explicitly kill creation wizard
            request.session.modified = True
            return True # Stop processing
            
    return False # No intercept found, continue to GPT

# =====================================================================
# ROUTE GPT JSON INTO SESSION
# =====================================================================

def route_gpt_fields(request, g):
    """
    Takes the raw GPT JSON and maps it to session variables.
    Handles 'smart' defaults, conflict checks, and context switching.
    """
    debug_session(request, f"ROUTING GPT FIELDS: {g}")
    has_changes = False

    raw_msg = request.data.get("message", "").strip()
    raw_msg_lower = raw_msg.lower()
    raw_msg_upper = raw_msg.upper()
    cleaned_msg = re.sub(r'[^\w\s]', '', raw_msg_lower).strip()

    # ---------------------------------------------------------
    # 0. RUN HARDCODED INTERCEPTORS FIRST
    # ---------------------------------------------------------
    if check_for_hardcoded_intercepts(request, raw_msg):
        return None 

    # ---------------------------------------------------------
    # 💥 1. TEMP TRIGGERS: SHORTCUTS FOR TESTING
    # ---------------------------------------------------------
    
    # A. OPEN WIZARD TRIGGER
    if "open wizard" in raw_msg_lower or "fetch inventory" in raw_msg_lower:
        debug_session(request, "🔧 FORCING INTENT: 'Open Wizard' detected.")
        g["intent"] = "fetch_project_inventory"
        # We return immediately so no other logic overrides this
        return 

    # B. TEST BATCH UPDATE TRIGGER
    elif "test batch update" in raw_msg_lower:
        debug_session(request, "🔧 FORCING INTENT: 'Execute Batch Update' detected.")
        g["intent"] = "execute_batch_update"
        return

    # ---------------------------------------------------------
    # 2. STANDARD INTERCEPTORS (Ref Sheet, Alignment, Stage)
    # ---------------------------------------------------------
    
    # 💥 SAFETY GUARD (Keywords that imply a fresh start)
    new_cmd_keywords = [
        "create", "make", "generate", "add ", 
        "renumber", "rename", "duplicate", "change", "modify", "update", "replace", "switch"
    ]
    is_new_command = any(w in raw_msg_lower for w in new_cmd_keywords)

    # A. REFERENCE SHEET INTERCEPTOR
    if request.session.get("ai_expecting_reference_sheet") and not is_new_command:
        debug_session(request, f"🔒 Reference Sheet Interceptor: Capturing '{raw_msg_upper}'")
        clean_ref = raw_msg_upper.strip()
        for word in ["SHEET", "USE", "THE", "NUMBER", "IS", "REFERENCE", "MATCHING"]:
            clean_ref = clean_ref.replace(word, "").strip()
        request.session["ai_pending_reference_sheet"] = clean_ref
        request.session["ai_expecting_reference_sheet"] = False
        request.session.modified = True
        return None 

    # B. ALIGNMENT INTERCEPTOR
    if request.session.get("ai_expecting_alignment_selection") and not is_new_command:
        if "CENTER" in raw_msg_upper:
            request.session["ai_pending_alignment_mode"] = "CENTER"
            request.session["ai_expecting_alignment_selection"] = False
            request.session.modified = True
            return None
        if any(x in raw_msg_upper for x in ["MATCH", "REFERENCE", "EXISTING"]):
            request.session["ai_pending_alignment_mode"] = "MATCH"
            request.session["ai_expecting_alignment_selection"] = False
            request.session.modified = True
            return None

    # C. MANUAL STAGE INTERCEPTOR
    if raw_msg_lower in ["cd", "pd", "dd", "wv", "in cd", "in pd", "in dd", "in wv"]:
        found_stage = raw_msg_lower.replace("in ", "").upper()
        request.session["ai_pending_stage"] = found_stage
        request.session.modified = True
        return None 

    # D. SCOPE BOX INTERCEPTORS
    if request.session.get("ai_last_known_scope_boxes") and not request.session.get("ai_pending_scope_box_id"):
         if cleaned_msg in ["no", "none", "skip", "na", "nope", "stop", "dont", "zero"]:
             request.session["ai_pending_scope_box_id"] = "SKIP"
             request.session.modified = True
             return None

    cached_boxes = request.session.get("ai_last_known_scope_boxes")
    has_scope_box_match = False
    
    if cached_boxes:
        from ..ai_engines.scope_box_engine import parse_scope_box_selection
        match_id, match_name = parse_scope_box_selection(raw_msg, cached_boxes)
        if match_id:
            current = request.session.get("ai_pending_scope_box_id")
            if current != match_id:
                request.session["ai_pending_scope_box_id"] = match_id
                request.session.modified = True
                has_changes = True
                has_scope_box_match = True

    if not request.session.get("ai_pending_scope_box_id"):
        sb_regex = r"\b(SB_[a-zA-Z0-9_]+)\b"
        sb_match = re.search(sb_regex, raw_msg, re.IGNORECASE)
        if sb_match:
            found_sb = sb_match.group(1).upper()
            request.session["ai_pending_scope_box_id"] = found_sb
            request.session.modified = True
            has_changes = True
            has_scope_box_match = True          

    # =========================================================================
    # 3. MODIFICATION SAFETY NET (Forcing Intents)
    # =========================================================================
    new_intent = g.get("intent")

    # A. RENUMBERING (Force rename_sheet)
    if "renumber" in raw_msg_lower:
        if not new_intent or new_intent == "unknown":
            debug_session(request, "🔧 FORCING INTENT: renumber detected -> rename_sheet")
            new_intent = "rename_sheet"
            g["intent"] = "rename_sheet"
            if not g.get("rename_target_raw"): 
                g["rename_target_raw"] = raw_msg

    # B. FIND & REPLACE / BATCH RENAME / SET TITLE (Force rename_view)
    if any(x in raw_msg_lower for x in ["change", "replace", "rename", "switch", "set"]):
        
        # Force "Change title" to be a rename_view intent
        if "title" in raw_msg_lower:
             debug_session(request, "🔧 FORCING INTENT: 'Change title' detected -> rename_view")
             new_intent = "rename_view"
             g["intent"] = "rename_view"
             if not g.get("rename_target_raw"): g["rename_target_raw"] = raw_msg
             if not g.get("rename_value_raw"): g["rename_value_raw"] = None

        # General Replace/Change check
        elif not new_intent or new_intent == "unknown" or new_intent == "create_view":
            debug_session(request, "🔧 FORCING INTENT: change/replace detected -> rename_view")
            new_intent = "rename_view"
            g["intent"] = "rename_view"
            if not g.get("rename_target_raw"): g["rename_target_raw"] = raw_msg
            if not g.get("rename_value_raw"): g["rename_value_raw"] = None

    # =========================================================================
    # 4. CONTEXT SWITCH & FRESH START
    # =========================================================================
    
    # 💥 LIST OF PROTECTED INTENTS (Do not overwrite these with "create_view")
    protected_intents = ["rename_view", "rename_sheet", "duplicate_view", "batch_rename_views", "renumber_sheets"]

    if "sheet" in raw_msg_lower and new_intent == "create_view":
        new_intent = "create_sheet"
        g["intent"] = "create_sheet"
    
    elif "view" in raw_msg_lower and "sheet" not in raw_msg_lower and new_intent != "create_view":
        # Only switch to create_view if we aren't doing a modification
        if new_intent not in protected_intents:
            new_intent = "create_view"
            g["intent"] = "create_view"

    if new_intent and new_intent != "unknown":
        is_fresh_start = False
        has_template = bool(g.get("template_raw"))
        
        # Fresh start logic...
        if (g.get("view_type_raw") and not has_template and not has_scope_box_match) \
           or g.get("sheet_category_raw") or g.get("rename_target_raw"):
            is_fresh_start = True
            
        if is_new_command:
            is_fresh_start = True
        
        if is_fresh_start:
            debug_session(request, "Fresh command detected. Clearing pending slots.")
            
            preserved_sb = None
            if has_scope_box_match:
                preserved_sb = request.session.get("ai_pending_scope_box_id")

            reset_pending(request)
            
            if preserved_sb: request.session["ai_pending_scope_box_id"] = preserved_sb
            
            request.session["ai_last_known_views"] = []
            request.session["ai_last_known_sheets"] = []
            request.session["ai_pending_intent"] = new_intent
            has_changes = True
        else:
            if not request.session.get("ai_pending_intent"):
                request.session["ai_pending_intent"] = new_intent
                has_changes = True

        if new_intent != "batch_create" and not g.get("batch_count_raw"):
            request.session["ai_pending_batch_count"] = None
            has_changes = True

    # =========================================================================
    # 5. SLOT FILLING
    # =========================================================================
    if g.get("stage_raw"):
        new_stage = g["stage_raw"].upper()
        current = request.session.get("ai_pending_stage")
        if current and new_stage != current: request.session["ai_pending_template"] = None 
        request.session["ai_pending_stage"] = new_stage
        has_changes = True
    elif not request.session.get("ai_pending_stage"):
        stage_match = re.search(r'\b(in|for|at)\s+(WV|PD|DD|CD)\b', raw_msg, re.IGNORECASE)
        if stage_match:
            request.session["ai_pending_stage"] = stage_match.group(2).upper()
            has_changes = True

    if g.get("view_type_raw"):
        new_vt = g["view_type_raw"]
        if "SB_" not in new_vt.upper():
            current_vt = request.session.get("ai_pending_view_type")
            if current_vt and new_vt != current_vt:
                request.session["ai_pending_template"] = None
                request.session["ai_pending_sheet_category"] = None
                has_changes = True
            request.session["ai_pending_view_type"] = new_vt
            has_changes = True
    
    # 💥 View Type Regex & FORCE VIEW INTENT
    vt_patterns = [
        r"\b(schedules?)\b", 
        r"\b(detail\s*views?)\b", 
        r"\b(drafting\s*views?)\b", 
        r"\b(legends?)\b",
        r"\b(floor\s*plans?)\b",
        r"\b(ceiling\s*plans?)\b"
    ]
    
    found_regex_view = False
    for pat in vt_patterns:
        match = re.search(pat, raw_msg_lower)
        if match:
            found_vt = match.group(1)
            request.session["ai_pending_view_type"] = found_vt
            
            # 💥 FIX: BLOCK CREATION IF MODIFYING
            # Don't switch to 'create_view' if user is modifying things.
            modification_verbs = ["rename", "renumber", "change", "replace", "switch", "modify"]
            is_modifying = any(verb in raw_msg_lower for verb in modification_verbs)
            
            if not is_modifying:
                request.session["ai_pending_intent"] = "create_view" 
                
            request.session["ai_pending_sheet_category"] = None
            has_changes = True
            found_regex_view = True
            break
            
    if not found_regex_view and not request.session.get("ai_pending_view_type"):
        if g.get("view_type_raw"):
             request.session["ai_pending_view_type"] = g["view_type_raw"]
             has_changes = True

    slots = {"template_raw": "ai_pending_template", "sheet_category_raw": "ai_pending_sheet_category", "user_provided_name_raw": "ai_pending_user_provided_name"}
    for gpt_key, session_key in slots.items():
        if g.get(gpt_key):
            val = g[gpt_key]
            
            # 💥 THE FIX: Clean up GPT's "greedy" template extraction
            if gpt_key == "template_raw" and isinstance(val, str):
                # Moved (?i) to the absolute start of the regex string
                val = re.split(r'(?i)\s+(and\s+sb_|with\s+sb_|sb_)', val)[0]
                val = val.strip()

            if session_key == "ai_pending_sheet_category" and request.session.get("ai_pending_intent") == "create_view":
                continue 
            
            request.session[session_key] = val
            has_changes = True

    # =========================================================================
    # 6. SMART LEVEL MERGING (GPT vs. RAW REGEX)
    # =========================================================================
    gpt_levels_raw = g.get("levels_raw")
    parsed_gpt = parse_levels(gpt_levels_raw) if gpt_levels_raw else []
    
    # Always run your new "Range Expander" on the raw user message
    parsed_raw = parse_levels(raw_msg)
    
    # DECISION: Who found more levels?
    if len(parsed_raw) > len(parsed_gpt):
        debug_session(request, f"Levels: Trusting Regex expansion ({len(parsed_raw)}) over GPT ({len(parsed_gpt)})")
        request.session["ai_pending_levels_parsed"] = parsed_raw
        request.session["ai_pending_levels_raw"] = ", ".join(parsed_raw)
        has_changes = True
    elif parsed_gpt:
        request.session["ai_pending_levels_raw"] = gpt_levels_raw
        request.session["ai_pending_levels_parsed"] = parsed_gpt
        has_changes = True
    elif parsed_raw and not request.session.get("ai_pending_levels_parsed"):
         # Fallback if GPT missed them entirely
         request.session["ai_pending_levels_parsed"] = parsed_raw
         request.session["ai_pending_levels_raw"] = ", ".join(parsed_raw)
         has_changes = True

    if g.get("batch_count_raw"):
        request.session["ai_pending_batch_count"] = str(g["batch_count_raw"])
        has_changes = True
    if not request.session.get("ai_pending_batch_count"):
        batch_match = re.search(r'\b(\d+|one|two|three|four|five)\s+([a-zA-Z0-9_]+\s+)?(sheet|view)', raw_msg_lower)
        if batch_match: request.session["ai_pending_batch_count"] = batch_match.group(1)

    if g.get("titleblock_raw"):
        raw_tb = g["titleblock_raw"]
        request.session["ai_pending_titleblock"] = raw_tb
        fam, t_type = parse_titleblock_from_user_text(raw_tb)
        if fam and t_type:
            request.session["titleblock_family"] = fam
            request.session["titleblock_type"] = t_type
        has_changes = True
    elif not request.session.get("ai_pending_titleblock"):
        tb_match = re.search(r'(?:using|with)\s+(A49_TB_[a-zA-Z0-9_]+(?:\s*:\s*[a-zA-Z0-9\s]+)?)', raw_msg, re.IGNORECASE)
        if tb_match:
            found_tb = tb_match.group(1).strip()
            request.session["ai_pending_titleblock"] = found_tb
            fam, t_type = parse_titleblock_from_user_text(found_tb)
            if fam and t_type:
                request.session["titleblock_family"] = fam
                request.session["titleblock_type"] = t_type
            has_changes = True

    extra_fields = { "rename_target_raw": "ai_pending_rename_target", "rename_value_raw": "ai_pending_rename_value", "duplicate_mode_raw": "ai_pending_duplicate_mode", "target_sheet_raw": "ai_pending_target_sheet", "reference_sheet_raw": "ai_pending_reference_sheet" }
    for gpt_key, session_key in extra_fields.items():
        if g.get(gpt_key):
            request.session[session_key] = g[gpt_key]
            has_changes = True

    if g.get("placement_raw"):
        raw_place = g["placement_raw"].upper()
        if raw_place in ["MATCH", "ALIGN", "REFERENCE"]:
            request.session["ai_pending_alignment_mode"] = "MATCH"
        else:
            request.session["ai_pending_alignment_mode"] = "CENTER"
        has_changes = True

    # 💥 SAFETY NET (PRIORITY FIX)
    if not request.session.get("ai_pending_intent"):
        if is_new_command:
            if request.session.get("ai_pending_view_type"):
                request.session["ai_pending_intent"] = "create_view"
                has_changes = True
            elif request.session.get("ai_pending_sheet_category") or "sheet" in raw_msg_lower:
                request.session["ai_pending_intent"] = "create_sheet"
                has_changes = True

    if has_changes:
        request.session.modified = True 
    
    return None