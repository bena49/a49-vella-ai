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
    """
    Reduce a level name (any naming convention — English, English with
    elevation prefix, Thai with elevation prefix) to the short code used
    in view names: e.g. "01", "02", "07T", "B1M", "RF", "SITE", "TOP".

    Routes through level_matcher.extract_level_signature for consistent
    handling across all A49 naming variants. Falls back to legacy regex
    if the matcher errors (defensive — should never happen in practice).
    """
    if not level: return ""

    # Primary path: signature extraction (handles all naming conventions)
    try:
        from .level_matcher import extract_level_signature
        sig = extract_level_signature(level)
        if sig["special"]:
            # SITE / RF / TOP / MZ / PD / AT — used as-is in the view name
            return sig["special"]
        if sig["digit"] is not None:
            prefix = sig["prefix"] or "L"
            num    = sig["digit"]
            suffix = sig["suffix"] or ""
            if prefix == "L":
                # Above-ground levels are zero-padded to 2 digits: 01, 02, 07T
                return f"{num:02d}{suffix}"
            # Basement / parking / others keep the prefix: B1, B1M, P1
            return f"{prefix}{num}{suffix}"
    except Exception:
        pass

    # Legacy fallback (kept defensive in case the matcher import fails)
    clean = str(level).upper().strip()
    if "SITE" in clean: return "SITE"   # was "00" — now matches new convention
    if "ROOF" in clean: return "RF"
    if "TOP" in clean or "PARAPET" in clean: return "TOP"
    match = re.search(r"\b([BPLM])\s*(\d+)([A-Z]?)", clean)
    if match:
        prefix = match.group(1)
        num = int(match.group(2))
        suffix = match.group(3) or ""
        return f"{prefix}{num}{suffix}" if prefix != "L" else f"{num:02d}{suffix}"
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

    # --- EXISTING LOGIC BELOW ---
    # NOTE: removed legacy "if SITE in level_name → abbrev=SITE" override.
    # Floor Plans at the SITE level now produce CD_A1_FL_SITE (preserving
    # the FL view-type), instead of collapsing to CD_A1_SITE. The lvl_code
    # itself becomes "SITE" via level_to_code().
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
#
# Sheet numbering format (effective post-2026-05-04 launch):
#
#   A0 — General/Cover         sequence  0000, 0010, 0020 …  (+10)
#   A1 — Floor Plans           level     1000=SITE, 1010=L1 … 1990=L99,
#                                        B1=1009 … B9=1001, ROOF=max+10
#   A2-A4, A6-A8 — sequence    series×1000 + 10, 20, 30 …
#   A5 — Ceiling Plans         level     same shape as A1 (5xxx). NO SITE.
#   X0 — Custom                sequence  X000, X010, X020 … (X + 3 digits)
#
# Special-suffix levels (M, T, …) use base+1, first-come-first-served
# (collision → keep +1 until a free slot is found).
#
# Per-format design + edge cases documented in:
#   memory/next_release_sheet_numbering_refactor.md
#   ai_router/ai_engines/test_sheet_numbering.py
# =====================================================================

SHEET_SET_MAP = {
    "A0": "A0_GENERAL INFORMATION", "A1": "A1_FLOOR PLANS", "A2": "A2_BUILDING ELEVATIONS",
    "A3": "A3_BUILDING SECTIONS", "A4": "A4_WALL SECTIONS", "A5": "A5_CEILING PLANS",
    "A6": "A6_ENLARGED PLANS AND INTERIOR ELEVATIONS", "A7": "A7_VERTICAL CIRCULATION",
    "A8": "A8_DOOR AND WINDOW SCHEDULE", "A9": "A9_DETAILS"
}
PROJECT_PHASE_MAP = {"WV": "00 - WORKING VIEW", "PD": "01 - PRE-DESIGN", "DD": "02 - DESIGN DEVELOPMENT", "CD": "03 - CONSTRUCTION DOCUMENTS", "NONE": None}

