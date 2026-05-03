# =====================================================================
# titleblock_engine.py (FINAL)
#
# Centralized titleblock management for A49 workflows.
#
# Handles:
#   - List of available titleblocks
#   - Cover sheet automatic logic (A0.00)
#   - Validation of user-selected titleblocks
#   - Parsing text like "horizontal plan sheet" into family + type
#   - Override logic
#   - Default titleblock storage (session-based)
# =====================================================================

# ---------------------------------------------------------------------
# A49 Standard Titleblocks (already embedded in Revit templates)
# ---------------------------------------------------------------------
TITLEBLOCKS = {
    "standard": {
        "A49_TB_A1_Horizontal": ["Plan Sheet", "Detail Sheet"],
        "A49_TB_A1_Vertical":   ["Plan Sheet", "Detail Sheet"],
    },
    "cover": {
        "A49_TB_A1_Horizontal_Cover": ["Cover"]
    }
}

# Session storage for default titleblock
DEFAULT_TITLEBLOCK = {
    "family": None,
    "type": None
}


# ---------------------------------------------------------------------
# Helper: Return list of standard titleblocks for user selection
# ---------------------------------------------------------------------
def get_standard_titleblocks():
    options = []
    for family, types in TITLEBLOCKS["standard"].items():
        for t in types:
            options.append(f"{family} : {t}")
    return options


# ---------------------------------------------------------------------
# Helper: Return cover titleblock (for sheet A0.00 only)
# ---------------------------------------------------------------------
def get_cover_titleblock():
    for family, types in TITLEBLOCKS["cover"].items():
        return family, types[0]  # Always only one type: "Cover"


# ---------------------------------------------------------------------
# Helper: Parse user’s natural language selection
#
# Examples:
#   "horizontal plan sheet"
#   "vertical detail"
#   "A49_TB_A1_Horizontal Plan Sheet"
# ---------------------------------------------------------------------
def parse_titleblock_from_user_text(text):
    if not text:
        return None, None

    text = text.lower().strip()

    # Try explicit pattern: "A49_TB_A1_Horizontal : Plan Sheet"
    for family, types in TITLEBLOCKS["standard"].items():
        if family.lower() in text:
            for t in types:
                if t.lower() in text:
                    return family, t

    # Try simplified descriptions
    if "horizontal" in text:
        family = "A49_TB_A1_Horizontal"
    elif "vertical" in text:
        family = "A49_TB_A1_Vertical"
    else:
        family = None

    if "plan" in text:
        t = "Plan Sheet"
    elif "detail" in text:
        t = "Detail Sheet"
    else:
        t = None

    if family and t:
        return family, t

    return None, None


# ---------------------------------------------------------------------
# Helper: Validate that family/type exist in A49 titleblock list
# ---------------------------------------------------------------------
def validate_titleblock(family, t):
    if not family or not t:
        return False

    return (
        family in TITLEBLOCKS["standard"]
        and t in TITLEBLOCKS["standard"][family]
    )


# ---------------------------------------------------------------------
# Helper: Set session default titleblock
# ---------------------------------------------------------------------
def set_default_titleblock(family, t):
    DEFAULT_TITLEBLOCK["family"] = family
    DEFAULT_TITLEBLOCK["type"] = t


# ---------------------------------------------------------------------
# Helper: Reset default titleblock
# ---------------------------------------------------------------------
def clear_default_titleblock():
    DEFAULT_TITLEBLOCK["family"] = None
    DEFAULT_TITLEBLOCK["type"] = None


# ---------------------------------------------------------------------
# Helper: Return default titleblock (if exists)
# ---------------------------------------------------------------------
def get_default_titleblock():
    fam = DEFAULT_TITLEBLOCK["family"]
    t = DEFAULT_TITLEBLOCK["type"]

    if fam and t:
        return fam, t
    return None, None


# ---------------------------------------------------------------------
# High-level logic for determining titleblock for a sheet
# ---------------------------------------------------------------------
def determine_titleblock(sheet_number, override_family=None, override_type=None, mode="STANDARD"):
    """
    Returns (family, type)
    """

    # NONE mode -> no titleblock
    if mode.upper() == "NONE":
        return None, None

    # COVER SHEET HANDLING (slot 0000 in the new format; "A0.00" kept for
    # legacy projects still using the dotted convention)
    if sheet_number in ("0000", "A0.00"):
        return get_cover_titleblock()

    # 1. OVERRIDE (user explicitly selected)
    if override_family and override_type:
        if validate_titleblock(override_family, override_type):
            return override_family, override_type

    # 2. DEFAULT (session-based)
    fam, t = get_default_titleblock()
    if fam and t:
        return fam, t
        
    # 3. 💥 SMART AUTO-SELECTION (The Tie-Breaker)
    # If the user didn't specify, and we have no session default,
    # we apply the A49 standard rule instead of asking the user.
    
    # Logic:
    # - A9 Sheets -> 'Detail Sheet'
    # - All Others -> 'Plan Sheet' (default)
    # - Default Family -> 'A49_TB_A1_Horizontal' (Most common)
    
    prefix = sheet_number.split(".")[0].upper() if sheet_number else ""
    
    smart_family = "A49_TB_A1_Horizontal"
    
    if prefix == "A9":
        smart_type = "Detail Sheet"
    else:
        smart_type = "Plan Sheet"
        
    return smart_family, smart_type

# ---------------------------------------------------------------------
# Helper: Get Smart Options for User Selection
# ---------------------------------------------------------------------
def get_smart_titleblock_options(sheet_category):
    """
    Returns a filtered list of titleblocks based on the category.
    A0 -> Auto-assigned (handled elsewhere)
    A9 -> Detail Sheets (Horizontal/Vertical)
    A1-A8 -> Plan Sheets (Horizontal/Vertical)
    """
    cat = str(sheet_category).upper()
    
    # 1. DETAIL SHEETS (A9)
    if cat == "A9":
        return [
            "A49_TB_A1_Horizontal : Detail Sheet",
            "A49_TB_A1_Vertical : Detail Sheet"
        ]
        
    # 2. PLAN SHEETS (A1 - A8 and others)
    # This is the standard list for almost everything else.
    return [
        "A49_TB_A1_Horizontal : Plan Sheet",
        "A49_TB_A1_Vertical : Plan Sheet"
    ]
