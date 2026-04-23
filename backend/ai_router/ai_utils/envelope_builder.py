# ai_router/ai_utils/envelope_builder.py

from rest_framework.response import Response

def send_envelope(request, env):
    """
    Wraps an envelope into a Django Response.
    """
    if request.session.session_key:
        env["session_key"] = request.session.session_key
    
    return Response({
        "revit_command": env
    })

# =====================================================================
# VIEW CREATION ENVELOPE
# =====================================================================

def map_view_type_to_revit(raw_type):
    """
    Maps loose natural language to strict Revit API ViewFamily strings.
    """
    if not raw_type: return "FloorPlan"
    
    vt = raw_type.lower().strip()
    
    # AUTOMATION SUPPORT FOR A8/A9
    if "schedule" in vt: return "Schedule" # Requires C# support or will fail
    if "detail" in vt or "drafting" in vt: return "DraftingView" 

    # AREA PLAN SUPPORT
    if "area" in vt: return "AreaPlan"

    # STANDARD
    if "ceiling" in vt: return "CeilingPlan" 
    if "reflected" in vt: return "CeilingPlan"
    if "rcp" in vt: return "CeilingPlan"
    
    if "site" in vt: return "FloorPlan" 
    if "floor" in vt: return "FloorPlan"
    if "plan" in vt: return "FloorPlan" 
    
    # BLOCKED BY GATEKEEPER (But mapped just in case)
    if "elevation" in vt: return "Elevation"
    if "section" in vt: return "Section"
    if "3d" in vt: return "ThreeD"
    
    return "FloorPlan"

def envelope_create_views(view_items):
    # Sanitize view_type for C# Plugin
    for v in view_items:
        raw = v.get("view_type", "")
        # Apply mapping here before sending to C#
        v["view_type"] = map_view_type_to_revit(raw)
        
    return {
        "command": "create_views",
        "views": view_items
    }

# =====================================================================
# SHEET CREATION ENVELOPE
# =====================================================================

def envelope_create_sheets(sheet_items):
    return {
        "command": "create_sheets",
        "sheets": sheet_items
    }

# =====================================================================
# MODIFICATION ENVELOPES
# =====================================================================

def envelope_rename_view(target, newname):
    return {
        "command": "rename_view",
        "target": target,
        "new_name": newname
    }

def envelope_rename_sheet(target, newname):
    return {
        "command": "rename_sheet",
        "target": target,
        "new_name": newname
    }

def envelope_duplicate_view(target, mode):
    return {
        "command": "duplicate_view",
        "target": target,
        "duplicate_mode": mode
    }

def envelope_apply_template(target, template):
    return {
        "command": "apply_template",
        "target": target,
        "template": template
    }

# =====================================================================
# PLACEMENT ENVELOPES
# =====================================================================

def envelope_place_view_on_sheet(view_name, sheet_number):
    return {
        "command": "place_view_on_sheet",
        "view": view_name,
        "sheet": sheet_number
    }

def envelope_remove_view_from_sheet(view_name, sheet_number):
    return {
        "command": "remove_view_from_sheet",
        "view": view_name,
        "sheet": sheet_number
    }

# =====================================================================
# PREFLIGHT ENVELOPES
# =====================================================================

def envelope_preflight_check(standards_data):
    return {
        "command": "preflight_check",
        "raw": standards_data
    }

def envelope_preflight_repair(standards_data, preflight_result):
    return {
        "command": "preflight_repair",
        "raw": {
            "standards": standards_data,
            "preflight_result": preflight_result
        }
    }

# =====================================================================
# AUTO-TAG ENVELOPES
# =====================================================================

def envelope_automate_tag(tag_category, tag_family, tag_type, view_ids, skip_tagged=True):
    return {
        "command": "automate_tag",
        "raw": {
            "tag_category": tag_category,
            "tag_family": tag_family,
            "tag_type": tag_type,
            "view_ids": view_ids,
            "skip_tagged": skip_tagged
        }
    }

# =====================================================================
# AUTO-DIM ENVELOPE
# =====================================================================

def envelope_automate_dim(
    view_ids,
    include_openings=True,
    include_grids=True,
    include_total=True,
    include_grids_only=True,
    include_detail=True,
    offset_mm=800,
    inset_mm=1000,
    smart_exterior=True,
    dim_type_name="",
):
    """
    Builds the envelope for the auto_dim C# command.

    Layer stacking (exterior, outermost first):
        Layer 1 — include_total:      Overall/total building dimension
        Layer 2 — include_grids_only: Grid-to-grid spacing
        Layer 3 — include_detail:     Detail perimeter (interior strings)

    Args:
        view_ids:           list of int Revit ElementId values
        include_openings:   include door/window edge references in wall strings
        include_grids:      include structural grid references
        include_total:      Layer 1 — overall dimension string (outermost)
        include_grids_only: Layer 2 — grid-to-grid string (middle)
        include_detail:     Layer 3 — detail/interior string (innermost)
        offset_mm:          base spacing between exterior layer strings (mm)
        inset_mm:           offset from wall edge for Layer 3 interior string (mm)
        smart_exterior:     exterior walls dimension outward from building perimeter
        dim_type_name:      name of DimensionType in Revit (empty = auto-select first linear)
    """
    if not isinstance(view_ids, list) or not all(isinstance(v, int) for v in view_ids):
        # Coerce to int list — handles JSON strings from session cache
        view_ids = [int(v) for v in view_ids if str(v).lstrip("-").isdigit()]

    return {
        "command": "auto_dim",
        "raw": {
            "view_ids":           view_ids,
            "include_openings":   include_openings,
            "include_grids":      include_grids,
            "include_total":      include_total,
            "include_grids_only": include_grids_only,
            "include_detail":     include_detail,
            "offset_mm":          offset_mm,
            "inset_mm":           inset_mm,
            "smart_exterior":     smart_exterior,
            "dim_type_name":      dim_type_name,
        }
    }


