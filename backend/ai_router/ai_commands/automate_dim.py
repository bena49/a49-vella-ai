# ai_router/ai_commands/automate_dim.py
# Automate Dimensioning command handler.
# Dispatches to the strategy-based C# orchestrator (AutoDimCommand).

from urllib import request

from rest_framework.response import Response

from ..ai_core.session_manager import reset_pending, debug_session
from ..ai_utils.envelope_builder import send_envelope, envelope_automate_dim


def handle_automate_dim(request):
    """
    Immediate command handler for the 'automate_dim' intent.

    Expected request.data keys:
        - view_ids:           list of int (Revit ElementId values)
        - include_openings:   bool (default True)
        - include_grids:      bool (default True)
        - include_total:      bool (Layer 1)
        - include_grids_only: bool (Layer 2)
        - offset_mm:          int  (default 800)
        - smart_exterior:     bool (default True)
        - dim_type_name:      str  (e.g. "A49_Linear")
    """
    debug_session(request, "📐 Automate Dimensioning: Building envelope...")

    view_ids        = request.data.get("view_ids", [])
    include_openings = request.data.get("include_openings", True)
    include_grids   = request.data.get("include_grids", True)

    include_total = request.data.get("include_total", True)
    include_grids_only = request.data.get("include_grids_only", True)

    offset_mm       = request.data.get("offset_mm", 800)
    inset_mm        = request.data.get("inset_mm", 1000)
    smart_exterior  = request.data.get("smart_exterior", True)
    dim_type_name   = request.data.get("dim_type_name", "")

    if not view_ids:
        return Response({
            "message": "❌ No views selected. Please select at least one floor plan view to dimension."
        })

    reset_pending(request)

    env = envelope_automate_dim(
        view_ids=view_ids,
        include_openings=include_openings,
        include_grids=include_grids,
        include_total=include_total,       # Pass to envelope
        include_grids_only=include_grids_only, # Pass to envelope
        offset_mm=offset_mm,
        inset_mm=inset_mm,
        smart_exterior=smart_exterior,
        dim_type_name=dim_type_name,
)
    return send_envelope(request, env)


# ============================================================================
# LIGHTWEIGHT NLP FLOW
# ============================================================================

STAGE_INPUT_MAP = {
    "wv": "WV", "pd": "PD", "dd": "DD", "cd": "CD",
    "1": "WV",  "2": "PD",  "3": "DD",  "4": "CD",
    "working view": "WV", "pre-design": "PD",
    "design development": "DD", "construction documents": "CD",
    "construction": "CD",
}


def _get_dim_state(request):
    return request.session.get("ai_nlp_dim_state", None)

def _set_dim_state(request, state):
    request.session["ai_nlp_dim_state"] = state
    request.session.modified = True

def _clear_dim_state(request):
    request.session["ai_nlp_dim_state"] = None
    request.session.modified = True


def _get_cached_floor_plan_views(request):
    views = request.session.get("ai_last_known_floor_plan_views", [])
    if views:
        return views
    all_views = request.session.get("ai_last_known_taggable_views", [])
    return [v for v in all_views if v.get("view_type") == "FloorPlan"]


def _filter_dim_views(request, stage="", view_name=""):
    views = _get_cached_floor_plan_views(request)
    if view_name:
        name_upper = view_name.upper()
        matched = [v for v in views if v.get("name", "").upper() == name_upper]
        if not matched:
            matched = [v for v in views if name_upper in v.get("name", "").upper()]
        return matched
    if stage:
        views = [v for v in views if v.get("stage", "") == stage]
    return views


def _dispatch_dim(request, state):
    view_ids = state["view_ids"]
    debug_session(request, f"📐 NLP Dim: {len(view_ids)} views")
    _clear_dim_state(request)
    env = envelope_automate_dim(
        view_ids=view_ids,
        include_openings=True,
        include_grids=True,
        offset_mm=800,
        smart_exterior=True,
        dim_type_name="",
    )
    return send_envelope(request, env)


