# ai_router/ai_commands/automate_dim.py
# Automate Dimensioning command handler.
# Dispatches to the strategy-based C# orchestrator (AutoDimCommand).
# Mirrors automate_tag.py structure exactly.

from rest_framework.response import Response

from ..ai_core.session_manager import reset_pending, debug_session
from ..ai_utils.envelope_builder import send_envelope, envelope_automate_dim


def handle_automate_dim(request):
    """
    Immediate command handler for the 'automate_dim' intent.

    Triggered from AutomateDimWizard. The wizard submits the complete
    payload — view IDs, what to reference, offset, and dim type name.

    Expected request.data keys:
        - view_ids:                 list of int (Revit ElementId values)
        - include_openings:         bool (default True)
        - include_intersecting:     bool (default True)
        - include_grids:            bool (default True)
        - offset_mm:                int  (default 800)
        - smart_exterior_placement: bool (default True)
        - dim_type_name:            str  (e.g. "A49_Linear")
    """
    debug_session(request, "📐 Automate Dimensioning: Building envelope...")

    view_ids                 = request.data.get("view_ids", [])
    include_openings         = request.data.get("include_openings", True)
    include_intersecting     = request.data.get("include_intersecting", True)
    include_grids            = request.data.get("include_grids", True)
    offset_mm                = request.data.get("offset_mm", 800)
    smart_exterior_placement = request.data.get("smart_exterior_placement", True)
    dim_type_name            = request.data.get("dim_type_name", "")

    if not view_ids:
        return Response({
            "message": "❌ No views selected. Please select at least one floor plan view to dimension."
        })

    reset_pending(request)

    env = envelope_automate_dim(
        view_ids=view_ids,
        include_openings=include_openings,
        include_intersecting=include_intersecting,
        include_grids=include_grids,
        offset_mm=offset_mm,
        smart_exterior_placement=smart_exterior_placement,
        dim_type_name=dim_type_name,
    )
    return send_envelope(request, env)


# ============================================================================
# LIGHTWEIGHT NLP FLOW
# ============================================================================
# Four states only (vs tag NLP's six — no view type or tag family steps):
#
#   awaiting_stage        → "Which stage? WV / PD / DD / CD"
#   awaiting_view_confirm → "Found N views. Dimension all?"
#   awaiting_specific_view→ "Please name the specific view"
#   → dispatch            → sends envelope to Revit
#
# Stage and/or view name pre-extracted by fast_route_intent are skipped.
# No cache dependency — floor_plan_views come from session if present,
# otherwise user is nudged to open the wizard (which fetches them on open).
# ============================================================================

# Stage input normalisation — mirrors tag NLP pattern
STAGE_INPUT_MAP = {
    "wv": "WV", "pd": "PD", "dd": "DD", "cd": "CD",
    "1": "WV",  "2": "PD",  "3": "DD",  "4": "CD",
    "working view": "WV", "pre-design": "PD",
    "design development": "DD", "construction documents": "CD",
    "construction": "CD",
}


# ── Session state helpers ────────────────────────────────────────────────────

def _get_dim_state(request):
    return request.session.get("ai_nlp_dim_state", None)

def _set_dim_state(request, state):
    request.session["ai_nlp_dim_state"] = state
    request.session.modified = True

def _clear_dim_state(request):
    request.session["ai_nlp_dim_state"] = None
    request.session.modified = True


# ── View filtering ───────────────────────────────────────────────────────────

def _get_cached_floor_plan_views(request):
    """
    Returns floor plan views from session cache.
    The cache is populated when AutomateDimWizard opens (fetch_project_info).
    Falls back to taggable_views filtered to FloorPlan if specific cache absent.
    """
    # Primary: dedicated floor plan cache set by updateWizardProps
    views = request.session.get("ai_last_known_floor_plan_views", [])
    if views:
        return views

    # Fallback: filter taggable_views (populated by tag wizard open)
    all_views = request.session.get("ai_last_known_taggable_views", [])
    return [v for v in all_views if v.get("view_type") == "FloorPlan"]


