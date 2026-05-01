# ai_router/ai_commands/automate_tag_nlp.py
# ============================================================================
# Conversational Tagging Engine — Natural Language slot-filling for Vella
# ============================================================================
# Handles the full conversation flow for tagging elements via chat:
#
#   Step 1: awaiting_view_type  — "Which view type? Floor Plan, Elevation, Section"
#   Step 2: awaiting_stage      — "Which stage? WV, PD, DD, CD"
#   Step 3: awaiting_view_confirm — "Found N views. Tag all, or provide a specific name?"
#   Step 4: awaiting_specific_view — "Please provide the view name"
#   Step 5: awaiting_tag_family  — "Which tag family? [list]"
#
# Slots provided upfront are skipped. Power users can provide everything
# in one command: "tag doors in CD_A1_FL_01 using A49_Door Tag : Mark"
# ============================================================================

from rest_framework.response import Response
from ..ai_utils.envelope_builder import send_envelope, envelope_automate_tag
from ..ai_core.session_manager import debug_session


# Tag category → compatible Revit view types (for the view-type question)
CATEGORY_VIEW_TYPE_OPTIONS = {
    "door":           [("FloorPlan", "Floor Plan"), ("Elevation", "Elevation"), ("Section", "Section")],
    "window":         [("FloorPlan", "Floor Plan"), ("Elevation", "Elevation"), ("Section", "Section")],
    "wall":           [("FloorPlan", "Floor Plan")],
    "room":           [("FloorPlan", "Floor Plan"), ("CeilingPlan", "Ceiling Plan"), ("Elevation", "Elevation"), ("Section", "Section")],
    "ceiling":        [("CeilingPlan", "Ceiling Plan")],
    "spot_elevation": [("FloorPlan", "Floor Plan"), ("Section", "Section")],
}

ELEMENT_NAMES = {
    "door": "doors", "window": "windows", "wall": "walls",
    "room": "rooms", "ceiling": "ceilings", "spot_elevation": "spot elevations",
}

# User input → Revit ViewType string
VIEW_TYPE_INPUT_MAP = {
    "floor plan": "FloorPlan", "floor plans": "FloorPlan", "floorplan": "FloorPlan",
    "plan": "FloorPlan", "plans": "FloorPlan", "fp": "FloorPlan", "1": "FloorPlan",
    "ceiling plan": "CeilingPlan", "ceiling plans": "CeilingPlan", "ceilingplan": "CeilingPlan",
    "rcp": "CeilingPlan", "ceiling": "CeilingPlan", "2": "CeilingPlan",
    "elevation": "Elevation", "elevations": "Elevation", "elev": "Elevation", "3": "Elevation",
    "section": "Section", "sections": "Section", "sect": "Section", "4": "Section",
}

# Stage input normalization
STAGE_INPUT_MAP = {
    "wv": "WV", "pd": "PD", "dd": "DD", "cd": "CD",
    "1": "WV", "2": "PD", "3": "DD", "4": "CD",
    "working view": "WV", "pre-design": "PD", "design development": "DD",
    "construction documents": "CD", "construction": "CD",
}


def _get_state(request):
    """Get or initialize the NLP tag conversation state."""
    return request.session.get("ai_nlp_tag_state", None)


def _set_state(request, state):
    """Save conversation state to session."""
    request.session["ai_nlp_tag_state"] = state
    request.session.modified = True


def _clear_state(request):
    """Clear conversation state."""
    request.session["ai_nlp_tag_state"] = None
    request.session.modified = True


def _ensure_cache(request):
    """
    Check if tag inventory is cached. If not, store current state as pending
    and trigger a fetch from Revit. Returns (has_cache, response).
    """
    all_views = request.session.get("ai_last_known_taggable_views", [])
    if all_views:
        return True, None
    
    # Store current state so we can resume after cache arrives
    state = _get_state(request)
    if state:
        request.session["ai_pending_nlp_tag_request"] = state
        request.session.modified = True
    
    debug_session(request, "🔍 NLP Tag: Cache empty, fetching from Revit...")
    return False, send_envelope(request, {"command": "fetch_project_info"})


def _get_compatible_types(category):
    """Returns the list of (key, label) view type options for this tag category."""
    return CATEGORY_VIEW_TYPE_OPTIONS.get(category, [])


