# ai_router/ai_commands/automate_tag_nlp.py
# Natural Language Tagging — handles commands like "tag doors in CD floor plans"
# Resolves views and tag families from cached project data, then dispatches to C#.

from rest_framework.response import Response
from ..ai_utils.envelope_builder import send_envelope, envelope_automate_tag
from ..ai_core.session_manager import debug_session


# View type keywords → Revit ViewType strings
VIEW_TYPE_KEYWORDS = {
    "floor plan": "FloorPlan",
    "floor plans": "FloorPlan",
    "plan": "FloorPlan",
    "plans": "FloorPlan",
    "ceiling plan": "CeilingPlan",
    "ceiling plans": "CeilingPlan",
    "rcp": "CeilingPlan",
    "elevation": "Elevation",
    "elevations": "Elevation",
    "section": "Section",
    "sections": "Section",
}

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
    
    nlp_data expected keys:
        - tag_category: str ("door", "window", "wall", "room", "ceiling")
        - stage: str ("WV", "PD", "DD", "CD") — optional
        - view_type_filter: str (e.g. "FloorPlan", "Elevation") — optional
        - level_filter: str (e.g. "01", "02") — optional
    """
    category = nlp_data.get("tag_category", "").lower()
    target_stage = nlp_data.get("stage", "").upper()
    view_type_filter = nlp_data.get("view_type_filter", "")
    level_filter = nlp_data.get("level_filter", "")
    
    element_name = ELEMENT_NAMES.get(category, "elements")
    
    # ─────────────────────────────────────────────────────
    # 1. CHECK CACHED DATA
    # ─────────────────────────────────────────────────────
    all_views = request.session.get("ai_last_known_taggable_views", [])
    
    if not all_views:
        return Response({
            "message": (
                f"I'd love to tag those {element_name}, but I haven't scanned your project views yet.\n\n"
                f"Please open the **Automate Tagging** wizard once (click + → Automate Tagging) "
                f"so I can inventory your project. After that, you can use chat commands like this anytime!"
            )
        })
    
    # ─────────────────────────────────────────────────────
    # 2. FILTER VIEWS
    # ─────────────────────────────────────────────────────
    compatible_types = CATEGORY_VIEW_COMPAT.get(category, [])
    
    target_views = [
        v for v in all_views
        if v.get("view_type") in compatible_types
    ]
    
    # Stage filter
    if target_stage:
        target_views = [v for v in target_views if v.get("stage") == target_stage]
    
    # View type filter (e.g., "floor plans" → "FloorPlan")
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
    # 3. RESOLVE TAG FAMILY
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
        
        # Store the pending NLP data in session so we can resume after user picks
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
    
    # Single tag family available — use it automatically
    selected_family = available_tags[0].get("family", "")
    selected_type = available_tags[0].get("type", "")
    
    # ─────────────────────────────────────────────────────
    # 4. DISPATCH
    # ─────────────────────────────────────────────────────
    stage_note = f" {target_stage}" if target_stage else ""
    debug_session(request, f"🚀 NLP Tag: {category} in {len(view_ids)} views using {selected_family}")
    
    # Post a chat message via the response (frontend will display this)
    # Then the revit_command will execute
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
    
    # Try to match the user's text against available tag families
    category = pending.get("category", "")
    inventory_key = f"ai_last_known_{category}_tags"
    available_tags = request.session.get(inventory_key, [])
    
    user_clean = user_text.strip()
    
    selected_family = None
    selected_type = None
    
    for tag in available_tags:
        tag_display = f"{tag['family']} : {tag['type']}"
        # Match if user pasted the full "Family : Type" or just the family name
        if (user_clean.lower() == tag_display.lower() or 
            user_clean.lower() == tag['family'].lower() or
            user_clean.lower() == tag['type'].lower()):
            selected_family = tag['family']
            selected_type = tag['type']
            break
    
    if not selected_family:
        # Didn't match — clear pending and let normal flow handle it
        request.session["ai_pending_nlp_tag"] = None
        request.session.modified = True
        return None
    
    # Clear pending state
    view_ids = pending.get("view_ids", [])
    view_count = pending.get("view_count", len(view_ids))
    stage = pending.get("stage", "")
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
