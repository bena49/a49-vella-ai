# ai_router/ai_commands/preflight.py
# Extracted from views.py — Preflight Check + Repair command logic

import os
import json
from rest_framework.response import Response

from ..ai_core.session_manager import reset_pending, debug_session
from ..ai_utils.envelope_builder import send_envelope, envelope_preflight_check, envelope_preflight_repair


def handle_preflight_check(request):
    """
    Immediate command handler for 'preflight_check' intent.
    Loads standards.json, stores in session, sends check envelope to C#.
    """
    standards_path = os.path.join(
        os.path.dirname(os.path.dirname(__file__)),  # ai_router/
        "standards", "standards.json"
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


def handle_preflight_repair_interceptor(request, clean_text):
    """
    Interceptor for preflight repair confirmation.
    Called from ai_router when ai_expecting_preflight_repair is True.
    
    Returns:
        Response if handled (confirm or cancel), None if not matched.
    """
    if not request.session.get("ai_expecting_preflight_repair"):
        return None

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

    return None