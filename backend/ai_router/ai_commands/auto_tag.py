# ai_router/ai_commands/auto_tag.py
# Auto-Tag Doors command handler
# Receives tag config from the wizard, builds envelope, sends to C#

from rest_framework.response import Response

from ..ai_core.session_manager import reset_pending, debug_session
from ..ai_utils.envelope_builder import send_envelope, envelope_auto_tag_doors


def handle_auto_tag_doors(request):
    """
    Immediate command handler for 'auto_tag_doors' intent.
    
    This is triggered from the AutoTagWizard via the frontend.
    The wizard sends the full payload directly (tag_family, tag_type, 
    view_ids, skip_tagged) — no session slot-filling needed.
    
    Expected request.data keys:
        - tag_family: str (e.g. "Door Tag")
        - tag_type: str (e.g. "Standard")
        - view_ids: list of int (ElementId values)
        - skip_tagged: bool (default True)
    """
    debug_session(request, "🏷️ Auto-Tag Doors: Building envelope...")

    tag_family = request.data.get("tag_family", "")
    tag_type = request.data.get("tag_type", "")
    view_ids = request.data.get("view_ids", [])
    skip_tagged = request.data.get("skip_tagged", True)

    if not tag_family:
        return Response({"message": "❌ No door tag family specified. Please select a tag family."})

    if not view_ids:
        return Response({"message": "❌ No views selected. Please select at least one plan view to tag."})

    reset_pending(request)

    env = envelope_auto_tag_doors(tag_family, tag_type, view_ids, skip_tagged)
    return send_envelope(request, env)