def _filter_dim_views(request, stage="", view_name=""):
    """Filter cached floor plan views by stage and/or name."""
    views = _get_cached_floor_plan_views(request)

    if view_name:
        name_upper = view_name.upper()
        matched = [v for v in views if v.get("name", "").upper() == name_upper]
        if not matched:
            matched = [v for v in views if name_upper in v.get("name", "").upper()]
        if not matched:
            matched = [v for v in views if v.get("name", "").upper().startswith(name_upper)]
        return matched

    if stage:
        views = [v for v in views if v.get("stage", "") == stage]

    return views


# ── Dispatch ─────────────────────────────────────────────────────────────────

def _dispatch_dim(request, state):
    """Build envelope and fire to Revit. Uses all defaults except view_ids."""
    view_ids = state["view_ids"]
    debug_session(request, f"📐 NLP Dim: dimensioning {len(view_ids)} floor plan views")
    _clear_dim_state(request)

    env = envelope_automate_dim(
        view_ids=view_ids,
        include_openings=True,
        include_intersecting=True,
        include_grids=True,
        offset_mm=800,
        smart_exterior_placement=True,
        dim_type_name="",        # C# falls back to first linear type in project
    )
    return send_envelope(request, env)


# ── Main entry point ─────────────────────────────────────────────────────────

def handle_automate_dim_nlp(request, nlp_data):
    """
    Starts the conversational dimensioning flow.
    Called from intent_router.dispatch_immediate_command for 'automate_dim_nlp'.

    nlp_data keys (all optional, pre-extracted by fast_route_intent):
        stage     — e.g. "CD"
        view_name — e.g. "CD_A1_FL_01"
    """
    stage     = nlp_data.get("stage", "").upper()
    view_name = nlp_data.get("view_name", "")

    debug_session(request, f"📐 NLP Dim: stage={stage}, view_name={view_name}")

    # ── Power user: specific view named → resolve and dispatch immediately ──
    if view_name:
        views = _filter_dim_views(request, view_name=view_name)

        if not views:
            # No cache yet — nudge to wizard which will populate it
            cached = _get_cached_floor_plan_views(request)
            if not cached:
                return Response({
                    "message": (
                        "📐 I don't have your floor plan views loaded yet. "
                        "Open the **Dimension Wizard** first — it will load your views — "
                        "then try the command again, or select views directly in the wizard."
                    ),
                    "intent": "wizard:automate_dim"
                })
            return Response({
                "message": f"I couldn't find a view named '{view_name}'. "
                           f"Please check the name and try again, or say 'cancel' to stop."
            })

        if len(views) > 1:
            view_list = "\n".join([f"• {v['name']}" for v in views])
            state = {
                "step": "awaiting_specific_view",
                "stage": stage,
                "view_ids": [],
            }
            _set_dim_state(request, state)
            return Response({
                "message": (
                    f"I found {len(views)} views matching '{view_name}':\n{view_list}\n\n"
                    f"Please provide the exact view name:"
                )
            })

        # Exactly one match — dispatch straight away
        state = {"view_ids": [views[0]["id"]]}
        return _dispatch_dim(request, state)

    # ── Stage pre-extracted → find views and confirm ────────────────────────
    if stage:
        return _ask_view_confirmation(request, {"stage": stage, "view_ids": []})

    # ── Nothing extracted → ask stage ───────────────────────────────────────
    return _ask_stage(request, {"stage": "", "view_ids": []})


# ── Conversation interceptor ─────────────────────────────────────────────────

