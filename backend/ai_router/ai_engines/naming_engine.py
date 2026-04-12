import re
from .titleblock_engine import determine_titleblock

# =====================================================================
# NAMING ENGINE (MASTER SOURCE OF TRUTH)
# =====================================================================

# 1. MASTER ABBREVIATIONS LIST
VIEW_ABBREVIATIONS = {
    "a1": "FL", "a2": "EL", "a3": "SE", "a4": "WS", "a5": "CP", 
    "a6": "PT", "a7": "ST", "a8": "SC", "a9": "D1",
    "area plan": "AP", "area": "AP", "eia": "AP", "nfa": "AP", "gfa": "AP",
    "site plan": "SITE", "site": "SITE", "floorplan": "FL", "floor plan": "FL", "plan": "FL", "floor": "FL",
    "elevation": "EL", "elev": "EL", "section": "SE", "building section": "SE", "wall section": "WS", "wall_section": "WS",
    "ceilingplan": "CP", "ceiling plan": "CP", "reflected ceiling plan": "CP", "reflected": "CP", "ceiling": "CP", "rcp": "CP", "rfl": "CP",
    "enlarged plan": "PT", "enlarged": "PT", "toilet": "PT", "restroom": "PT", "pattern": "PT", "canopy": "PT",
    "stair": "ST", "stair plan": "ST", "ramp": "ST", "ramp plan": "ST", "lift": "ST", "lift plan": "ST", "elevator": "ST",
    "door schedule": "AD", "window schedule": "AW", "schedule": "SC",
    "detail": "D1", "drafting": "D1",
    "cover": "CV", "list": "LI"
}

def get_view_abbrev(view_type):
    if not view_type: return "VW"
    vt = view_type.lower().strip()
    sorted_keys = sorted(VIEW_ABBREVIATIONS.keys(), key=len, reverse=True)
    for key in sorted_keys:
        if key in vt: return VIEW_ABBREVIATIONS[key]
    return "VW"

VIEW_ABBREV = VIEW_ABBREVIATIONS

def get_view_sheet_type(view_type_abbrev):
    MAPPING = {
        "FL": "A1", "SITE": "A1", "RF": "A1", "CP": "A5", "EL": "A2", "SE": "A3", 
        "WS": "A4", "PT": "A6", "ST": "A7", "AD": "A8", "AW": "A8", "SC": "A8", "D1": "A9",
        "AP": "AP"
    }
    return MAPPING.get(view_type_abbrev, "A1")

def level_to_code(level):
    if not level: return ""
    clean = str(level).upper().strip()
    if "SITE" in clean: return "00" 
    if "ROOF" in clean: return "RF"
    match = re.search(r"\b([BPLM])\s*(\d+)", clean)
    if match:
        prefix = match.group(1) 
        num = int(match.group(2))
        return f"{prefix}{num}" if prefix != "L" else f"{num:02d}"
    nums = re.findall(r"\d+", clean)
    if nums: return nums[0].zfill(2)
    return "00"

def normalize_stage(stage):
    if not stage: return ""
    return stage.upper().strip()

# 💥 UPDATED: Added 'template_name' argument
def generate_standard_view_name(stage, view_type, level_name, existing_names, template_name=None):
    stage = normalize_stage(stage)
    abbrev = get_view_abbrev(view_type)
    
    # 💥 NEW: AREA PLAN LOGIC (Checks Template for EIA/NFA)
    if abbrev == "AP":
        lvl_code = level_to_code(level_name)
        vt_check = view_type.upper()
        tpl_check = (template_name or "").upper() # Look at the template!
        
        # Determine Sub-Type
        subtype = "AREA PLAN" # Default
        
        # Check View Type OR Template Name
        if "EIA" in vt_check or "EIA" in tpl_check: 
            subtype = "(EIA) AREA PLAN"
        elif "NFA" in vt_check or "NFA" in tpl_check: 
            subtype = "(NFA) AREA PLAN"
        elif "GFA" in vt_check or "GFA" in tpl_check: 
            subtype = "(GFA) AREA PLAN"
        
        base_name = f"{stage}_AP_{lvl_code} - {subtype}"
        
        if base_name not in existing_names: return base_name
        
        counter = 1
        while True:
            candidate = f"{base_name} Copy {counter}"
            if candidate not in existing_names: return candidate
            counter += 1

    # --- EXISTING LOGIC BELOW (Unchanged) ---
    if level_name and "SITE" in level_name.upper(): abbrev = "SITE"
    sheet_type = get_view_sheet_type(abbrev)

    if sheet_type in ["A8", "A9"] or abbrev in ["SC", "AD", "AW", "D1"]:
        base_prefix = f"{stage}_{sheet_type}_{abbrev}"
        counter = 1
        while True:
            candidate = f"{base_prefix}_{str(counter).zfill(2)}"
            if candidate not in existing_names: return candidate
            counter += 1
            if counter > 99: break 
        return f"{base_prefix}_New"

    lvl_code = level_to_code(level_name)
    if stage in ("PD", "DD", "CD"):
        if abbrev == "SITE": base = f"{stage}_{sheet_type}_{abbrev}"
        else: base = f"{stage}_{sheet_type}_{abbrev}_{lvl_code}"
    else:
        if abbrev == "SITE": base = f"{stage}_{abbrev}"
        else: base = f"{stage}_{abbrev}_{lvl_code}"

    name = base
    counter = 1
    while name in existing_names:
        name = f"{base} Copy {counter}"
        counter += 1
    return name