def _filter_views(request, category, stage, view_type_filter, view_name=""):
    """Filter cached views based on current slot values."""
    all_views = request.session.get("ai_last_known_taggable_views", [])
    compatible_keys = [k for k, _ in _get_compatible_types(category)]
    
    views = [v for v in all_views if v.get("view_type") in compatible_keys]
    
    if view_name:
        name_upper = view_name.upper()
        matched = [v for v in views if v.get("name", "").upper() == name_upper]
        if not matched:
            matched = [v for v in views if name_upper in v.get("name", "").upper()]
        if not matched:
            matched = [v for v in views if v.get("name", "").upper().startswith(name_upper)]
        return matched
    
    if stage:
        views = [v for v in views if v.get("stage") == stage]
    if view_type_filter:
        views = [v for v in views if v.get("view_type") == view_type_filter]
    
    return views


def _dispatch_tag(request, state):
    """Build envelope and send the tag command to Revit."""
    category = state["tag_category"]
    view_ids = state["view_ids"]
    tag_family = state["tag_family"]
    tag_type = state["tag_type"]
    element_name = ELEMENT_NAMES.get(category, "elements")
    
    debug_session(request, f"🚀 NLP Tag: {category} in {len(view_ids)} views using {tag_family} : {tag_type}")
    _clear_state(request)
    
    env = envelope_automate_tag(
        tag_category=category,
        tag_family=tag_family,
        tag_type=tag_type,
        view_ids=view_ids,
        skip_tagged=True
    )
    return send_envelope(request, env)


# ============================================================================
# MAIN ENTRY POINT — called from intent_router for 'automate_tag_nlp' intent
# ============================================================================

def handle_automate_tag_nlp(request, nlp_data):
    """
    Starts or continues the conversational tagging flow.
    Called when the fast router detects a tagging NLP command.
    
    nlp_data keys (all optional, extracted by fast router):
        tag_category, stage, view_type_filter, view_name, tag_family_raw
    """
    category = nlp_data.get("tag_category", "").lower()
    stage = nlp_data.get("stage", "").upper()
    view_type_filter = nlp_data.get("view_type_filter", "")
    view_name = nlp_data.get("view_name", "")
    tag_family_raw = nlp_data.get("tag_family_raw", "")
    
    element_name = ELEMENT_NAMES.get(category, "elements")
    
    # Ensure cache exists
    has_cache, fetch_resp = _ensure_cache(request)
    if not has_cache:
        # Store what we know so far for resumption
        _set_state(request, {
            "step": "pending_cache",
            "tag_category": category,
            "stage": stage,
            "view_type_filter": view_type_filter,
            "view_name": view_name,
            "tag_family_raw": tag_family_raw,
        })
        return fetch_resp
    
    # ─────────────────────────────────────────────────────
    # POWER USER: Everything provided → execute immediately
    # ─────────────────────────────────────────────────────
    if view_name and tag_family_raw:
        views = _filter_views(request, category, "", "", view_name)
        if not views:
            return Response({"message": f"I couldn't find a view named '{view_name}'. Please check the name and try again."})
        
        # If multiple views match, ask user to be more specific
        if len(views) > 1:
            view_list = "\n".join([f"• {v['name']}" for v in views])
            state = {
                "step": "awaiting_specific_view",
                "tag_category": category,
                "stage": "",
                "view_type_filter": "",
                "view_name": "",
                "matched_views": views,
                "view_ids": [],
                "tag_family": "",
                "tag_type": "",
                "tag_family_raw": tag_family_raw,
            }
            _set_state(request, state)
            return Response({
                "message": f"I found {len(views)} views matching '{view_name}':\n{view_list}\n\n"
                           f"Please provide the exact view name, including the suffix (e.g. the part in parentheses):"
            })
        
        family, ftype = _resolve_tag_family_from_text(request, category, tag_family_raw)
        if not family:
            return Response({"message": f"I couldn't find a tag family matching '{tag_family_raw}' in this project."})
        
        state = {
            "tag_category": category,
            "view_ids": [v["id"] for v in views],
            "tag_family": family,
            "tag_type": ftype,
        }
        return _dispatch_tag(request, state)
    
    # ─────────────────────────────────────────────────────
    # DETERMINE WHICH STEP TO START AT
    # ─────────────────────────────────────────────────────
    compatible_types = _get_compatible_types(category)
    
    # If only one view type is compatible (e.g. ceiling → only CeilingPlan), auto-select
    if len(compatible_types) == 1:
        view_type_filter = compatible_types[0][0]
    
    # Build initial state
    state = {
        "step": None,
        "tag_category": category,
        "stage": stage,
        "view_type_filter": view_type_filter,
        "view_name": view_name,
        "matched_views": [],
        "view_ids": [],
        "tag_family": "",
        "tag_type": "",
    }
    
    # Skip to the right step based on what we have
    if view_name:
        # Specific view → resolve then ask tag family
        views = _filter_views(request, category, "", "", view_name)
        if not views:
            return Response({"message": f"I couldn't find a view named '{view_name}'. Please check the name and try again."})
        
        if len(views) > 1:
            view_list = "\n".join([f"• {v['name']}" for v in views])
            state["step"] = "awaiting_specific_view"
            state["matched_views"] = views
            _set_state(request, state)
            return Response({
                "message": f"I found {len(views)} views matching '{view_name}':\n{view_list}\n\n"
                           f"Please provide the exact view name, including the suffix (e.g. the part in parentheses):"
            })
        
        state["matched_views"] = views
        state["view_ids"] = [v["id"] for v in views]
        return _ask_tag_family(request, state)
    
    if not view_type_filter:
        return _ask_view_type(request, state)
    
    if not stage:
        return _ask_stage(request, state)
    
    # We have category + view type + stage → find views and confirm
    return _ask_view_confirmation(request, state)