# Slot-specific names: only A0/A6/A7/A8 have distinct names per slot.
# Each list is indexed by slot/10 (slot 0000 → idx 0; slot 0010 → idx 1).
# A `None` placeholder means "no name at this slot" → falls back to category
# default below, then to "CUSTOM SHEET".
DEFAULT_SHEET_NAMES = {
    "A0": ["COVER", "DRAWING INDEX", "SITE AND VICINITY PLAN",
           "STANDARD SYMBOLS", "SAFETY PLAN", "WALL TYPES"],
    "A6": [None, "ENLARGED TOILET PLAN", "FLOOR PATTERN PLAN", "CANOPY PLAN"],
    "A7": [None, "ENLARGED STAIR PLAN", "ENLARGED STAIR SECTION",
           "ENLARGED RAMP PLAN", "ENLARGED LIFT PLAN"],
    "A8": [None, "DOOR SCHEDULE", "WINDOW SCHEDULE"],
}

# Single repeating name for categories where every slot is the same kind of
# sheet (A2 = ELEVATIONS, A3 = BUILDING SECTIONS, etc.). Used as the fallback
# when DEFAULT_SHEET_NAMES doesn't have an entry for the requested slot.
CATEGORY_DEFAULT_NAME = {
    "A2": "ELEVATIONS",
    "A3": "BUILDING SECTIONS",
    "A4": "WALL SECTIONS",
    "A9": "DETAILS",
    "X0": "CUSTOM SHEET",
}
COVER_TITLEBLOCK = {"family": "A49_TB_A1_Horizontal_Cover", "type": "Cover"}

# Categories whose slot is determined by a level (not by sequence position).
_LEVEL_BASED_CATEGORIES = {"A1", "A5"}

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

# ── Slot/number primitives ───────────────────────────────────────────────

def _series_base(sheet_type):
    """Numeric base for a sheet category. A0=0, A1=1000 … A9=9000.
    X0 returns None (handled separately as letter-prefix format)."""
    st = (sheet_type or "").upper()
    if st == "X0": return None
    m = re.match(r"^A(\d)$", st)
    if m: return int(m.group(1)) * 1000
    return None

def _format_slot(sheet_type, slot):
    """Render an integer slot as the displayed sheet-number string.
    A0/A1/…/A9 → 4-digit zero-padded ('0010', '1090').
    X0 → 'X' + 3-digit ('X010')."""
    st = (sheet_type or "").upper()
    if st == "X0": return f"X{int(slot):03d}"
    return f"{int(slot):04d}"

def _parse_slot(sheet_number):
    """Extract the numeric slot from a sheet-number string.
    Returns (slot_int, category_inferred) or (None, None) if not parseable.
    For X0, returns the integer after the 'X'."""
    if not sheet_number: return None, None
    s = str(sheet_number).strip().upper()
    if s.startswith("X"):
        try: return int(s[1:]), "X0"
        except (ValueError, TypeError): return None, None
    try:
        n = int(s)
    except (ValueError, TypeError):
        return None, None
    # Infer category from thousands digit
    if n < 1000: cat = "A0"
    else: cat = f"A{n // 1000}"
    return n, cat

def sort_key_sheet_number(sheet_number):
    """Stable sort key for sheet numbers. Sorts numeric (0000-9999) ascending,
    then X-series (X000-X999) ascending, then any unrecognised string last
    (defensive — should not occur under the post-2026-05 numbering spec)."""
    s = str(sheet_number or "").upper().strip()
    if s.startswith("X"):
        try:
            return (1, int(s[1:]), s)
        except ValueError:
            return (2, 0, s)
    try:
        return (0, int(s), s)
    except ValueError:
        return (2, 0, s)

