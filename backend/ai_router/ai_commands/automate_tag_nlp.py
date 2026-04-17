# ai_router/ai_commands/automate_tag_nlp.py
# Natural Language Tagging — handles commands like "tag doors in CD floor plans or tag windows in DD elevations"
# Resolves views and tag families from cached project data, then dispatches to C#.
# If cache is empty, triggers a fetch from Revit and auto-resumes after data arrives.

from rest_framework.response import Response
from ..ai_utils.envelope_builder import send_envelope, envelope_automate_tag
from ..ai_core.session_manager import debug_session


# Tag category → compatible Revit view types
CATEGORY_VIEW_COMPAT = {
    "door":    ["FloorPlan", "Elevation", "Section"],
    "window":  ["FloorPlan", "Elevation", "Section"],
    "wall":    ["FloorPlan"],
    "room":    ["FloorPlan", "CeilingPlan", "Elevation", "Section"],
    "ceiling": ["CeilingPlan"],
}

# Friendly element names for chat messages
ELEMENT_NAMES = {
    "door": "doors", "window": "windows", "wall": "walls",
    "room": "rooms", "ceiling": "ceilings",
}


def handle_automate_tag_nlp(request, nlp_data):
    """
    Executes tagging based on Natural Language intent parsed by the fast router.
    
    If the tag inventory cache is empty, stores the pending request in session
    and triggers a fetch_project_info from Revit. When the data arrives back
    (via cache_tag_inventory), the pending request auto-resumes.
    """
    category = nlp_data.get("tag_category", "").lower()
    target_stage = nlp_data.get("stage", "").upper()
    view_type_filter = nlp_data.get("view_type_filter", "")
    level_filter = nlp_data.get("level_filter", "")
    
    element_name = ELEMENT_NAMES.get(category, "elements")
    
    # ─────────────────────────────────────────────────────
    # 1. CHECK CACHED DATA — if empty, trigger auto-fetch
    # ─────────────────────────────────────────────────────
    all_views = request.session.get("ai_last_known_taggable_views", [])
    
    if not all_views:
        # Store the pending NLP request so we can resume after data arrives
        request.session["ai_pending_nlp_tag_request"] = {
            "tag_category": category,
            "stage": target_stage,
            "view_type_filter": view_type_filter,
            "level_filter": level_filter,
        }
        request.session.modified = True
        
        debug_session(request, f"🔍 NLP Tag: Cache empty, fetching project inventory from Revit...")
        
        # Tell Revit to send project info (this goes to Vue → Vue caches to backend)
        return send_envelope(request, {"command": "fetch_project_info"})
    
    # ─────────────────────────────────────────────────────
    # 2. EXECUTE
    # ─────────────────────────────────────────────────────
    return _execute_nlp_tag(request, category, target_stage, view_type_filter, level_filter)


def resume_pending_nlp_tag(request):
    """
    Called after cache_tag_inventory populates the session.
    Checks if there's a pending NLP tag request and resumes it.
    
    Returns Response if resumed, None if no pending request.
    """
    pending = request.session.get("ai_pending_nlp_tag_request")
    if not pending:
        return None
    
    # Clear the pending flag
    request.session["ai_pending_nlp_tag_request"] = None
    request.session.modified = True
    
    debug_session(request, f"🔄 Resuming pending NLP tag request: {pending}")
    
    return _execute_nlp_tag(
        request,
        pending.get("tag_category", ""),
        pending.get("stage", ""),
        pending.get("view_type_filter", ""),
        pending.get("level_filter", ""),
    )