# ============================================================================
# CONVERSATION INTERCEPTOR — called from views.py for ongoing conversations
# ============================================================================

def handle_nlp_tag_conversation(request, user_text):
    """
    Processes user responses during an active tagging conversation.
    Called from views.py interceptor when ai_nlp_tag_state exists.
    
    Returns Response if handled, None if state doesn't exist.
    """
    state = _get_state(request)
    if not state:
        return None
    
    step = state.get("step")
    txt = user_text.strip()
    txt_lower = txt.lower().strip()
    
    # Allow user to cancel at any step
    if txt_lower in ["cancel", "stop", "nevermind", "never mind", "quit", "exit"]:
        _clear_state(request)
        return Response({"message": "No problem. Tagging cancelled."})
    
    category = state.get("tag_category", "")
    element_name = ELEMENT_NAMES.get(category, "elements")
    
    # ─────────────────────────────────────────────────────
    if step == "awaiting_view_type":
        resolved = VIEW_TYPE_INPUT_MAP.get(txt_lower)
        if not resolved:
            compatible = _get_compatible_types(category)
            options = ", ".join([label for _, label in compatible])
            return Response({"message": f"I didn't catch that. Please choose: {options}"})
        
        # Validate compatibility
        compatible_keys = [k for k, _ in _get_compatible_types(category)]
        if resolved not in compatible_keys:
            options = ", ".join([label for _, label in _get_compatible_types(category)])
            return Response({"message": f"That view type isn't compatible with {element_name} tags. Please choose: {options}"})
        
        state["view_type_filter"] = resolved
        
        if not state.get("stage"):
            return _ask_stage(request, state)
        return _ask_view_confirmation(request, state)
    
    # ─────────────────────────────────────────────────────
    elif step == "awaiting_stage":
        resolved = STAGE_INPUT_MAP.get(txt_lower)
        if not resolved:
            return Response({"message": "I didn't catch that. Please choose a stage: WV, PD, DD, or CD"})
        
        state["stage"] = resolved
        return _ask_view_confirmation(request, state)
    
    # ─────────────────────────────────────────────────────
    elif step == "awaiting_view_confirm":
        if txt_lower in ["yes", "y", "all", "tag all", "sure", "ok", "confirm", "proceed"]:
            # User confirmed tagging all matched views
            state["view_ids"] = [v["id"] for v in state.get("matched_views", [])]
            return _ask_tag_family(request, state)
        
        elif txt_lower in ["no", "n", "specific", "select"]:
            # User wants to pick a specific view
            state["step"] = "awaiting_specific_view"
            _set_state(request, state)
            return Response({"message": "Please provide the specific view name to tag:"})
        
        else:
            # Maybe they typed a view name directly
            views = _filter_views(request, category, state.get("stage", ""), state.get("view_type_filter", ""), txt)
            if views:
                state["matched_views"] = views
                state["view_ids"] = [v["id"] for v in views]
                return _ask_tag_family(request, state)
            
            return Response({"message": "Please say 'Yes' to tag all, 'No' to provide a specific view name, or type a view name directly."})
    
    # ─────────────────────────────────────────────────────
    elif step == "awaiting_specific_view":
        views = _filter_views(request, category, "", "", txt)
        if not views:
            return Response({"message": f"I couldn't find a view matching '{txt}'. Please check the name and try again, or say 'cancel' to stop."})
        
        state["matched_views"] = views
        state["view_ids"] = [v["id"] for v in views]
        
        if len(views) > 1:
            view_list = "\n".join([f"• {v['name']}" for v in views[:10]])
            suffix = f"\n  ...and {len(views) - 10} more" if len(views) > 10 else ""
            state["step"] = "awaiting_specific_view"
            _set_state(request, state)
            return Response({
                "message": f"I still found {len(views)} views matching '{txt}':\n{view_list}{suffix}\n\nPlease provide the exact view name including the suffix:"
            })
        
        # If we have a saved tag_family_raw from power user path, resolve and dispatch
        saved_family_raw = state.get("tag_family_raw", "")
        if saved_family_raw:
            family, ftype = _resolve_tag_family_from_text(request, category, saved_family_raw)
            if family:
                state["tag_family"] = family
                state["tag_type"] = ftype
                return _dispatch_tag(request, state)
        
        return _ask_tag_family(request, state)
    
    # ─────────────────────────────────────────────────────
    elif step == "awaiting_tag_family":
        inventory_key = f"ai_last_known_{category}_tags"
        available_tags = request.session.get(inventory_key, [])
        
        selected_family = None
        selected_type = None
        
        for tag in available_tags:
            tag_display = f"{tag['family']} : {tag['type']}"
            if (txt_lower == tag_display.lower() or
                txt_lower == tag['family'].lower() or
                txt_lower == tag['type'].lower() or
                txt_lower == tag['family'].lower() + " " + tag['type'].lower()):
                selected_family = tag['family']
                selected_type = tag['type']
                break
        
        if not selected_family:
            tag_list = "\n".join([f"• {t['family']} : {t['type']}" for t in available_tags])
            return Response({"message": f"I didn't match that. Please copy and paste one:\n{tag_list}"})
        
        state["tag_family"] = selected_family
        state["tag_type"] = selected_type
        return _dispatch_tag(request, state)
    
    # Unknown step — clear state
    _clear_state(request)
    return None


