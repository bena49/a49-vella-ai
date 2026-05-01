# ai_router/ai_commands/automate_dim.py
# Automate Dimensioning command handler.
# Dispatches to the strategy-based C# orchestrator (AutoDimCommand).
# NLP / conversational flow removed — all requests open the Wizard.

from rest_framework.response import Response

from ..ai_core.session_manager import reset_pending, debug_session
from ..ai_utils.envelope_builder import send_envelope, envelope_automate_dim


def handle_automate_dim(request):
    """
    Immediate command handler for the 'automate_dim' intent.
    Called when the user submits the AutomateDimWizard.

    Expected request.data keys:
        - view_ids:             list of int (Revit ElementId values)
        - include_openings:     bool (default True)
        - include_grids:        bool (default True)
        - include_total:        bool - Layer 1: Overall/Total (default True)
        - include_grids_only:   bool - Layer 2: Grid-to-Grid (default True)
        - include_detail:       bool - Layer 3: Detail/interior strings (default True)
        - offset_mm:            int  (default 1600) -- spacing between exterior layers
        - inset_mm:             int  (default 1200) -- offset for Layer 3 interior string
        - depth_mm:             int  (default 5000) -- extension length for dimension lines
        - include_interior:     bool (default True) -- whether to include interior dimensions at all (if False, Layer 3 is only exterior detail)
        - smart_exterior:       bool (default True)
        - dim_type_name:        str  (e.g. "A49_Linear", empty = auto-select)
    """
    debug_session(request, "Automate Dimensioning: Building envelope...")

    view_ids           = request.data.get("view_ids", [])
    include_openings   = request.data.get("include_openings", True)
    include_grids      = request.data.get("include_grids", True)
    include_total      = request.data.get("include_total", True)
    include_grids_only = request.data.get("include_grids_only", True)
    include_detail     = request.data.get("include_detail", True)
    offset_mm          = request.data.get("offset_mm", 1600)
    inset_mm           = request.data.get("inset_mm", 1200)
    depth_mm           = request.data.get("depth_mm", 5000)
    include_interior   = request.data.get("include_interior", True)
    smart_exterior     = request.data.get("smart_exterior", True)
    dim_type_name      = request.data.get("dim_type_name", "")

    if not view_ids:
        return Response({
            "message": "No views selected. Please select at least one floor plan view to dimension."
        })

    reset_pending(request)

    env = envelope_automate_dim(
        view_ids=view_ids,
        include_openings=include_openings,
        include_grids=include_grids,
        include_total=include_total,
        include_grids_only=include_grids_only,
        include_detail=include_detail,
        offset_mm=offset_mm,
        inset_mm=inset_mm,
        depth_mm=depth_mm,
        include_interior=include_interior,
        smart_exterior=smart_exterior,
        dim_type_name=dim_type_name,
    )
    return send_envelope(request, env)