def _existing_in_category(sheet_type, existing_numbers):
    """Subset of existing_numbers belonging to this category (as integer slots)."""
    st = (sheet_type or "").upper()
    out = []
    for num in (existing_numbers or []):
        slot, cat = _parse_slot(num)
        if slot is None: continue
        if st == "X0":
            if cat == "X0": out.append(slot)
        elif cat == st:
            out.append(slot)
    return out

# ── Level → slot resolution (A1 / A5) ────────────────────────────────────

def _max_above_grade_level(project_levels):
    """Highest above-grade level number in the project (the L<N> with the
    largest N, ignoring suffix variants like L7T). Returns 0 if none found."""
    from .level_matcher import extract_level_signature
    max_n = 0
    for lvl in (project_levels or []):
        sig = extract_level_signature(lvl)
        if sig["prefix"] == "L" and sig["digit"] is not None and not sig["suffix"]:
            if sig["digit"] > max_n:
                max_n = sig["digit"]
    return max_n

def compute_sheet_slot(sheet_type, level_name=None, project_levels=None,
                       request_levels=None):
    """Compute the *initial* slot integer for a sheet, before collision check.

    For ROOF/TOP: prefers the highest above-grade level in `request_levels`
    (the levels the user is creating sheets for in this batch). Falls back
    to `project_levels` only when the request has no above-grade levels —
    e.g. "Create sheet for ROOF only". This insulates ROOF placement from
    stale/dirty session caches and from hidden Revit reference levels that
    don't represent real building floors.

    Returns:
        int slot, or None if the combination is invalid (e.g. A5+SITE,
        unsupported level naming, level out of supported range).
    """
    st = (sheet_type or "").upper()
    base = _series_base(st)
    if base is None or st not in _LEVEL_BASED_CATEGORIES:
        return None  # Sequence-based — caller uses _next_sequence_slot

    if not level_name:
        return None

    from .level_matcher import extract_level_signature
    sig = extract_level_signature(level_name)

    # SITE → base anchor (only for A1; A5 has no Site ceiling plan)
    if sig["special"] == "SITE":
        if st == "A5":
            return None
        return base

    # ROOF / TOP → max above-grade L + 1, in slot terms (×10)
    if sig["special"] in ("RF", "TOP"):
        max_n = _max_above_grade_level(request_levels) if request_levels else 0
        if max_n == 0:
            max_n = _max_above_grade_level(project_levels)
        if max_n == 0:
            max_n = 1  # Defensive: project/request with no above-grade levels
        return base + (max_n + 1) * 10

    # Above grade L<N>
    if sig["prefix"] == "L" and sig["digit"] is not None:
        n = sig["digit"]
        if n < 1 or n > 99:
            return None  # Out of range; spec caps at L99 for now
        slot = base + n * 10
        if sig["suffix"]:
            slot += 1  # M / T / … suffix → +1, FCFS handled by collision loop
        return slot

    # Below grade B<N> — descends into the 1000–1009 range.
    # Suffix variants (B<N>M, B<N>T, …) take the parent's natural slot
    # (closer to grade); the bare basement shifts DOWN by 1 to make room.
    # Cascades when multiple variants exist (each suffix variant at or above
    # B<N> pushes B<N> down by 1). Without request context, falls back to
    # the natural slot (B<N>=base+10−N) — same as before this enhancement.
    if sig["prefix"] == "B" and sig["digit"] is not None:
        n = sig["digit"]
        if n < 1 or n > 9:
            return None  # Cap at B9 (spec: rarely > B5 in practice)

        natural_slot = base + (10 - n)

        # Build the basement signature set from the request context. Each
        # entry is (digit, suffix). Distinct M/T variants count separately;
        # accidental duplicates collapse via the set.
        request_basements = set()
        if request_levels:
            from .level_matcher import extract_level_signature as _extract
            for lvl in request_levels:
                ls = _extract(lvl)
                if (ls["prefix"] == "B" and ls["digit"] is not None
                        and 1 <= ls["digit"] <= 9):
                    request_basements.add((ls["digit"], ls["suffix"] or ""))

        if sig["suffix"]:
            # Suffix variant: takes its parent's natural slot, shifted only
            # by suffix variants ABOVE this level (smaller digit).
            shift = sum(1 for d, s in request_basements if d < n and s)
        else:
            # Bare basement: shifted by suffix variants AT OR ABOVE this level.
            shift = sum(1 for d, s in request_basements if d <= n and s)

        slot = natural_slot - shift
        if slot < base + 1:
            return None  # Overflowed past the basement range
        return slot

    return None