def handle_automate_dim_nlp(request, nlp_data):
    stage     = nlp_data.get("stage", "").upper()
    view_name = nlp_data.get("view_name", "")

    if view_name:
        views = _filter_dim_views(request, view_name=view_name)
        if not views:
            cached = _get_cached_floor_plan_views(request)
            if not cached:
                return Response({
                    "message": (
                        "📐 I don't have your floor plan views loaded yet. "
                        "Open the **Dimension Wizard** first, then try again."
                    ),
                    "intent": "wizard:automate_dim"
                })
            return Response({
                "message": f"I couldn't find a view named '{view_name}'. Please check the name."
            })
        if len(views) > 1:
            view_list = "\n".join([f"• {v['name']}" for v in views])
            _set_dim_state(request, {"step": "awaiting_specific_view", "stage": stage, "view_ids": []})
            return Response({"message": f"Found {len(views)} matching '{view_name}':\n{view_list}\n\nPlease provide the exact view name:"})
        return _dispatch_dim(request, {"view_ids": [views[0]["id"]]})

    if stage:
        return _ask_view_confirmation(request, {"stage": stage, "view_ids": []})

    return _ask_stage(request, {"stage": "", "view_ids": []})


def handle_nlp_dim_conversation(request, user_text):
    state = _get_dim_state(request)
    if not state:
        return None

    step      = state.get("step")
    txt_lower = user_text.strip().lower()

    if txt_lower in ["cancel", "stop", "nevermind", "quit", "exit"]:
        _clear_dim_state(request)
        return Response({"message": "No problem. Dimensioning cancelled."})

    if step == "awaiting_stage":
        resolved = STAGE_INPUT_MAP.get(txt_lower)
        if not resolved:
            return Response({"message": "Please choose a stage: WV, PD, DD, or CD"})
        state["stage"] = resolved
        return _ask_view_confirmation(request, state)

    elif step == "awaiting_view_confirm":
        if txt_lower in ["yes", "y", "all", "sure", "ok", "confirm", "proceed"]:
            state["view_ids"] = [v["id"] for v in state.get("matched_views", [])]
            return _dispatch_dim(request, state)
        elif txt_lower in ["no", "n", "specific", "select"]:
            state["step"] = "awaiting_specific_view"
            _set_dim_state(request, state)
            return Response({"message": "Please provide the specific floor plan view name:"})
        else:
            views = _filter_dim_views(request, view_name=user_text.strip())
            if views:
                state["view_ids"] = [v["id"] for v in views]
                return _dispatch_dim(request, state)
            return Response({"message": "Say **Yes** to dimension all, **No** to name a specific view, or type a view name."})

    elif step == "awaiting_specific_view":
        views = _filter_dim_views(request, view_name=user_text.strip())
        if not views:
            return Response({"message": f"Couldn't find '{user_text.strip()}'. Try again or say 'cancel'."})
        if len(views) > 1:
            view_list = "\n".join([f"• {v['name']}" for v in views[:10]])
            _set_dim_state(request, state)
            return Response({"message": f"Still found {len(views)} matches:\n{view_list}\n\nPlease give the exact name:"})
        state["view_ids"] = [views[0]["id"]]
        return _dispatch_dim(request, state)

    _clear_dim_state(request)
    return None


def _ask_stage(request, state):
    state["step"] = "awaiting_stage"
    _set_dim_state(request, state)
    return Response({"message": "📐 Which design stage should I dimension?\n• WV\n• PD\n• DD\n• CD"})


def _ask_view_confirmation(request, state):
    stage = state.get("stage", "")
    views = _filter_dim_views(request, stage=stage)

    if not views:
        cached = _get_cached_floor_plan_views(request)
        if not cached:
            _clear_dim_state(request)
            return Response({
                "message": (
                    "📐 I don't have your floor plan views loaded yet. "
                    "Open the **Dimension Wizard** first, then try again."
                ),
                "intent": "wizard:automate_dim"
            })
        _clear_dim_state(request)
        stage_label = f" in {stage}" if stage else ""
        return Response({"message": f"No floor plan views found{stage_label}. Try a different stage or open the Dimension Wizard."})

    state["matched_views"] = views
    state["step"] = "awaiting_view_confirm"
    _set_dim_state(request, state)

    stage_label = f"{stage} " if stage else ""
    view_list = "\n".join([f"• {v['name']}" for v in views[:15]])
    suffix = f"\n  ...and {len(views) - 15} more" if len(views) > 15 else ""

    return Response({
        "message": (
            f"Found {len(views)} {stage_label}floor plan view(s):\n{view_list}{suffix}\n\n"
            f"Shall I dimension all {len(views)}? Say **Yes** to proceed or **No** to name a specific view."
        )
    })