def generate_custom_view_name(user_name, existing_names):
    if not user_name: user_name = "Unnamed View"
    name = user_name
    counter = 1
    while name in existing_names:
        name = f"{user_name} Copy {counter}"
        counter += 1
    return name

# =====================================================================
# 2. SHEET HELPERS
# =====================================================================

SHEET_SET_MAP = {
    "A0": "A0_GENERAL INFORMATION", "A1": "A1_FLOOR PLANS", "A2": "A2_BUILDING ELEVATIONS", 
    "A3": "A3_BUILDING SECTIONS", "A4": "A4_WALL SECTIONS", "A5": "A5_CEILING PLANS",
    "A6": "A6_ENLARGED PLANS AND INTERIOR ELEVATIONS", "A7": "A7_VERTICAL CIRCULATION", 
    "A8": "A8_DOOR AND WINDOW SCHEDULE", "A9": "A9_DETAILS"
}
PROJECT_PHASE_MAP = {"WV": "00 - WORKING VIEW", "PD": "01 - PRE-DESIGN", "DD": "02 - DESIGN DEVELOPMENT", "CD": "03 - CONSTRUCTION DOCUMENTS", "NONE": None}
DEFAULT_SHEET_NAMES = {
    "A0": ["COVER", "DRAWING INDEX", "SITE AND VICINITY PLAN", "STANDARD SYMBOLS", "SAFETY PLAN", "WALL TYPES"],
    "A1": ["1ST FLOOR PLAN", "2ND FLOOR PLAN", "3RD FLOOR PLAN", "4TH FLOOR PLAN"],
    "A2": ["ELEVATIONS"], "A3": ["BUILDING SECTIONS"], "A4": ["WALL SECTIONS"],
    "A5": ["1ST FLOOR CEILING PLAN", "2ND FLOOR CEILING PLAN", "3RD FLOOR CEILING PLAN"],
    "A6": ["ENLARGED TOILET PLAN", "FLOOR PATTERN PLAN", "CANOPY PLAN"],
    "A7": ["ENLARGED STAIR PLAN", "ENLARGED STAIR SECTION", "ENLARGED RAMP PLAN", "ENLARGED LIFT PLAN"],
    "A8": ["DOOR SCHEDULE", "WINDOW SCHEDULE"], "A9": ["DETAILS"], "X0": ["CUSTOM SHEET"]
}
COVER_TITLEBLOCK = {"family": "A49_TB_A1_Horizontal_Cover", "type": "Cover"}

def normalize_sheet_type(name_or_title, view_type_raw=None):
    text = (str(name_or_title or "") + " " + str(view_type_raw or "")).lower()
    if any(x in text for x in ["cover", "index", "list", "survey", "symbol", "legend", "general"]): return "A0"
    if "site" in text: return "A1"
    if "floor" in text or "plan" in text:
        if not any(x in text for x in ["ceiling", "enlarged", "toilet", "restroom", "pattern", "canopy", "stair", "ramp", "lift", "elevator"]): return "A1"
    if "ceiling" in text: return "A5"
    if any(x in text for x in ["enlarged", "toilet", "restroom", "pattern", "flooring", "canopy", "roof detail"]): return "A6"
    if any(x in text for x in ["stair", "ramp", "lift", "elevator"]): return "A7"
    if "elevation" in text: return "A2"
    if "building section" in text: return "A3"
    if "wall section" in text: return "A4"
    if "detail" in text: return "A9"
    return "A1"

def _extract_index(num):
    match = re.findall(r"\.(\d+)", num)
    return int(match[0]) if match else 0