def handle_nlp_dim_conversation(request, user_text):
    """
    Processes user replies during an active dimensioning conversation.
    Called from views.py interceptor when ai_nlp_dim_state exists.

    Returns Response if handled, None if no active state.
    """
    state = _get_dim_state(request)
    if not state:
        return None

    step      = state.get("step")
    txt       = user_text.strip()
    txt_lower = txt.lower().strip()

    # Cancel at any point
    if txt_lower in ["cancel", "stop", "nevermind", "never mind", "quit", "exit"]:
        _clear_dim_state(request)
        return Response({"message": "No problem. Dimensioning cancelled."})

    # ── awaiting_stage ───────────────────────────────────────────────────────
    if step == "awaiting_stage":
        resolved = STAGE_INPUT_MAP.get(txt_lower)
        if not resolved:
            return Response({
                "message": "I didn't catch that. Please choose a stage: WV, PD, DD, or CD"
            })
        state["stage"] = resolved
        return _ask_view_confirmation(request, state)

    # ── awaiting_view_confirm ────────────────────────────────────────────────
    elif step == "awaiting_view_confirm":
        if txt_lower in ["yes", "y", "all", "sure", "ok", "confirm", "proceed", "tag all"]:
            state["view_ids"] = [v["id"] for v in state.get("matched_views", [])]
            return _dispatch_dim(request, state)

        elif txt_lower in ["no", "n", "specific", "select", "choose"]:
            state["step"] = "awaiting_specific_view"
            _set_dim_state(request, state)
            return Response({"message": "Please provide the specific floor plan view name:"})

        else:
            # Maybe they typed a view name directly
            views = _filter_dim_views(request, view_name=txt)
            if views:
                state["view_ids"] = [v["id"] for v in views]
                return _dispatch_dim(request, state)

            return Response({
                "message": "Please say **Yes** to dimension all, **No** to name a specific view, "
                           "or type a view name directly."
            })

    # ── awaiting_specific_view ───────────────────────────────────────────────
    elif step == "awaiting_specific_view":
        views = _filter_dim_views(request, view_name=txt)

        if not views:
            return Response({
                "message": f"I couldn't find a view matching '{txt}'. "
                           f"Please check the name and try again, or say 'cancel' to stop."
            })

        if len(views) > 1:
            view_list = "\n".join([f"• {v['name']}" for v in views[:10]])
            suffix = f"\n  ...and {len(views) - 10} more" if len(views) > 10 else ""
            _set_dim_state(request, state)
            return Response({
                "message": (
                    f"I still found {len(views)} views matching '{txt}':\n{view_list}{suffix}\n\n"
                    f"Please provide the exact view name:"
                )
            })

        state["view_ids"] = [views[0]["id"]]
        return _dispatch_dim(request, state)

    # Unknown step — clear state and let normal routing handle it
    _clear_dim_state(request)
    return None


# ── Step functions ───────────────────────────────────────────────────────────

def _ask_stage(request, state):
    """Ask which design stage to dimension."""
    state["step"] = "awaiting_stage"
    _set_dim_state(request, state)
    return Response({
        "message": "📐 Sure! Which design stage should I dimension?\n• WV\n• PD\n• DD\n• CD"
    })


def _ask_view_confirmation(request, state):
    """Show matched floor plan views and ask to dimension all or pick specific."""
    stage = state.get("stage", "")
    views = _filter_dim_views(request, stage=stage)

    if not views:
        # No cache yet
        cached = _get_cached_floor_plan_views(request)
        if not cached:
            _clear_dim_state(request)
            return Response({
                "message": (
                    "📐 I don't have your floor plan views loaded yet. "
                    "Open the **Dimension Wizard** first — it will load your views — "
                    "then try the command again."
                ),
                "intent": "wizard:automate_dim"
            })
        _clear_dim_state(request)
        stage_label = f" in {stage}" if stage else ""
        return Response({
            "message": (
                f"I couldn't find any floor plan views{stage_label}. "
                f"Please check the stage or open the Dimension Wizard to select views manually."
            )
        })

    state["matched_views"] = views
    state["step"] = "awaiting_view_confirm"
    _set_dim_state(request, state)

    stage_label = f"{stage} " if stage else ""
    view_list = "\n".join([f"• {v['name']}" for v in views[:15]])
    suffix = f"\n  ...and {len(views) - 15} more" if len(views) > 15 else ""

    return Response({
        "message": (
            f"I found {len(views)} {stage_label}floor plan view(s):\n{view_list}{suffix}\n\n"
            f"Shall I dimension all {len(views)}? "
            f"Say **Yes** to proceed, or **No** to name a specific view."
        )
    })

