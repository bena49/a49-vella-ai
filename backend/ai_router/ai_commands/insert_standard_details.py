# ai_router/ai_commands/insert_standard_details.py
# Insert Standard Details command handler — wraps Revit's native
# ID_INSERT_VIEWS_FROM_FILE workflow for the A49 standard detail packages.
#
# Two modes:
#   - "preview": wizard requests file existence/version info on open
#   - "execute": user clicked Browse — copy path to clipboard + post Revit cmd

from rest_framework.response import Response

from ..ai_core.session_manager import reset_pending, debug_session
from ..ai_utils.envelope_builder import send_envelope, envelope_insert_standard_details


VALID_PACKAGES = {"standard", "eia"}
VALID_MODES = {"preview", "execute"}


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

    debug_session(request, f"📚 Insert Standard Details: mode={mode}, package={package}")

    if mode == "execute":
        reset_pending(request)

    env = envelope_insert_standard_details(mode, package)
    return send_envelope(request, env)
