# ai_router/ai_commands/insert_standard_details.py
# Insert Standard Details command handler — wraps Revit's native
# ID_INSERT_VIEWS_FROM_FILE workflow for the A49 standard detail packages.
#
# Two modes:
#   - "preview": wizard requests file existence/version info on open
#   - "execute": user clicked Browse — copy path to clipboard + post Revit cmd
#
# Path config is read from standards.json's "standard_details_file" block,
# matching the pattern used by preflight's "template_file" config.

import os
import json

from rest_framework.response import Response

from ..ai_core.session_manager import reset_pending, debug_session
from ..ai_utils.envelope_builder import send_envelope, envelope_insert_standard_details


VALID_PACKAGES = {"standard", "eia"}
VALID_MODES = {"preview", "execute"}


def _load_standard_details_config():
    """
    Load the standard_details_file block from standards.json.
    Returns None if missing — handler will surface a clear error.
    """
    standards_path = os.path.join(
        os.path.dirname(os.path.dirname(__file__)),  # ai_router/
        "standards", "standards.json"
    )
    try:
        with open(standards_path, "r", encoding="utf-8") as f:
            standards_data = json.load(f)
        return standards_data.get("standard_details_file")
    except Exception:
        return None


def handle_insert_standard_details(request):
    """
    Immediate command handler for the 'insert_standard_details' intent.
    """
    package = (request.data.get("package") or "").lower()
    mode = (request.data.get("mode") or "execute").lower()

    if package not in VALID_PACKAGES:
        return Response({
            "message": f"❌ Invalid package '{package}'. Must be one of: {', '.join(sorted(VALID_PACKAGES))}."
        })

    if mode not in VALID_MODES:
        return Response({
            "message": f"❌ Invalid mode '{mode}'. Must be one of: {', '.join(sorted(VALID_MODES))}."
        })

    config = _load_standard_details_config()
    if not config:
        return Response({
            "message": "❌ standard_details_file config missing from standards.json. Cannot resolve file path."
        })

    debug_session(request, f"📚 Insert Standard Details: mode={mode}, package={package}")

    if mode == "execute":
        reset_pending(request)

    env = envelope_insert_standard_details(mode, package, config)
    return send_envelope(request, env)