def get_next_sheet_number(sheet_type, existing_numbers, requested_index=None):
    prefix = sheet_type + "."
    taken = set(existing_numbers)
    if requested_index is not None:
        target_num = f"{prefix}{str(requested_index).zfill(2)}"
        if target_num not in taken: return target_num
        for char in "abcdefghijklmnopqrstuvwxyz":
            if f"{target_num}{char}" not in taken: return f"{target_num}{char}"
        return f"{target_num}_Copy"
    
    start = 0 if sheet_type in ["A0", "X0"] else 1
    max_idx = start - 1
    for num in taken:
        if num.startswith(prefix):
            try:
                suffix = num.split(".")[-1]
                if suffix.isdigit():
                    val = int(suffix)
                    if val > max_idx: max_idx = val
            except: pass
    return f"{prefix}{str(max_idx + 1).zfill(2)}"

def get_ordinal_str(n):
    if 11 <= (n % 100) <= 13: suffix = 'TH'
    else: suffix = {1: 'ST', 2: 'ND', 3: 'RD'}.get(n % 10, 'TH')
    return f"{n}{suffix}"

def get_default_sheet_name(sheet_type, index):
    seq_idx = index if sheet_type == "A0" else index - 1
    names = DEFAULT_SHEET_NAMES.get(sheet_type, [])
    if 0 <= seq_idx < len(names): return names[seq_idx]
    return "CUSTOM SHEET"

def generate_smart_name(sheet_type, level_code=None, sequence_index=0):
    st = sheet_type.upper()
    
    # 1. FLOOR PLANS (A1)
    if st == "A1":
        # CASE A: Explicit Level Code (e.g. from View)
        if level_code: 
            uc = str(level_code).upper()
            if uc == "00": return "SITE PLAN"
            if "B" in uc: return f"{uc} BASEMENT PLAN"
            if "P" in uc: return f"{uc} PARKING PLAN"
            digits = re.findall(r'\d+', uc)
            if digits:
                num = int(digits[0])
                ord_str = get_ordinal_str(num)
                return f"{ord_str} FLOOR PLAN"
            return "FLOOR PLAN"
            
        # 💥 FIX: Offset Correction
        # sequence_index is 0-based (e.g. A1.01 = 0, A1.08 = 7).
        # We must ADD 1 to get the human-readable ordinal (0 -> 1st, 7 -> 8th).
        if sequence_index >= 0:
            ord_str = get_ordinal_str(sequence_index + 1)
            return f"{ord_str} FLOOR PLAN"
            
        return "FLOOR PLAN"

    # 2. CEILING PLANS (A5)
    if st == "A5":
        base = generate_smart_name("A1", level_code, sequence_index)
        
        clean_base = re.sub(r"\s+(FLOOR\s+)?PLAN$", "", base, flags=re.IGNORECASE)
        
        if "SITE" in clean_base.upper(): return "SITE CEILING PLAN" 
        
        if sequence_index >= 0 and "FLOOR" not in clean_base.upper() and "BASEMENT" not in clean_base.upper():
             return f"{clean_base} FLOOR CEILING PLAN"
             
        return f"{clean_base} CEILING PLAN"

    # 3. OTHER SHEETS (Default List)
    names_list = DEFAULT_SHEET_NAMES.get(st, [])
    if not names_list: return "CUSTOM SHEET"
    if len(names_list) == 1: return names_list[0] 
    
    # 💥 FIX: Use sequence_index directly
    # build_sheets_payload already converts A1.01 -> 0.
    # Previously, we subtracted 1 again, which was wrong.
    if 0 <= sequence_index < len(names_list): return names_list[sequence_index] 
    
    return "CUSTOM SHEET"

def make_standard_sheet(sheet_type, sheet_number, sheet_name, stage):
    return {"sheet_number": sheet_number, "sheet_name": sheet_name, "sheet_type": sheet_type, "project_phase": PROJECT_PHASE_MAP.get(stage, None), "sheet_set": SHEET_SET_MAP.get(sheet_type, None), "discipline": "ARCHITECTURE"}

def make_custom_sheet(sheet_type, sheet_number, sheet_name):
    return {"sheet_number": sheet_number, "sheet_name": sheet_name, "sheet_type": sheet_type, "project_phase": None, "sheet_set": None, "discipline": None}

