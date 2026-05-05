# ============================================================================
# rename_preview.py — wizard-facing handlers for the Phase 2 Renumber/Rename
# wizard. These are stateless: the wizard sends the inventory + an operation
# spec, the backend returns a preview (or the operation registry) and never
# touches Revit. The wizard then submits filtered updates via the existing
# `execute_batch_update` envelope (handled in C# / ExecuteBatchUpdateCommand).
#
# WIRED INTO
# ──────────
#   intent_router.py — both intents are added to ALLOWED_IMMEDIATE_COMMANDS
#   and dispatched here from dispatch_immediate_command(). Frontend calls them
#   via useChat.submitDirect({ message: "rename_preview", ... }) which bypasses
#   the chat side-effects (no isThinking flag, no message bubble) and returns
#   the raw JSON to the wizard.
# ============================================================================

from rest_framework.response import Response

from ..ai_engines.rename_pattern_engine import (
    compute_rename_preview,
    list_operations,
)
from ..ai_engines.naming_engine import SCHEMES


def handle_rename_preview(request):
    """Compute a rename preview for the wizard's diff table.

    Expected request.data:
      inventory:  list of sheet items (from FetchProjectInventoryCommand).
                  Each item: {unique_id, number, name, category, stage}
      operation:  operation name (str) — see list_rename_operations
      params:     dict of operation-specific params (optional, defaults {})
      selection:  optional list[str] of unique_ids to limit preview to

    Returns:
      {
        "status":        "success" | "error",
        "preview":       [...rows],            # see compute_rename_preview()
        "row_count":     int,                  # total rows returned
        "changed_count": int,                  # rows whose number/name changed
        "warning_count": int,                  # rows carrying any warnings
      }
    """
    data = request.data or {}
    inventory = data.get("inventory") or []
    operation = data.get("operation")
    params = data.get("params") or {}
    selection = data.get("selection")

    if not operation:
        return Response({
            "status": "error",
            "message": "rename_preview: missing 'operation' field",
        })
    if not isinstance(inventory, list):
        return Response({
            "status": "error",
            "message": "rename_preview: 'inventory' must be a list",
        })

    spec = {"operation": operation, "params": params}
    rows = compute_rename_preview(inventory, spec, selection=selection)

    return Response({
        "status":        "success",
        "preview":       rows,
        "row_count":     len(rows),
        "changed_count": sum(1 for r in rows if r.get("changed")),
        "warning_count": sum(1 for r in rows if r.get("warnings")),
    })


def handle_list_rename_operations(request):
    """Return the registered operations + scheme names so the wizard can build
    its operation-picker and scheme-convert dropdowns without hardcoding."""
    return Response({
        "status":     "success",
        "operations": list_operations(),
        "schemes":    list(SCHEMES.keys()),
    })