# ── Sequence-based slot selection (A0, A2-A4, A6-A8, X0) ─────────────────

def _next_sequence_slot(sheet_type, existing_numbers):
    """Pick the next +10 slot for a sequence-based category.
    A0 / X0 begin at slot 0 (cover slot). All others begin at slot 10."""
    st = (sheet_type or "").upper()
    cat_slots = _existing_in_category(st, existing_numbers)
    base = _series_base(st) or 0  # X0 has no numeric base

    if not cat_slots:
        if st in ("A0", "X0"): return base + 0
        return base + 10

    # Always advance from the highest existing slot, never gap-fill.
    # Insert-between is a deliberate user action handled by a future wizard.
    max_slot = max(cat_slots)
    next_slot = ((max_slot // 10) + 1) * 10
    return next_slot

# ── Public: get next sheet number ────────────────────────────────────────

def get_next_sheet_number(sheet_type, existing_numbers, level_name=None,
                          project_levels=None, request_levels=None):
    """Compute the next sheet number for a given category.

    Args:
        sheet_type: 'A0' through 'A9', or 'X0'.
        existing_numbers: list of strings — sheets already in the project.
        level_name: required for level-based categories (A1, A5). Ignored otherwise.
        project_levels: full project level inventory (cached on Vella mount).
            Used as a fallback when request_levels has no above-grade levels.
        request_levels: levels the user is creating sheets for in this batch.
            Preferred source for ROOF/TOP max computation (insulates against
            stale cache).

    Returns:
        Sheet-number string (e.g. '1010', 'X020'), or None when the
        combination is invalid (e.g. A5 + SITE).
    """
    st = (sheet_type or "").upper()
    existing_set = set(str(n).upper() for n in (existing_numbers or []))

    # Level-based path (A1, A5) — slot dictated by level
    if st in _LEVEL_BASED_CATEGORIES and level_name:
        slot = compute_sheet_slot(st, level_name=level_name,
                                  project_levels=project_levels,
                                  request_levels=request_levels)
        if slot is None:
            return None

        # Collision direction depends on whether the level is above or below
        # grade. Above-grade and special levels push UP (next +1 slot — uses
        # the M/T suffix range). Basements push DOWN — pushing UP would land
        # the duplicate in L1's territory (e.g. duplicate B1 → 1011 instead
        # of the architecturally-correct 1007 below B1).
        base = _series_base(st)
        from .level_matcher import extract_level_signature as _extract
        sig = _extract(level_name)
        is_basement = sig["prefix"] == "B" and sig["digit"] is not None

        step = -1 if is_basement else 1
        while _format_slot(st, slot) in existing_set:
            slot += step
            if is_basement and slot < base + 1:
                return None  # Underflowed below the basement range
            if not is_basement and slot >= base + 1000:
                return None  # Overflowed into the next category
        return _format_slot(st, slot)

    # Sequence-based path — also covers A1/A5 when no level supplied (rare)
    slot = _next_sequence_slot(st, existing_numbers or [])
    while _format_slot(st, slot) in existing_set:
        slot += 10
        if st != "X0":
            base = _series_base(st)
            if slot >= base + 1000:
                return None
    return _format_slot(st, slot)

# ── Smart name generation ────────────────────────────────────────────────

def _level_label(level_name):
    """Human label for a level. Returns a tuple (label, kind) where kind is
    'site' | 'roof' | 'normal' so the caller can apply category-specific
    naming rules (e.g. A1+ROOF drops the 'FLOOR' word)."""
    if not level_name:
        return ("", "normal")
    from .level_matcher import extract_level_signature
    sig = extract_level_signature(level_name)
    if sig["special"] == "SITE": return ("SITE", "site")
    if sig["special"] in ("RF", "TOP"): return ("LEVEL ROOF", "roof")
    if sig["special"] == "MZ": return ("MEZZANINE", "normal")
    if sig["special"]: return (sig["special"], "normal")
    if sig["digit"] is not None:
        prefix = sig["prefix"] or "L"
        suffix = sig["suffix"] or ""
        if prefix == "L":
            return (f"LEVEL {sig['digit']}{suffix}", "normal")
        return (f"LEVEL {prefix}{sig['digit']}{suffix}", "normal")
    return (str(level_name).upper(), "normal")

def generate_smart_name(sheet_type, sheet_number=None, level_name=None):
    """Default sheet name for a category + slot + (optional) level.

    A1/A5 use the level: 'LEVEL 1 FLOOR PLAN', 'LEVEL B1 CEILING PLAN', etc.
    Roof drops 'FLOOR' on A1 → 'LEVEL ROOF PLAN'. Site is rejected on A5.
    Other categories look up the slot in DEFAULT_SHEET_NAMES, then fall back
    to CATEGORY_DEFAULT_NAME (for A2/A3/A4/A9/X0), then to 'CUSTOM SHEET'.
    """
    st = (sheet_type or "").upper()

    # Level-based naming (A1 / A5)
    if st == "A1" and level_name:
        label, kind = _level_label(level_name)
        if kind == "site": return "SITE PLAN"
        if kind == "roof": return f"{label} PLAN"  # 'LEVEL ROOF PLAN' (no FLOOR)
        return f"{label} FLOOR PLAN"

    if st == "A5" and level_name:
        label, kind = _level_label(level_name)
        if kind == "site": return "SITE CEILING PLAN"  # rejected upstream
        return f"{label} CEILING PLAN"

    # Sequence-based naming — index into DEFAULT_SHEET_NAMES by slot/10
    slot, _ = _parse_slot(sheet_number) if sheet_number else (None, None)
    names = DEFAULT_SHEET_NAMES.get(st, [])
    fallback = CATEGORY_DEFAULT_NAME.get(st, "CUSTOM SHEET")

    if slot is None:
        return fallback

    # Slot offset within category. A0 starts at 0, others at 10. X0 has no base.
    base = _series_base(st)
    offset = slot if st == "X0" else slot - (base or 0)
    idx = offset // 10
    if 0 <= idx < len(names) and names[idx]:
        return names[idx]
    return fallback

def make_standard_sheet(sheet_type, sheet_number, sheet_name, stage):
    return {"sheet_number": sheet_number, "sheet_name": sheet_name, "sheet_type": sheet_type, "project_phase": PROJECT_PHASE_MAP.get(stage, None), "sheet_set": SHEET_SET_MAP.get(sheet_type, None), "discipline": "ARCHITECTURE"}

def make_custom_sheet(sheet_type, sheet_number, sheet_name):
    return {"sheet_number": sheet_number, "sheet_name": sheet_name, "sheet_type": sheet_type, "project_phase": None, "sheet_set": None, "discipline": None}

def build_sheets_payload(request_data, existing_sheet_numbers):
    """Generate the list of sheet payloads for a create_sheet command.

    Reads request_data['project_levels'] (cached from Revit) to resolve
    ROOF/TOP slots correctly. Falls back to request_data['levels'] if the
    project inventory wasn't passed in (degraded — ROOF will land at
    max(levels)+10 instead of project max+10)."""
    stage = request_data.get("stage", "CD")
    sheet_type_raw = request_data.get("sheet_category")
    mode = request_data.get("mode", "STANDARD")
    user_name = request_data.get("sheet_name")
    levels = request_data.get("levels") or []
    project_levels = request_data.get("project_levels") or levels
    count = request_data.get("batch_count", None)
    forced_fam = request_data.get("titleblock_family")
    forced_type = request_data.get("titleblock_type")
    raw_tb = request_data.get("titleblock_raw")

    if sheet_type_raw:
        sheet_type = sheet_type_raw.upper().split(",")[0].strip()
    else:
        # Sheet category was not specified — infer from view_type so e.g. a
        # "Create Ceiling Plan + place on new sheet" flow lands on A5 (not the
        # legacy A1 default which silently produced floor-plan sheet numbers).
        view_type = request_data.get("view_type")
        sheet_type = "A1"
        if view_type:
            abbrev = get_view_abbrev(view_type)
            inferred = get_view_sheet_type(abbrev)
            # 'AP' (Area Plan) isn't a real sheet category — area plans go on
            # A1 sheets per A49 standard. All other mappings are real (A1-A9).
            if inferred and inferred != "AP":
                sheet_type = inferred

    if user_name:
        clean_name = user_name.lower().strip().replace("sheets", "").replace("sheet", "").strip()
        generic_terms = ["plan", "plans", "view", "views", "floor plan", "site plan",
                         "elevation", "section", "detail", "schedule"]
        if clean_name in generic_terms:
            user_name = None

    sheets = []

    def create_payload(num, name):
        if forced_fam and forced_type:
            fam, t_type = forced_fam, forced_type
        else:
            fam, t_type = determine_titleblock(num, override_family=raw_tb, mode="STANDARD")
        # Cover slot always uses the cover titleblock
        if num == "0000":
            fam, t_type = "A49_TB_A1_Horizontal_Cover", "Cover"
        if mode == "CUSTOM" or sheet_type.startswith("X"):
            p = make_custom_sheet(sheet_type, num, name)
        else:
            p = make_standard_sheet(sheet_type, num, name, stage)
        p["titleblock_family"] = fam
        p["titleblock_type"] = t_type
        return p

    # A0 + cover keyword in user input → force the 0000 cover slot
    force_cover = False
    if sheet_type == "A0":
        view_type = request_data.get("view_type")
        if (user_name and "cover" in user_name.lower()) or \
           (view_type and "cover" in view_type.lower()):
            force_cover = True

    if levels and sheet_type in _LEVEL_BASED_CATEGORIES:
        for lvl in levels:
            num = get_next_sheet_number(sheet_type, existing_sheet_numbers,
                                        level_name=lvl,
                                        project_levels=project_levels,
                                        request_levels=levels)
            if not num:
                # Invalid combo (e.g. A5+SITE) — skip silently; caller decides
                # whether to surface "no sheets created" message.
                continue
            existing_sheet_numbers.append(num)
            final_name = user_name if user_name else generate_smart_name(
                sheet_type, sheet_number=num, level_name=lvl)
            sheets.append(create_payload(num, final_name.upper()))
    else:
        try:
            total_count = int(count) if count else 1
        except (TypeError, ValueError):
            total_count = 1
        total = len(levels) if levels else total_count

        for i in range(total):
            if force_cover and i == 0 and "0000" not in existing_sheet_numbers:
                num = "0000"
            else:
                num = get_next_sheet_number(sheet_type, existing_sheet_numbers)
            if not num:
                continue
            existing_sheet_numbers.append(num)
            final_name = user_name if user_name else generate_smart_name(
                sheet_type, sheet_number=num)
            sheets.append(create_payload(num, final_name.upper()))

    # Sort by sheet number ascending so downstream (Revit creation order
    # AND chat success-report) always reads low → high.
    sheets.sort(key=lambda p: sort_key_sheet_number(p.get("sheet_number")))
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