def build_sheets_payload(request_data, existing_sheet_numbers):
    stage = request_data.get("stage", "CD") 
    sheet_type_raw = request_data.get("sheet_category")
    mode = request_data.get("mode", "STANDARD")
    user_name = request_data.get("sheet_name")
    levels = request_data.get("levels") or [] 
    count = request_data.get("batch_count", None)
    forced_fam = request_data.get("titleblock_family")
    forced_type = request_data.get("titleblock_type")
    raw_tb = request_data.get("titleblock_raw")

    if sheet_type_raw: sheet_type = sheet_type_raw.upper().split(",")[0].strip()
    else: sheet_type = "A1"

    if user_name:
        clean_name = user_name.lower().strip().replace("sheets", "").replace("sheet", "").strip()
        generic_terms = ["plan", "plans", "view", "views", "floor plan", "site plan", "elevation", "section", "detail", "schedule"]
        if clean_name in generic_terms: user_name = None 

    sheets = []
    def create_payload(num, name):
        if forced_fam and forced_type: fam, t_type = forced_fam, forced_type
        else: fam, t_type = determine_titleblock(num, override_family=raw_tb, mode="STANDARD")
        if num.startswith("A0.00"): fam, t_type = "A49_TB_A1_Horizontal_Cover", "Cover"
        if mode == "CUSTOM" or sheet_type.startswith("X"): p = make_custom_sheet(sheet_type, num, name)
        else: p = make_standard_sheet(sheet_type, num, name, stage)
        p["titleblock_family"] = fam
        p["titleblock_type"] = t_type
        return p

    force_cover = False
    if sheet_type == "A0":
        view_type = request_data.get("view_type")
        if (user_name and "cover" in user_name.lower()) or (view_type and "cover" in view_type.lower()): force_cover = True

    if levels and sheet_type in ["A1", "A5"]:
        for lvl in levels:
            lvl_code = level_to_code(lvl)
            if sheet_type == "A5" and lvl_code == "00": continue 
            prefix = sheet_type + "."
            target_num = f"{prefix}{lvl_code}"
            final_num = target_num
            suffix_char = 'a'
            while final_num in existing_sheet_numbers:
                final_num = f"{target_num}{suffix_char}"
                suffix_char = chr(ord(suffix_char) + 1)
            existing_sheet_numbers.append(final_num)
            final_name = user_name if user_name else generate_smart_name(sheet_type, lvl_code)
            sheets.append(create_payload(final_num, final_name.upper()))
    else:
        try: total_count = int(count) if count else 1
        except: total_count = 1
        total = len(levels) if levels else total_count
        for i in range(total):
            if force_cover and i == 0:
                base_num = "A0.00"
                if base_num in existing_sheet_numbers: num = get_next_sheet_number(sheet_type, existing_sheet_numbers, 0)
                else: num = base_num
            else: num = get_next_sheet_number(sheet_type, existing_sheet_numbers)
            existing_sheet_numbers.append(num)
            try:
                parts = re.split(r"[^\d]", num)
                digits = [p for p in parts if p.isdigit()]
                if digits:
                    val = int(digits[-1])
                    seq_idx = val if sheet_type == "A0" else val - 1
                else: seq_idx = i
            except: seq_idx = i
            final_name = user_name if user_name else generate_smart_name(sheet_type, None, seq_idx)
            sheets.append(create_payload(num, final_name.upper()))
    return sheets

# =====================================================================
# MAIN ENTRY POINT (RESTORED)
# =====================================================================

def apply(request_data, existing_view_names, existing_sheet_numbers, force_suffix=False, forced_suffix_number=None, forced_suffix_base=None):
    command = request_data.get("command")
    mode = request_data.get("mode", "STANDARD")
    existing_view_names = existing_view_names or []
    existing_sheet_numbers = existing_sheet_numbers or []

    # 💥💥💥 THIS IS CRITICAL FOR VIEW CREATION 💥💥💥
    if command == "create_view":
        views_out = []
        stage = request_data.get("stage")
        view_type = request_data.get("view_type")
        levels = request_data.get("levels") or []
        final_template = request_data.get("template")

        if not final_template and stage and view_type:
            from .template_engine import get_templates
            tpl_info = get_templates(stage, view_type)
            final_template = tpl_info.get("default_template")

        for lvl in levels:
            if mode == "NONE": name = generate_custom_view_name(request_data.get("sheet_name"), existing_view_names)
            else: name = generate_standard_view_name(stage, view_type, lvl, existing_view_names, template_name=final_template)
            existing_view_names.append(name)
            views_out.append({"name": name, "view_type": view_type, "level": lvl, "template": final_template})

        return {"views": views_out, "sheets": [], "category": None, "stage": stage, "final_template": final_template}

    if command in ("create_sheet", "create_sheets"):
        sheets_out = build_sheets_payload(request_data, existing_sheet_numbers)
        return {
            "views": [], "sheets": sheets_out,
            "sheet_type": request_data.get("sheet_category"),
            "stage": request_data.get("stage"), "final_template": None
        }

    return {"views": [], "sheets": [], "category": None, "stage": None, "final_template": None}

def get_default_template_for_stage(stage, view_type_raw):
    return None