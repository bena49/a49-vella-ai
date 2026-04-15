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

def envelope_auto_tag_doors(tag_family, tag_type, view_ids, skip_tagged=True):
    return {
        "command": "auto_tag_doors",
        "raw": {
            "tag_family": tag_family,
            "tag_type": tag_type,
            "view_ids": view_ids,
            "skip_tagged": skip_tagged
        }
    }