# ============================================================================
# STEP FUNCTIONS — each asks one question and sets the next step
# ============================================================================

def _ask_view_type(request, state):
    """Ask which view type to tag."""
    category = state["tag_category"]
    element_name = ELEMENT_NAMES.get(category, "elements")
    compatible = _get_compatible_types(category)
    
    if len(compatible) == 1:
        # Only one option — auto-select
        state["view_type_filter"] = compatible[0][0]
        if not state.get("stage"):
            return _ask_stage(request, state)
        return _ask_view_confirmation(request, state)
    
    options = "\n".join([f"• {label}" for _, label in compatible])
    state["step"] = "awaiting_view_type"
    _set_state(request, state)
    
    return Response({
        "message": f"Sure! Which view type should I tag {element_name} in?\n{options}"
    })


def _ask_stage(request, state):
    """Ask which design stage."""
    state["step"] = "awaiting_stage"
    _set_state(request, state)
    
    return Response({
        "message": "Which design stage?\n• WV\n• PD\n• DD\n• CD"
    })


def _ask_view_confirmation(request, state):
    """Show matched views and ask to tag all or pick specific."""
    category = state["tag_category"]
    stage = state.get("stage", "")
    vtype = state.get("view_type_filter", "")
    element_name = ELEMENT_NAMES.get(category, "elements")
    
    views = _filter_views(request, category, stage, vtype)
    
    if not views:
        _clear_state(request)
        vtype_label = vtype if vtype else "compatible"
        return Response({
            "message": f"I couldn't find any {vtype_label} views in {stage} that support {element_name} tags. "
                       f"Please check your project views or try a different stage."
        })
    
    state["matched_views"] = views
    state["step"] = "awaiting_view_confirm"
    _set_state(request, state)
    
    # Format view type label for display
    vtype_labels = {"FloorPlan": "Floor Plan", "CeilingPlan": "Ceiling Plan", 
                    "Elevation": "Elevation", "Section": "Section"}
    vtype_display = vtype_labels.get(vtype, "view")
    
    view_list = "\n".join([f"• {v['name']}" for v in views[:15]])
    suffix = f"\n  ...and {len(views) - 15} more" if len(views) > 15 else ""
    
    return Response({
        "message": (
            f"I found {len(views)} {stage} {vtype_display}(s):\n{view_list}{suffix}\n\n"
            f"Shall I tag all {len(views)}? Say 'Yes' to tag all, 'No' to provide a specific view name."
        )
    })


