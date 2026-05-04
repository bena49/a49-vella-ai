# ai_router/ai_core/callback_handler.py
# Extracted from views.py — Revit list-result callback handlers

import json
import re
from rest_framework.response import Response

from ..ai_utils.formatters import (
    format_views_for_display, format_sheets_for_display,
    normalize_view_list, normalize_sheet_list,
    update_last_known_views, update_last_known_sheets
)


# =====================================================================
# SHARED HELPER: Restore pending_request_data into session
# =====================================================================

# Full key map used by list_views and list_scope_boxes callbacks
_FULL_PENDING_KEY_MAP = {
    "intent": "ai_pending_intent",
    "view_type": "ai_pending_view_type",
    "levels": "ai_pending_levels_parsed",
    "stage": "ai_pending_stage",
    "template": "ai_pending_template",
    "sheet_category": "ai_pending_sheet_category",
    "titleblock": "ai_pending_titleblock",
    "alignment_mode": "ai_pending_alignment_mode",
    "reference_sheet": "ai_pending_reference_sheet",
    "scope_box_id": "ai_pending_scope_box_id",
}

def _restore_pending_data(request, pending_data, key_map):
    """Restores pending_request_data fields into session using a key map."""
    for data_key, session_key in key_map.items():
        val = pending_data.get(data_key)
        if val is not None or session_key in ("ai_pending_intent",):
            request.session[session_key] = val
    request.session.modified = True


# =====================================================================
# CALLBACK: list_views_result
# =====================================================================

def handle_list_views_result(request, finalize_router_fn):
    """
    Handles the list_views_result callback from Revit/C#.
    
    Args:
        request: Django request with list_views_result in data
        finalize_router_fn: Reference to finalize_router for internal requests
    
    Returns:
        Response
    """
    raw_list = request.data.get("list_views_result", [])
    list_result = normalize_view_list(raw_list)
    update_last_known_views(request, list_result)

    # 💥 1. Internal Request?
    pending_data = request.session.get("ai_pending_request_data")
    if pending_data:
        _restore_pending_data(request, pending_data, _FULL_PENDING_KEY_MAP)
        return finalize_router_fn(request)

    # 💥 2. Explicit List Request?
    if request.session.get("ai_list_mode"):
        request.session["ai_list_mode"] = False
        return Response({
            "message": format_views_for_display(list_result),
            "done": True,
            "session_key": request.session.session_key
        })

    return Response({"message": "Views cached.", "session_key": request.session.session_key})


# =====================================================================
# CALLBACK: list_sheets_result
# =====================================================================

# Sheets only restores a subset of keys
_SHEETS_PENDING_KEY_MAP = {
    "intent": "ai_pending_intent",
    "alignment_mode": "ai_pending_alignment_mode",
    "reference_sheet": "ai_pending_reference_sheet",
}

def handle_list_sheets_result(request, finalize_router_fn):
    """
    Handles the list_sheets_result callback from Revit/C#.
    """
    raw_list = request.data.get("list_sheets_result", [])
    list_result = normalize_sheet_list(raw_list)
    update_last_known_sheets(request, list_result)

    # 💥 1. Internal Request?
    pending_data = request.session.get("ai_pending_request_data")
    if pending_data:
        _restore_pending_data(request, pending_data, _SHEETS_PENDING_KEY_MAP)
        return finalize_router_fn(request)

    # 💥 2. Explicit List Request?
    if request.session.get("ai_list_mode"):
        request.session["ai_list_mode"] = False
        return Response({
            "message": format_sheets_for_display(list_result),
            "done": True,
            "session_key": request.session.session_key
        })

    return Response({"message": "Sheets cached.", "session_key": request.session.session_key})


# =====================================================================
# CALLBACK: list_scope_boxes_result
# =====================================================================

def _parse_scope_boxes_result(raw_result):
    """Normalizes the various formats C# might send scope boxes in."""
    scope_boxes = []
    try:
        if isinstance(raw_result, str):
            parsed = json.loads(raw_result)
            if isinstance(parsed, dict):
                scope_boxes = parsed.get("scope_boxes", [])
            elif isinstance(parsed, list):
                scope_boxes = parsed
        elif isinstance(raw_result, list):
            scope_boxes = raw_result
        elif isinstance(raw_result, dict):
            scope_boxes = raw_result.get("scope_boxes", [])
    except:
        scope_boxes = []
    return scope_boxes