def _execute_nlp_tag(request, category, target_stage, view_type_filter, level_filter):
    """
    Core NLP tag execution — filters views, resolves tag family, dispatches.
    """
    element_name = ELEMENT_NAMES.get(category, "elements")
    all_views = request.session.get("ai_last_known_taggable_views", [])
    compatible_types = CATEGORY_VIEW_COMPAT.get(category, [])
    
    target_views = [v for v in all_views if v.get("view_type") in compatible_types]
    
    # Stage filter
    if target_stage:
        target_views = [v for v in target_views if v.get("stage") == target_stage]
    
    # View type filter
    if view_type_filter:
        target_views = [v for v in target_views if v.get("view_type") == view_type_filter]
    
    # Level filter
    if level_filter:
        target_views = [v for v in target_views if v.get("level") == level_filter]
    
    if not target_views:
        stage_note = f" in {target_stage}" if target_stage else ""
        vtype_note = f" ({view_type_filter})" if view_type_filter else ""
        return Response({
            "message": f"I couldn't find any {element_name}-compatible views{stage_note}{vtype_note}. "
                       f"Try adjusting the stage or view type, or use the wizard for manual selection."
        })
    
    view_ids = [v["id"] for v in target_views]
    
    # ─────────────────────────────────────────────────────
    # RESOLVE TAG FAMILY
    # ─────────────────────────────────────────────────────
    inventory_key = f"ai_last_known_{category}_tags"
    available_tags = request.session.get(inventory_key, [])
    
    if not available_tags:
        return Response({
            "message": (
                f"I found {len(target_views)} views to tag, but there are no {category} tag families "
                f"loaded in this project.\n\n"
                f"Would you like me to run a **Preflight Repair** to bring in the standard tags from the IRIs library?"
            )
        })
    
    # If multiple tag families available, list them for user to pick
    if len(available_tags) > 1:
        tag_list = "\n".join([f"• {t['family']} : {t['type']}" for t in available_tags])
        
        request.session["ai_pending_nlp_tag"] = {
            "category": category,
            "view_ids": view_ids,
            "stage": target_stage,
            "view_count": len(target_views),
        }
        request.session.modified = True
        
        return Response({
            "message": (
                f"I found {len(target_views)} views with {element_name} to tag.\n\n"
                f"Which tag family should I use? Please copy and paste one:\n{tag_list}"
            )
        })
    
    # Single tag family — auto-select
    selected_family = available_tags[0].get("family", "")
    selected_type = available_tags[0].get("type", "")
    
    # ─────────────────────────────────────────────────────
    # DISPATCH
    # ─────────────────────────────────────────────────────
    debug_session(request, f"🚀 NLP Tag: {category} in {len(view_ids)} views using {selected_family}")
    
    env = envelope_automate_tag(
        tag_category=category,
        tag_family=selected_family,
        tag_type=selected_type,
        view_ids=view_ids,
        skip_tagged=True
    )
    
    return send_envelope(request, env)


def handle_nlp_tag_family_selection(request, user_text):
    """
    Interceptor for when a user is selecting a tag family after an NLP tag command.
    Called when ai_pending_nlp_tag is set in session.
    
    Returns Response if handled, None if not applicable.
    """
    pending = request.session.get("ai_pending_nlp_tag")
    if not pending:
        return None
    
    category = pending.get("category", "")
    inventory_key = f"ai_last_known_{category}_tags"
    available_tags = request.session.get(inventory_key, [])
    
    user_clean = user_text.strip()
    
    selected_family = None
    selected_type = None
    
    for tag in available_tags:
        tag_display = f"{tag['family']} : {tag['type']}"
        if (user_clean.lower() == tag_display.lower() or 
            user_clean.lower() == tag['family'].lower() or
            user_clean.lower() == tag['type'].lower()):
            selected_family = tag['family']
            selected_type = tag['type']
            break
    
    if not selected_family:
        request.session["ai_pending_nlp_tag"] = None
        request.session.modified = True
        return None
    
    view_ids = pending.get("view_ids", [])
    view_count = pending.get("view_count", len(view_ids))
    request.session["ai_pending_nlp_tag"] = None
    request.session.modified = True
    
    element_name = ELEMENT_NAMES.get(category, "elements")
    debug_session(request, f"🚀 NLP Tag (family selected): {category} in {view_count} views using {selected_family}")
    
    env = envelope_automate_tag(
        tag_category=category,
        tag_family=selected_family,
        tag_type=selected_type,
        view_ids=view_ids,
        skip_tagged=True
    )
    
    return send_envelope(request, env)