def _ask_tag_family(request, state):
    """Ask which tag family to use, or auto-select if only one."""
    category = state["tag_category"]
    inventory_key = f"ai_last_known_{category}_tags"
    available_tags = request.session.get(inventory_key, [])
    
    if not available_tags:
        _clear_state(request)
        return Response({
            "message": (
                f"I found the views, but there are no {category} tag families loaded in this project.\n\n"
                f"Would you like me to run a Preflight Repair to bring in the standard tags?"
            )
        })
    
    if len(available_tags) == 1:
        # Single tag family — auto-select and dispatch
        state["tag_family"] = available_tags[0]["family"]
        state["tag_type"] = available_tags[0]["type"]
        view_count = len(state.get("view_ids", []))
        element_name = ELEMENT_NAMES.get(category, "elements")
        
        # Show a brief confirmation before executing
        return _dispatch_tag(request, state)
    
    # Multiple tag families — ask user to pick
    tag_list = "\n".join([f"• {t['family']} : {t['type']}" for t in available_tags])
    view_count = len(state.get("view_ids", []))
    
    state["step"] = "awaiting_tag_family"
    _set_state(request, state)
    
    return Response({
        "message": (
            f"I found {view_count} view(s) to tag. Which tag family should I use?\n"
            f"Please copy and paste one:\n{tag_list}"
        )
    })


# ============================================================================
# POWER USER HELPER — resolve tag family from raw text like "A49_Door Tag : Mark"
# ============================================================================

def _resolve_tag_family_from_text(request, category, raw_text):
    """
    Tries to match raw_text against available tag families.
    Returns (family, type) or (None, None).
    """
    inventory_key = f"ai_last_known_{category}_tags"
    available_tags = request.session.get(inventory_key, [])
    
    raw_lower = raw_text.lower().strip()
    
    for tag in available_tags:
        tag_display = f"{tag['family']} : {tag['type']}".lower()
        if raw_lower == tag_display or raw_lower in tag_display:
            return tag['family'], tag['type']
    
    # Try partial match on family name
    for tag in available_tags:
        if raw_lower in tag['family'].lower() or tag['family'].lower() in raw_lower:
            return tag['family'], tag['type']
    
    return None, None


# ============================================================================
# RESUME AFTER CACHE POPULATED
# ============================================================================

def resume_pending_nlp_tag(request):
    """
    Called after cache_tag_inventory populates the session.
    Checks for pending NLP tag request or conversation state and resumes.
    
    Returns Response if resumed, None if nothing pending.
    """
    # Check for pending request (from initial command before cache existed)
    pending = request.session.get("ai_pending_nlp_tag_request")
    if pending:
        request.session["ai_pending_nlp_tag_request"] = None
        request.session.modified = True
        
        debug_session(request, f"🔄 Resuming pending NLP tag: {pending}")
        
        # Re-enter handle_automate_tag_nlp with the stored data
        return handle_automate_tag_nlp(request, pending)
    
    # Check for pending conversation state
    state = _get_state(request)
    if state and state.get("step") == "pending_cache":
        debug_session(request, f"🔄 Resuming pending NLP tag conversation")
        return handle_automate_tag_nlp(request, state)
    
    return None