def handle_list_scope_boxes_result(request, finalize_router_fn):
    """
    Handles the list_scope_boxes_result callback from Revit/C#.
    """
    raw_result = request.data.get("list_scope_boxes_result")
    scope_boxes = _parse_scope_boxes_result(raw_result)

    request.session["ai_last_known_scope_boxes"] = scope_boxes
    request.session["ai_scope_box_checked"] = True
    request.session.modified = True

    # 💥 1. Internal Request?
    pending_data = request.session.get("ai_pending_request_data")
    if pending_data:
        _restore_pending_data(request, pending_data, _FULL_PENDING_KEY_MAP)
        return finalize_router_fn(request)

    # 💥 2. Explicit List Request?
    if request.session.get("ai_list_mode"):
        request.session["ai_list_mode"] = False
        msg = ("**Available Scope Boxes:**\n" + "\n".join([f"• {sb['name']}" for sb in scope_boxes])
               if scope_boxes else "No Scope Boxes found.")
        return Response({"message": msg, "done": True, "session_key": request.session.session_key})

    return Response({"message": "Scope boxes cached.", "session_key": request.session.session_key})


# =====================================================================
# CALLBACK: list_levels_result
# =====================================================================

def _parse_levels_result(raw_result):
    """
    Normalizes the get_levels response into a list of level-name strings.
    C# GetLevelsCommand may post the levels either:
      - as a JSON array of {name, elevation_mm} dicts (preferred, post-fix), or
      - as a stringified JSON-LIKE blob with a trailing comma before "]"
        (legacy GetLevelsCommand StringBuilder output).
    Both shapes are accepted.
    """
    if not raw_result:
        return []
    levels_data = raw_result
    if isinstance(raw_result, str):
        text = raw_result.strip()
        if not text:
            return []
        # Strip the legacy trailing-comma artifact ("[ ..., ]") so json.loads succeeds.
        text = re.sub(r",\s*\]", "]", text)
        try:
            levels_data = json.loads(text)
        except Exception:
            return []

    names = []
    if isinstance(levels_data, list):
        for entry in levels_data:
            if isinstance(entry, dict):
                name = entry.get("name")
                if name:
                    names.append(name)
            elif isinstance(entry, str) and entry.strip():
                names.append(entry.strip())
    return names


def handle_list_levels_result(request, finalize_router_fn):
    """
    Handles the list_levels_result callback from C# (sent by GetLevelsCommand).
    Populates ai_last_known_levels, resolves any pending level tokens against
    the actual project level names, then resumes the original chat command.
    """
    raw_result = request.data.get("list_levels_result")
    level_names = _parse_levels_result(raw_result)

    request.session["ai_last_known_levels"] = level_names
    request.session.modified = True

    pending_data = request.session.get("ai_pending_request_data")
    if pending_data:
        _restore_pending_data(request, pending_data, _FULL_PENDING_KEY_MAP)

        # Resolve raw tokens (e.g. SITE, TOP, L1, B1M) to the project's exact
        # Revit level names so the C# add-in can match them directly. Without
        # this step, tokens with no digit (SITE, TOP, RF) fail C#'s digit-based
        # fallback in projects where level names are Thai or otherwise
        # non-English.
        if level_names:
            try:
                from ..ai_engines.level_matcher import resolve_tokens_to_project_levels
                tokens = request.session.get("ai_pending_levels_parsed") or []
                if tokens:
                    resolved = resolve_tokens_to_project_levels(tokens, level_names)
                    if resolved and resolved != tokens:
                        request.session["ai_pending_levels_parsed"] = resolved
                        request.session.modified = True
            except Exception:
                # Best-effort; if resolution fails the existing C# fallback
                # still runs for digit-bearing tokens.
                pass

        return finalize_router_fn(request)

    return Response({"message": "Levels cached.", "session_key": request.session.session_key})


# =====================================================================
# DISPATCHER: Check all callbacks in one call
# =====================================================================

def handle_revit_callbacks(request, finalize_router_fn):
    """
    Checks request.data for any Revit list-result callback keys.

    Returns:
        Response if a callback was handled, None if no callback found.
    """
    data_keys = request.data.keys()

    if "list_views_result" in data_keys:
        return handle_list_views_result(request, finalize_router_fn)

    if "list_sheets_result" in data_keys:
        return handle_list_sheets_result(request, finalize_router_fn)

    if "list_scope_boxes_result" in data_keys:
        return handle_list_scope_boxes_result(request, finalize_router_fn)

    if "list_levels_result" in data_keys:
        return handle_list_levels_result(request, finalize_router_fn)

    # auto_dim_result arrives via Django if the C# bridge routes through backend.
    # In the standard flow it is sent directly via SendRawMessage to the frontend,
    # but this guard ensures it is silently consumed if it arrives here instead.
    if "auto_dim_result" in data_keys:
        return Response({"status": "silent"})

    return None