# ai_router/ai_commands/automate_tag.py
# Automate Tagging command handler - unified tagging for doors, windows,
# walls, rooms, and ceilings. Dispatches to the strategy-based C# orchestrator.

from rest_framework.response import Response

from ..ai_core.session_manager import reset_pending, debug_session
from ..ai_utils.envelope_builder import send_envelope, envelope_automate_tag


VALID_CATEGORIES = {"door", "window", "wall", "room", "ceiling", "spot_elevation"}


def handle_automate_tag(request):
    """
    Immediate command handler for the 'automate_tag' intent.

    Triggered from the AutomateTagWizard. The wizard submits the complete
    payload — category, tag family/type, view IDs, skip-tagged flag.

    Expected request.data keys:
        - tag_category: str ("door" | "window" | "wall" | "room" | "ceiling" | "spot_elevation")
        - tag_family: str  (ignored for spot_elevation — type is determined per view type)
        - tag_type: str
        - view_ids: list of int (Revit ElementId values)
        - skip_tagged: bool (default True)
        --- spot_elevation only ---
        - spot_plan_type: str    (SpotDimensionType name for FloorPlan views)
        - spot_section_type: str (SpotDimensionType name for Section views)
    """
    debug_session(request, "🏷️ Automate Tagging: Building envelope...")

    tag_category = request.data.get("tag_category", "").lower()
    tag_family = request.data.get("tag_family", "")
    tag_type = request.data.get("tag_type", "")
    view_ids = request.data.get("view_ids", [])
    skip_tagged = request.data.get("skip_tagged", True)

    if tag_category not in VALID_CATEGORIES:
        return Response({
            "message": f"❌ Invalid tag category '{tag_category}'. Must be one of: {', '.join(sorted(VALID_CATEGORIES))}."
        })

    if tag_category != "spot_elevation" and not tag_family:
        return Response({"message": f"❌ No {tag_category} tag family specified."})

    if not view_ids:
        return Response({"message": "❌ No views selected. Please select at least one view to tag."})

    reset_pending(request)

    env = envelope_automate_tag(tag_category, tag_family, tag_type, view_ids, skip_tagged)

    # Inject spot elevation type names into the envelope for C#
    if tag_category == "spot_elevation":
        env["raw"]["spot_plan_type"]    = request.data.get("spot_plan_type", "")
        env["raw"]["spot_section_type"] = request.data.get("spot_section_type", "")

    return send_envelope(request, env)
