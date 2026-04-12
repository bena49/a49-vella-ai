# ================================================================
# template_engine.py (VERSION - IMPORTED SOURCE)
# ================================================================

# 💥 IMPORT MASTER LIST FROM NAMING ENGINE (Single Source of Truth)
from .naming_engine import get_view_abbrev, VIEW_ABBREV

# ---------------------------------------------------------------
# A49 TEMPLATE MAPPING
# ---------------------------------------------------------------
TEMPLATE_MAP = {
    "WV": {
        "FL": ["A49_WV_FLOOR PLAN"],
        "SITE": ["A49_WV_SITE PLAN"],
        "CP": ["A49_WV_REFLECTED CEILING PLAN"],
        "EL": ["A49_WV_ELEVATION"],
        "SE": ["A49_WV_BUILDING SECTION"],
        "WS": ["A49_WV_WALL SECTION"],
    },

    "PD": {
        "FL": ["A49_PD_FLOOR PLAN", "A49_PD_PRESENTATION PLAN"],
        "SITE": ["A49_PD_SITE PLAN"],
        "CP": ["A49_PD_REFLECTED CEILING PLAN"],
        "EL": ["A49_PD_ELEVATION"],
        "SE": ["A49_PD_BUILDING SECTION"],
        "WS": ["A49_PD_WALL SECTION"],
        "SC": ["A49_PD_DOOR & WINDOW"],
        "D1": ["A49_PD_DETAILS"],
        "AP": ["A49_PD_EIA AREA PLAN", "A49_PD_NFA AREA PLAN"],
    },

    "DD": {
        "FL": ["A49_DD_FLOOR PLAN", "A49_DD_PRESENTATION PLAN"],
        "SITE": ["A49_DD_SITE PLAN"],
        "CP": ["A49_DD_REFLECTED CEILING PLAN"],
        "EL": ["A49_DD_ELEVATION"],
        "SE": ["A49_DD_WALL SECTION"],
        "WS": ["A49_DD_WALL SECTION"],
        "SC": ["A49_DD_DOOR & WINDOW"],
        "D1": ["A49_DD_DETAILS"],
        "AP": ["A49_DD_EIA AREA PLAN", "A49_DD_NFA AREA PLAN"],
    },

    "CD": {
        "A1": [
            "A49_CD_A1_FLOOR PLAN",
            "A49_CD_A1_FLOOR PLAN_COLOR"
        ],
        "SITE": [
            "A49_CD_A1_SITE PLAN",
            "A49_CD_A1_SITE PLAN_COLOR"
        ],
        "A2": ["A49_CD_A2_ELEVATION", "A49_CD_A2_ELEVATION_COLOR"],
        "A3": ["A49_CD_A3_BUILDING SECTION", "A49_CD_A3_BUILDING SECTION_COLOR"],
        "A4": ["A49_CD_A4_WALL SECTION", "A49_CD_A4_DETAIL SECTION", "A49_CD_A4_WALL SECTION_COLOR"],
        "A5": [
            "A49_CD_A5_REFLECTED CEILING PLAN", 
            "A49_CD_A5_REFLECTED CEILING PLAN_COLOR"
        ],
        "A6": ["A49_CD_A6_PATTERNS PLAN", "A49_CD_A6_INTERIOR ELEVATION"],
        "A7": ["A49_CD_A7_VERTICAL CIRCULATION PLAN", "A49_CD_A7_VERTICAL CIRCULATION_SECTION"],
        
        "A8": ["A49_CD_A8_DOOR & WINDOW"],
        
        "A9": ["A49_CD_A9_DETAILS"]
    }
}

# ---------------------------------------------------------------
# HELPERS
# ---------------------------------------------------------------
def get_abbrev(vt: str):
    """
    Wrapper for naming_engine.get_view_abbrev to keep API consistent.
    """
    return get_view_abbrev(vt)

# ---------------------------------------------------------------
# MAIN ENTRY POINT
# ---------------------------------------------------------------
def get_templates(stage: str, view_type: str, category: str = None, mode: str = "STANDARD"):
    if mode.upper() == "NONE":
        return {"available_templates": [], "default_template": None, "requires_user_selection": False}

    stage = (stage or "").upper()
    vt_abbrev = get_abbrev(view_type) # Uses naming_engine logic

    if stage in ("WV", "PD", "DD"):
        stage_templates = TEMPLATE_MAP.get(stage, {})
        templates = stage_templates.get(vt_abbrev, [])
        return {
            "available_templates": templates,
            "default_template": templates[0] if templates else None,
            "requires_user_selection": len(templates) > 1
        }

    if stage == "CD":
        VIEW_TO_CATEGORY_MAP = {
            "FL": "A1", "SITE": "A1", 
            "CP": "A5", "EL": "A2", "SE": "A3", 
            "WS": "A4", "PT": "A6", "ST": "A7", 
            "AD": "A8", "AW": "A8", "SC": "A8",
            "D1": "A9" 
        }
        
        sheet_category = category or VIEW_TO_CATEGORY_MAP.get(vt_abbrev)
        
        if not sheet_category:
            return {"available_templates": [], "default_template": None, "requires_user_selection": True}

        # 💥 SPECIAL LOOKUP LOGIC
        lookup_key = "SITE" if vt_abbrev == "SITE" else sheet_category
        
        cd_templates = TEMPLATE_MAP["CD"].get(lookup_key, []) 

        return {
            "available_templates": cd_templates,
            "default_template": cd_templates[0] if cd_templates else None,
            "requires_user_selection": len(cd_templates) > 1
        }

    return {"available_templates": [], "default_template": None, "requires_user_selection": True}

def validate_template(stage: str, view_type: str, category: str, selected_template: str, mode: str = "STANDARD"):
    if mode.upper() == "NONE": return True
    valid_info = get_templates(stage, view_type, category, mode)
    valid_list = valid_info["available_templates"]
    if not valid_list: return False
    return selected_template in valid_list