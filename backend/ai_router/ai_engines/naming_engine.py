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

# Slot-specific names live inside each scheme's category config under
# `named_slots`. Indexed by primary slot position (slot offset / primary_increment):
#   v1 A0: idx 0 = slot 0000 → "COVER"; idx 1 = slot 0010 → "DRAWING INDEX"; …
#   v2 A0: idx 0 = slot 00000 → "COVER"; idx 1 = slot 00100 → "DRAWING INDEX"; …
# A `None` placeholder means "no name at this slot" → falls back to
# CATEGORY_DEFAULT_NAME below, then to "CUSTOM SHEET".

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

# =====================================================================
# NUMBERING SCHEMES (single source of truth)
#
# Every sheet-number computation reads from the active scheme — base values,
# slot increments, level/sub-level encoding, and slot string format. This
# lets us swap between numbering conventions (e.g. 4-digit small-project vs
# future 5-digit large-project) without touching slot logic.
#
# Per-category fields:
#   type ........... "level" (A1/A5) or "sequence" (everything else)
#   base ........... starting integer for the category (e.g. A1 = 1000)
#   format ......... str.format template applied to the integer slot
#   primary_increment . step between primary slots (e.g. 1010 → 1020 = +10)
#   sub_increment ..... step for M/T sub-level slots and basement spacing
#   level_increment ... only "level" type — step between L<N> slots
#                       (e.g. L1 = base+10, L2 = base+20 → +10)
#   sub_level_increment . only "level" type — step for M/T variants
#                         (collisions on L1 push +1 in iso19650_4digit)
#   site_slots ........ only "level" type — count of SITE slots reserved at
#                       the very base (A1 has 1 in 4-digit; A5 has 0 = no SITE)
#   basement_count .... only "level" type — max basement number supported
#                       (B<n> for n in 1..basement_count)
#   roof_offset ....... only "level" type — "auto" = max above-grade L + 1,
#                       or an int = fixed offset from base
#
# Two schemes ship today: iso19650_4digit (small projects, default) and
# iso19650_5digit (large projects). Adding a third is a config addition,
# not a code change. Legacy keys "v1_small" / "v2_large" are auto-migrated
# in resolve_scheme_for_request() so old saved sessions keep working.
# =====================================================================

SCHEMES = {
    "iso19650_4digit": {
        "digit_count": 4,
        "categories": {
            "A0": {"type": "sequence", "base": 0,    "primary_increment": 10, "sub_increment": 1, "format": "{:04d}",
                   "named_slots": ["COVER", "DRAWING INDEX", "SITE AND VICINITY PLAN",
                                   "STANDARD SYMBOLS", "SAFETY PLAN", "WALL TYPES"]},
            "A1": {"type": "level",    "base": 1000, "level_increment": 10, "sub_level_increment": 1,
                                       "site_slots": 1, "basement_count": 9, "roof_offset": "auto",
                                       "format": "{:04d}"},
            "A2": {"type": "sequence", "base": 2000, "primary_increment": 10, "sub_increment": 1, "format": "{:04d}"},
            "A3": {"type": "sequence", "base": 3000, "primary_increment": 10, "sub_increment": 1, "format": "{:04d}"},
            "A4": {"type": "sequence", "base": 4000, "primary_increment": 10, "sub_increment": 1, "format": "{:04d}"},
            "A5": {"type": "level",    "base": 5000, "level_increment": 10, "sub_level_increment": 1,
                                       "site_slots": 0, "basement_count": 9, "roof_offset": "auto",
                                       "format": "{:04d}"},
            # A6 reordered per V2 spec (applied to both schemes for consistency):
            # FLOOR PATTERN PLAN at 6010, ENLARGED TOILET PLAN at 6020.
            "A6": {"type": "sequence", "base": 6000, "primary_increment": 10, "sub_increment": 1, "format": "{:04d}",
                   "named_slots": [None, "FLOOR PATTERN PLAN", "ENLARGED TOILET PLAN", "CANOPY PLAN"]},
            "A7": {"type": "sequence", "base": 7000, "primary_increment": 10, "sub_increment": 1, "format": "{:04d}",
                   "named_slots": [None, "ENLARGED STAIR PLAN", "ENLARGED STAIR SECTION",
                                   "ENLARGED RAMP PLAN", "ENLARGED LIFT PLAN"]},
            "A8": {"type": "sequence", "base": 8000, "primary_increment": 10, "sub_increment": 1, "format": "{:04d}",
                   "named_slots": [None, "DOOR SCHEDULE", "WINDOW SCHEDULE"]},
            "A9": {"type": "sequence", "base": 9000, "primary_increment": 10, "sub_increment": 1, "format": "{:04d}"},
            "X0": {"type": "sequence", "base": 0,    "primary_increment": 10, "sub_increment": 1, "format": "X{:03d}"},
        },
    },
    "a49_dotted": {
        # Dotted A49 sheet naming format: A<series>.<NN> (e.g. A1.03).
        # Brought back per staff feedback after the iso19650 4/5-digit shipped
        # as the default. KEY DIFFERENCE from 4/5-digit: A1/A5 are NOT
        # level-based here — they're "level_sequence" type, meaning the user
        # creates sheets in any order and the engine just hands out the next
        # free sequential slot. SITE still anchors at .00 in A1 (matching the
        # other schemes' contract), but L1/L2/B1 etc. land at whichever sequence
        # slot is next free, not at a deterministic level→slot mapping.
        #
        # gap_fill: True on every category — if a sheet is deleted mid-project,
        # the next created sheet reuses the freed slot (different from 4/5-digit
        # which always advance from max).
        #
        # range_size = 100: each category occupies slots 0-99 (formats as 00-99).
        #
        # Sub-parts (A1.03.1, A1.03.2 for splitting drawings) are deferred to
        # Phase 2 — Phase 1 ships the basic dotted scheme only.
        "digit_count": None,   # not numeric format; "format" handles per-category
        "range_size": 100,     # per-category slot capacity (00-99)
        "categories": {
            "A0": {"type": "sequence",       "base": 0, "primary_increment": 1, "sub_increment": 0,
                                             "format": "A0.{:02d}", "gap_fill": True,
                   # A0.06 is the literal slot "CUSTOM"; A0.07+ overflow into the
                   # generic "CUSTOM SHEET" fallback via CATEGORY_DEFAULT_NAME.
                   "named_slots": ["COVER", "DRAWING INDEX", "SITE AND VICINITY PLAN",
                                   "STANDARD SYMBOLS", "SAFETY PLAN", "WALL TYPES",
                                   "CUSTOM"]},
            "A1": {"type": "level_sequence", "base": 0, "primary_increment": 1, "sub_increment": 0,
                                             "format": "A1.{:02d}", "gap_fill": True,
                                             "site_slots": 1},
            "A2": {"type": "sequence",       "base": 0, "primary_increment": 1, "sub_increment": 0,
                                             "format": "A2.{:02d}", "gap_fill": True},
            "A3": {"type": "sequence",       "base": 0, "primary_increment": 1, "sub_increment": 0,
                                             "format": "A3.{:02d}", "gap_fill": True},
            "A4": {"type": "sequence",       "base": 0, "primary_increment": 1, "sub_increment": 0,
                                             "format": "A4.{:02d}", "gap_fill": True},
            "A5": {"type": "level_sequence", "base": 0, "primary_increment": 1, "sub_increment": 0,
                                             "format": "A5.{:02d}", "gap_fill": True,
                                             "site_slots": 0},
            "A6": {"type": "sequence",       "base": 0, "primary_increment": 1, "sub_increment": 0,
                                             "format": "A6.{:02d}", "gap_fill": True,
                   "named_slots": [None, "FLOOR PATTERN PLAN", "ENLARGED TOILET PLAN", "CANOPY PLAN"]},
            "A7": {"type": "sequence",       "base": 0, "primary_increment": 1, "sub_increment": 0,
                                             "format": "A7.{:02d}", "gap_fill": True,
                   "named_slots": [None, "ENLARGED STAIR PLAN", "ENLARGED STAIR SECTION",
                                   "ENLARGED RAMP PLAN", "ENLARGED LIFT PLAN"]},
            "A8": {"type": "sequence",       "base": 0, "primary_increment": 1, "sub_increment": 0,
                                             "format": "A8.{:02d}", "gap_fill": True,
                   "named_slots": [None, "DOOR SCHEDULE", "WINDOW SCHEDULE"]},
            "A9": {"type": "sequence",       "base": 0, "primary_increment": 1, "sub_increment": 0,
                                             "format": "A9.{:02d}", "gap_fill": True},
            "X0": {"type": "sequence",       "base": 0, "primary_increment": 1, "sub_increment": 0,
                                             "format": "X0.{:02d}", "gap_fill": True},
        },
    },
    "iso19650_5digit": {
        # 5-digit format for large projects: every increment is ×10 the 4-digit
        # value (primary 100 vs 10, sub 10 vs 1). Same conceptual shape, more
        # breathing room. SITE has 10 slots in A1 instead of 1; basements span
        # 9 slots (B9-B1) at +10 spacing instead of 3 slots at +1.
        "digit_count": 5,
        "categories": {
            "A0": {"type": "sequence", "base": 0,     "primary_increment": 100, "sub_increment": 10, "format": "{:05d}",
                   "named_slots": ["COVER", "DRAWING INDEX", "SITE AND VICINITY PLAN",
                                   "STANDARD SYMBOLS", "SAFETY PLAN", "WALL TYPES"]},
            "A1": {"type": "level",    "base": 10000, "level_increment": 100, "sub_level_increment": 10,
                                       "site_slots": 10, "basement_count": 9, "roof_offset": "auto",
                                       "format": "{:05d}"},
            "A2": {"type": "sequence", "base": 20000, "primary_increment": 100, "sub_increment": 10, "format": "{:05d}"},
            "A3": {"type": "sequence", "base": 30000, "primary_increment": 100, "sub_increment": 10, "format": "{:05d}"},
            "A4": {"type": "sequence", "base": 40000, "primary_increment": 100, "sub_increment": 10, "format": "{:05d}"},
            "A5": {"type": "level",    "base": 50000, "level_increment": 100, "sub_level_increment": 10,
                                       "site_slots": 0, "basement_count": 9, "roof_offset": "auto",
                                       "format": "{:05d}"},
            # A6 reordered per V2 spec: FLOOR PATTERN first, TOILET second.
            "A6": {"type": "sequence", "base": 60000, "primary_increment": 100, "sub_increment": 10, "format": "{:05d}",
                   "named_slots": [None, "FLOOR PATTERN PLAN", "ENLARGED TOILET PLAN", "CANOPY PLAN"]},
            "A7": {"type": "sequence", "base": 70000, "primary_increment": 100, "sub_increment": 10, "format": "{:05d}",
                   "named_slots": [None, "ENLARGED STAIR PLAN", "ENLARGED STAIR SECTION",
                                   "ENLARGED RAMP PLAN", "ENLARGED LIFT PLAN"]},
            "A8": {"type": "sequence", "base": 80000, "primary_increment": 100, "sub_increment": 10, "format": "{:05d}",
                   "named_slots": [None, "DOOR SCHEDULE", "WINDOW SCHEDULE"]},
            "A9": {"type": "sequence", "base": 90000, "primary_increment": 100, "sub_increment": 10, "format": "{:05d}"},
            "X0": {"type": "sequence", "base": 0,     "primary_increment": 100, "sub_increment": 10, "format": "X{:04d}"},
        },
    },
}


def get_active_scheme(scheme_name=None):
    """Return the scheme config dict to use for slot calculations.

    When called with no arguments, returns the global default
    (iso19650_4digit). Per-request resolution should use
    `resolve_scheme_for_request(request)` instead — it inspects the
    project's existing sheets and the user's session override.
    """
    return SCHEMES[scheme_name or "iso19650_4digit"]


def _cat_cfg(sheet_type, scheme=None):
    """Look up a category's config from the active (or supplied) scheme."""
    scheme = scheme or get_active_scheme()
    return scheme["categories"].get((sheet_type or "").upper())


# ── Per-request scheme resolution ────────────────────────────────────────
# Selects iso19650_4digit or iso19650_5digit based on (in priority order):
#   1. Auto-detect from cached project sheets — if any A49-shaped sheet
#      number is 5+ chars, the project is on the 5-digit scheme (decisive
#      — wins over the override so a 5-digit project can never accidentally
#      write 4-digit slots).
#   2. Explicit session override — `request.session['ai_numbering_scheme']`,
#      used for new/empty projects where auto-detect can't decide.
#   3. Default — iso19650_4digit.
#
# This implements the user-approved "B + A combined" toggle from the
# Phase 0 design conversation: auto-detect is primary, override is a
# fallback for empty projects.

# Legacy scheme keys → new ISO19650 keys. Auto-applied to session overrides
# in resolve_scheme_for_request() so any session that still has the old
# value stored (from before the v1.2.x rename) keeps working.
_LEGACY_SCHEME_KEYS = {
    "v1_small": "iso19650_4digit",
    "v2_large": "iso19650_5digit",
}


def _detect_scheme_from_sheets(sheets):
    """Inspect a list of cached sheet-number strings and return the scheme
    name that matches the dominant numbering shape:
      - "a49_dotted" if any sheet matches the dotted format (A0.05, A1.03, X0.02).
      - "iso19650_5digit" if any iso19650 sheet is 5+ chars.
      - "iso19650_4digit" if at least one iso19650 sheet is present but none
        is 5+ chars.
      - None if the cache is empty or contains no recognisably-A49 sheets
        (caller falls back to override or default).

    Detection is decisive — once a single sheet of either shape exists,
    the project is locked to that scheme (mixed-scheme projects are
    explicitly disallowed; auto-detect wins over the session override).

    Only sheets whose number portion matches a known A49 shape count
    toward detection. User-renamed / non-conformant sheets are ignored
    so they can't poison the auto-detect.
    """
    if not sheets:
        return None
    saw_iso19650 = False
    for s in sheets:
        num = str(s).split(" - ")[0].strip().upper()
        if not num:
            continue
        # a49_dotted shape: A<digit>.<digits> or X0.<digits> — decisive on first hit.
        if _DOTTED_SHEET_RE.match(num):
            return "a49_dotted"
        # iso19650 shape: pure digits, or X + digits (no dot).
        is_iso = (num.isdigit()) or (num.startswith("X") and num[1:].isdigit())
        if not is_iso:
            continue
        saw_iso19650 = True
        if len(num) >= 5:
            return "iso19650_5digit"
    return "iso19650_4digit" if saw_iso19650 else None


def resolve_scheme_for_request(request):
    """Determine the active numbering scheme for this request's session.

    Returns the scheme dict (suitable for passing into
    `build_sheets_payload(..., scheme=...)`, `get_next_sheet_number(..., scheme=...)`,
    etc.). Falls back to the global default (iso19650_4digit) when no
    request context is available.
    """
    if request is None:
        return get_active_scheme()

    # 1. Auto-detect from cached project sheets — wins over override so a
    #    5-digit project is never written to 4-digit by accident.
    cached_sheets = []
    try:
        cached_sheets = request.session.get("ai_last_known_sheets") or []
    except Exception:
        pass
    detected = _detect_scheme_from_sheets(cached_sheets)
    if detected and detected in SCHEMES:
        return SCHEMES[detected]

    # 2. Session override — used for new/empty projects.
    try:
        override = request.session.get("ai_numbering_scheme")
    except Exception:
        override = None
    # Migrate legacy keys ("v1_small" / "v2_large") to current names so old
    # saved sessions keep working without manual cleanup.
    if override in _LEGACY_SCHEME_KEYS:
        override = _LEGACY_SCHEME_KEYS[override]
        request.session["ai_numbering_scheme"] = override
        request.session.modified = True
    if override and override in SCHEMES:
        return SCHEMES[override]

    # 3. Default
    return SCHEMES["iso19650_4digit"]


# Categories that take a level_name argument when generating a sheet number.
# Includes both:
#   - "level"          (iso19650 A1/A5, deterministic level→slot mapping)
#   - "level_sequence" (a49_dotted A1/A5, SITE anchor + sequence allocation)
# Derived from the active scheme so swapping schemes can't desync this.
def _level_based_categories(scheme=None):
    scheme = scheme or get_active_scheme()
    return {name for name, cfg in scheme["categories"].items()
            if cfg.get("type") in ("level", "level_sequence")}

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

def _series_base(sheet_type, scheme=None):
    """Numeric base for a sheet category, read from the active scheme.
    Returns None for X0 (preserves the prior contract — call sites use
    ``_series_base(...) or 0``)."""
    st = (sheet_type or "").upper()
    if st == "X0": return None
    cat = _cat_cfg(st, scheme)
    return cat["base"] if cat else None

def _format_slot(sheet_type, slot, scheme=None):
    """Render an integer slot as the displayed sheet-number string,
    using the format template defined by the active scheme.
    iso19650_4digit: A0-A9 → 4-digit zero-padded; X0 → 'X' + 3-digit.
    iso19650_5digit: A0-A9 → 5-digit zero-padded; X0 → 'X' + 4-digit."""
    cat = _cat_cfg(sheet_type, scheme)
    if cat and cat.get("format"):
        return cat["format"].format(int(slot))
    # Fallback (shouldn't happen for registered categories)
    scheme = scheme or get_active_scheme()
    return f"{int(slot):0{scheme['digit_count']}d}"

def _range_size(scheme):
    """Per-category slot capacity for the given scheme.
    iso19650_4digit: 1000 (A0=0-999, A1=1000-1999, …)
    iso19650_5digit: 10000 (A0=0-9999, A1=10000-19999, …)
    a49_dotted: 100 (each category 0-99, prefixed with series letter+digit)"""
    if scheme.get("range_size") is not None:
        return scheme["range_size"]
    return 10 ** (scheme["digit_count"] - 1)


# a49_dotted format: A0.05, A1.03, X0.02, plus optional sub-part (A1.03.1,
# A5.03.3) for splitting drawings or attaching mezzanines to a parent floor.
# `_parse_slot` reads only group(1) and group(2) — the optional sub-component
# is captured for any caller that needs it but is treated as a sibling of the
# parent slot for allocation purposes (auto-allocator never emits sub-parts;
# users add them manually via the rename wizard's editable cells).
_DOTTED_SHEET_RE = re.compile(r"^([AX]\d)\.(\d{1,3})(?:\.(\d+))?$")


def _parse_slot(sheet_number, scheme=None):
    """Extract the numeric slot from a sheet-number string.
    Returns (slot_int, category_inferred) or (None, None) if not parseable.

    Recognises three formats (in priority order):
      1. a49_dotted: 'A1.03', 'A0.05', 'X0.02' — series prefix is the
         category, the post-dot integer is the slot.
      2. X-prefix numeric: 'X010' (4-digit) / 'X0100' (5-digit) — category
         is X0, slot is the integer after 'X'.
      3. Pure numeric: '1010' / '10100' — category inferred by which
         scheme range owns the integer.
    """
    if not sheet_number: return None, None
    s = str(sheet_number).strip().upper()

    # 1. a49_dotted (must be checked before bare 'X...' since 'X0.05' starts with X)
    m = _DOTTED_SHEET_RE.match(s)
    if m:
        try:
            return int(m.group(2)), m.group(1)
        except (ValueError, TypeError):
            return None, None

    # 2. X-prefix numeric (iso19650 X-series)
    if s.startswith("X"):
        try: return int(s[1:]), "X0"
        except (ValueError, TypeError): return None, None

    # 3. Pure numeric — find which iso19650 category's range owns it.
    try:
        n = int(s)
    except (ValueError, TypeError):
        return None, None

    scheme = scheme or get_active_scheme()
    if scheme.get("digit_count") is None:
        # Active scheme is dotted; numeric input doesn't belong to any
        # registered category. Return as A0 fallback.
        return n, "A0"
    range_size = _range_size(scheme)
    for cat_name, cat_cfg in scheme["categories"].items():
        if cat_name == "X0": continue
        base = cat_cfg.get("base")
        if base is None: continue
        if base <= n < base + range_size:
            return n, cat_name
    # Fallback: derive from leading digit so unrecognised numbers still
    # round-trip (matches prior "A{n // 1000}" behaviour for 4-digit numbers).
    if n < range_size: return n, "A0"
    return n, f"A{n // range_size}"

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
                       request_levels=None, scheme=None):
    """Compute the *initial* slot integer for a sheet, before collision check.

    For ROOF/TOP: prefers the highest above-grade level in `request_levels`
    (the levels the user is creating sheets for in this batch). Falls back
    to `project_levels` only when the request has no above-grade levels —
    e.g. "Create sheet for ROOF only". This insulates ROOF placement from
    stale/dirty session caches and from hidden Revit reference levels that
    don't represent real building floors.

    All slot math is parameterized via the active scheme:
      - level_increment ........ step between L<N> slots
      - sub_level_increment .... step for M/T variants and basement spacing
      - basement_count ......... max basement number supported
      - site_slots ............. count of SITE slots reserved at base (0 = no SITE)
      - roof_offset ............ "auto" (= max above-grade L + 1) or fixed int

    Returns:
        int slot, or None if the combination is invalid (e.g. A5+SITE,
        unsupported level naming, level out of supported range).
    """
    st = (sheet_type or "").upper()
    cat = _cat_cfg(st, scheme)
    if cat is None:
        return None
    cat_type = cat.get("type")

    # level_sequence (a49_dotted A1/A5): only SITE has a deterministic slot
    # (anchored at 0 for A1; rejected for A5). Everything else is sequence-
    # allocated by the caller via _next_sequence_slot().
    if cat_type == "level_sequence":
        if not level_name:
            return None
        from .level_matcher import extract_level_signature
        sig = extract_level_signature(level_name)
        if sig["special"] == "SITE":
            if cat.get("site_slots", 0) <= 0:
                return None  # A5 has no SITE
            return cat["base"]
        return None  # All other levels handled as sequence

    if cat_type != "level":
        return None  # Pure sequence — caller uses _next_sequence_slot

    base = cat["base"]
    level_inc = cat["level_increment"]
    sub_inc = cat["sub_level_increment"]
    site_slots = cat.get("site_slots", 0)
    basement_count = cat.get("basement_count", 0)
    roof_offset = cat.get("roof_offset", "auto")

    if not level_name:
        return None

    from .level_matcher import extract_level_signature
    sig = extract_level_signature(level_name)

    # SITE → base anchor (only when this category reserves SITE slots).
    # A5 has site_slots=0 → SITE is rejected for ceiling plans.
    if sig["special"] == "SITE":
        if site_slots <= 0:
            return None
        return base  # First SITE slot — collision loop allocates further sites

    # ROOF / TOP → max above-grade L + 1, scaled by the level increment.
    if sig["special"] in ("RF", "TOP"):
        if roof_offset != "auto":
            return base + roof_offset
        max_n = _max_above_grade_level(request_levels) if request_levels else 0
        if max_n == 0:
            max_n = _max_above_grade_level(project_levels)
        if max_n == 0:
            max_n = 1  # Defensive: project/request with no above-grade levels
        return base + (max_n + 1) * level_inc

    # Above grade L<N>
    if sig["prefix"] == "L" and sig["digit"] is not None:
        n = sig["digit"]
        if n < 1 or n > 99:
            return None  # Out of range; spec caps at L99 for now
        slot = base + n * level_inc
        if sig["suffix"]:
            slot += sub_inc  # M / T / … suffix → +1 (v1) or +10 (v2), FCFS via collision loop
        return slot

    # Below grade B<N> — natural slot = base + (10 - N) * sub_increment
    # (so B<basement_count> sits closest to base, B1 closest to L1).
    # Suffix variants (B<N>M, B<N>T, …) take the parent's natural slot
    # (closer to grade); the bare basement shifts DOWN by sub_increment to
    # make room. Cascades when multiple variants exist.
    if sig["prefix"] == "B" and sig["digit"] is not None:
        n = sig["digit"]
        if n < 1 or n > basement_count:
            return None

        natural_slot = base + (10 - n) * sub_inc

        # Build the basement signature set from the request context. Each
        # entry is (digit, suffix). Distinct M/T variants count separately;
        # accidental duplicates collapse via the set.
        request_basements = set()
        if request_levels:
            from .level_matcher import extract_level_signature as _extract
            for lvl in request_levels:
                ls = _extract(lvl)
                if (ls["prefix"] == "B" and ls["digit"] is not None
                        and 1 <= ls["digit"] <= basement_count):
                    request_basements.add((ls["digit"], ls["suffix"] or ""))

        if sig["suffix"]:
            # Suffix variant: takes its parent's natural slot, shifted only
            # by suffix variants ABOVE this level (smaller digit).
            shift = sum(1 for d, s in request_basements if d < n and s)
        else:
            # Bare basement: shifted by suffix variants AT OR ABOVE this level.
            shift = sum(1 for d, s in request_basements if d <= n and s)

        slot = natural_slot - shift * sub_inc
        if slot < base + sub_inc:
            return None  # Overflowed past the basement range
        return slot

    return None

# ── Sequence-based slot selection (A0, A2-A4, A6-A8, X0) ─────────────────

def _next_sequence_slot(sheet_type, existing_numbers, scheme=None):
    """Pick the next primary-increment slot for a sequence-based category.

    Default policy (iso19650_4digit / iso19650_5digit): always advance from
    the highest existing slot — never gap-fill. Insert-between is a
    deliberate user action handled by a future wizard.

    a49_dotted policy (`gap_fill: True` on the category): pick the LOWEST
    free slot (reuses freed-up holes from deleted sheets).

    Starting slot:
      - A0 / X0:                           slot 0 (cover slot)
      - level_sequence (a49_dotted A1):    slot 1 (skip slot 0 reserved for SITE)
      - everything else:                   base + primary_increment
    """
    st = (sheet_type or "").upper()
    cat_slots = set(_existing_in_category(st, existing_numbers))
    base = _series_base(st, scheme) or 0  # X0 has no numeric base
    cat = _cat_cfg(st, scheme) or {}
    primary_inc = cat.get("primary_increment", 10)
    gap_fill = cat.get("gap_fill", False)
    cat_type = cat.get("type")

    # Determine the lowest possible slot for this category.
    if cat_type == "level_sequence":
        # A1 reserves slot 0 for SITE; A5 has no slot 0. Either way, the
        # general sequence allocation skips slot 0.
        starting = base + primary_inc
    elif st in ("A0", "X0"):
        starting = base + 0
    else:
        starting = base + primary_inc

    if not cat_slots:
        return starting

    if gap_fill:
        # Walk upward from the lowest valid slot until a free one is found.
        slot = starting
        while slot in cat_slots:
            slot += primary_inc
        return slot

    # Default: advance from the highest existing slot.
    max_slot = max(cat_slots)
    next_slot = ((max_slot // primary_inc) + 1) * primary_inc
    # Defensive: if max < starting (e.g. only A1.00 exists, asking for next),
    # use starting instead.
    return max(next_slot, starting)

# ── Public: get next sheet number ────────────────────────────────────────

def get_next_sheet_number(sheet_type, existing_numbers, level_name=None,
                          project_levels=None, request_levels=None, scheme=None):
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
        scheme: optional override for the active numbering scheme.

    Returns:
        Sheet-number string (e.g. '1010', 'X020'), or None when the
        combination is invalid (e.g. A5 + SITE).
    """
    scheme = scheme or get_active_scheme()
    st = (sheet_type or "").upper()
    cat = _cat_cfg(st, scheme) or {}
    cat_type = cat.get("type")
    existing_set = set(str(n).upper() for n in (existing_numbers or []))
    range_size = _range_size(scheme)

    # 1. Pure level-based (iso19650 A1/A5): slot dictated deterministically
    #    by the level identity.
    if cat_type == "level" and level_name:
        slot = compute_sheet_slot(st, level_name=level_name,
                                  project_levels=project_levels,
                                  request_levels=request_levels,
                                  scheme=scheme)
        if slot is None:
            return None

        # Collision direction depends on whether the level is above or below
        # grade. Above-grade / special levels push UP by sub_increment (into
        # the M/T sub-slot range). Basements push DOWN by sub_increment —
        # pushing UP would land the duplicate in L1's territory.
        base = cat["base"]
        sub_inc = cat["sub_level_increment"]
        from .level_matcher import extract_level_signature as _extract
        sig = _extract(level_name)
        is_basement = sig["prefix"] == "B" and sig["digit"] is not None

        step = -sub_inc if is_basement else sub_inc
        while _format_slot(st, slot, scheme) in existing_set:
            slot += step
            if is_basement and slot < base + sub_inc:
                return None  # Underflowed below the basement range
            if not is_basement and slot >= base + range_size:
                return None  # Overflowed into the next category
        return _format_slot(st, slot, scheme)

    # 2. level_sequence (a49_dotted A1/A5): SITE goes to slot 0; everything
    #    else is sequence-allocated by the user (next free slot).
    if cat_type == "level_sequence":
        if level_name:
            from .level_matcher import extract_level_signature as _extract
            sig = _extract(level_name)
            if sig["special"] == "SITE":
                slot = compute_sheet_slot(st, level_name=level_name, scheme=scheme)
                if slot is None:
                    return None  # A5 + SITE rejected
                # Collision: SITE slot already taken — for now, no second SITE
                # slot in a49_dotted (Phase 2 will allow A1.00.1, A1.00.2 splits).
                if _format_slot(st, slot, scheme) in existing_set:
                    return None
                return _format_slot(st, slot, scheme)
        # Non-SITE level (or no level supplied) → fall through to sequence allocation.

    # 3. Sequence (or level_sequence non-SITE fallthrough): allocate next slot.
    primary_inc = cat.get("primary_increment", 10)
    slot = _next_sequence_slot(st, existing_numbers or [], scheme=scheme)
    while _format_slot(st, slot, scheme) in existing_set:
        slot += primary_inc
        if st != "X0":
            base = cat.get("base", 0)
            if slot >= base + range_size:
                return None
    return _format_slot(st, slot, scheme)

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


def _ordinal(n):
    """English ordinal suffix: 1→1ST, 2→2ND, 3→3RD, 4→4TH, 11→11TH, 21→21ST.
    Used by the a49_dotted scheme; iso19650 schemes keep 'LEVEL N' wording."""
    if 11 <= (n % 100) <= 13:
        return f"{n}TH"
    return f"{n}{ {1: 'ST', 2: 'ND', 3: 'RD'}.get(n % 10, 'TH') }"


def _level_label_dotted(level_name):
    """A49-dotted variant of _level_label.

    Differences from the iso19650 wording:
      • Above-grade floors use ordinals: L1→1ST, L2→2ND, L3→3RD, L21→21ST.
      • Basements drop the 'LEVEL ' prefix:  B1→B1, B2→B2.
      • Mezzanines (suffix 'M') skip the ordinal:  L1M→1M, B1M→B1M.
      • Roof drops the 'LEVEL ' prefix too: ROOF→ROOF.
    """
    if not level_name:
        return ("", "normal")
    from .level_matcher import extract_level_signature
    sig = extract_level_signature(level_name)
    if sig["special"] == "SITE": return ("SITE", "site")
    if sig["special"] in ("RF", "TOP"): return ("ROOF", "roof")
    if sig["special"] == "MZ": return ("MEZZANINE", "normal")
    if sig["special"]: return (sig["special"], "normal")
    if sig["digit"] is not None:
        prefix = sig["prefix"] or "L"
        suffix = sig["suffix"] or ""
        if prefix == "L":
            # Above-grade floors: ordinal unless the level carries a suffix
            # (mezzanine / transfer floors), in which case the digit+suffix
            # combo is the readable form (e.g. L1M → "1M").
            if suffix:
                return (f"{sig['digit']}{suffix}", "normal")
            return (_ordinal(sig['digit']), "normal")
        # Basements (B) and any other letter-prefixed levels keep their
        # raw form, no 'LEVEL ' prefix.
        return (f"{prefix}{sig['digit']}{suffix}", "normal")
    return (str(level_name).upper(), "normal")

def generate_smart_name(sheet_type, sheet_number=None, level_name=None, scheme=None):
    """Default sheet name for a category + slot + (optional) level.

    A1/A5 use the level: 'LEVEL 1 FLOOR PLAN', 'LEVEL B1 CEILING PLAN', etc.
    Roof drops 'FLOOR' on A1 → 'LEVEL ROOF PLAN'. Site is rejected on A5.
    Other categories look up the slot in the active scheme's `named_slots`,
    then fall back to CATEGORY_DEFAULT_NAME (A2/A3/A4/A9/X0), then 'CUSTOM SHEET'.
    """
    st = (sheet_type or "").upper()

    # a49_dotted uses a different level-label style (ordinals, no 'LEVEL '
    # prefix on basements/mezzanines). Detect via digit_count=None — that's
    # the dotted scheme's signature in SCHEMES.
    is_dotted = scheme is not None and scheme.get("digit_count") is None
    label_fn = _level_label_dotted if is_dotted else _level_label

    # Level-based naming (A1 / A5)
    if st == "A1" and level_name:
        label, kind = label_fn(level_name)
        if kind == "site": return "SITE PLAN"
        if kind == "roof": return f"{label} PLAN"  # 'LEVEL ROOF PLAN' / 'ROOF PLAN' (no FLOOR)
        return f"{label} FLOOR PLAN"

    if st == "A5" and level_name:
        label, kind = label_fn(level_name)
        if kind == "site": return "SITE CEILING PLAN"  # rejected upstream
        return f"{label} CEILING PLAN"

    # Sequence-based naming — index into the scheme's named_slots by primary
    # slot position (slot offset from base, divided by the category's primary_increment).
    slot, _ = _parse_slot(sheet_number, scheme) if sheet_number else (None, None)
    cat = _cat_cfg(st, scheme) or {}
    names = cat.get("named_slots", [])
    fallback = CATEGORY_DEFAULT_NAME.get(st, "CUSTOM SHEET")

    if slot is None:
        return fallback

    base = _series_base(st, scheme)
    primary_inc = cat.get("primary_increment", 10)

    offset = slot if st == "X0" else slot - (base or 0)
    idx = offset // primary_inc
    if 0 <= idx < len(names) and names[idx]:
        return names[idx]
    return fallback

def make_standard_sheet(sheet_type, sheet_number, sheet_name, stage):
    return {"sheet_number": sheet_number, "sheet_name": sheet_name, "sheet_type": sheet_type, "project_phase": PROJECT_PHASE_MAP.get(stage, None), "sheet_set": SHEET_SET_MAP.get(sheet_type, None), "discipline": "ARCHITECTURE"}

def make_custom_sheet(sheet_type, sheet_number, sheet_name):
    return {"sheet_number": sheet_number, "sheet_name": sheet_name, "sheet_type": sheet_type, "project_phase": None, "sheet_set": None, "discipline": None}

def build_sheets_payload(request_data, existing_sheet_numbers, scheme=None):
    """Generate the list of sheet payloads for a create_sheet command.

    Reads request_data['project_levels'] (cached from Revit) to resolve
    ROOF/TOP slots correctly. Falls back to request_data['levels'] if the
    project inventory wasn't passed in (degraded — ROOF will land at
    max(levels)+10 instead of project max+10).

    `scheme` selects the active numbering scheme (defaults to iso19650_4digit via
    `get_active_scheme()`)."""
    scheme = scheme or get_active_scheme()
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

    # Cover-slot string for this scheme — used for both the auto-titleblock
    # check below and the force_cover allocation. v1: "0000", v2: "00000".
    cover_slot_str = _format_slot("A0", 0, scheme)

    sheets = []

    def create_payload(num, name):
        if forced_fam and forced_type:
            fam, t_type = forced_fam, forced_type
        else:
            fam, t_type = determine_titleblock(num, override_family=raw_tb, mode="STANDARD")
        # Cover slot always uses the cover titleblock
        if num == cover_slot_str:
            fam, t_type = "A49_TB_A1_Horizontal_Cover", "Cover"
        if mode == "CUSTOM" or sheet_type.startswith("X"):
            p = make_custom_sheet(sheet_type, num, name)
        else:
            p = make_standard_sheet(sheet_type, num, name, stage)
        p["titleblock_family"] = fam
        p["titleblock_type"] = t_type
        return p

    # A0 + cover keyword in user input → force the cover slot
    force_cover = False
    if sheet_type == "A0":
        view_type = request_data.get("view_type")
        if (user_name and "cover" in user_name.lower()) or \
           (view_type and "cover" in view_type.lower()):
            force_cover = True

    level_based = _level_based_categories(scheme)

    if levels and sheet_type in level_based:
        for lvl in levels:
            num = get_next_sheet_number(sheet_type, existing_sheet_numbers,
                                        level_name=lvl,
                                        project_levels=project_levels,
                                        request_levels=levels,
                                        scheme=scheme)
            if not num:
                # Invalid combo (e.g. A5+SITE) — skip silently; caller decides
                # whether to surface "no sheets created" message.
                continue
            existing_sheet_numbers.append(num)
            final_name = user_name if user_name else generate_smart_name(
                sheet_type, sheet_number=num, level_name=lvl, scheme=scheme)
            sheets.append(create_payload(num, final_name.upper()))
    else:
        try:
            total_count = int(count) if count else 1
        except (TypeError, ValueError):
            total_count = 1
        total = len(levels) if levels else total_count

        for i in range(total):
            if force_cover and i == 0 and cover_slot_str not in existing_sheet_numbers:
                num = cover_slot_str
            else:
                num = get_next_sheet_number(sheet_type, existing_sheet_numbers, scheme=scheme)
            if not num:
                continue
            existing_sheet_numbers.append(num)
            final_name = user_name if user_name else generate_smart_name(
                sheet_type, sheet_number=num, scheme=scheme)
            sheets.append(create_payload(num, final_name.upper()))

    # Sort by sheet number ascending so downstream (Revit creation order
    # AND chat success-report) always reads low → high.
    sheets.sort(key=lambda p: sort_key_sheet_number(p.get("sheet_number")))
    return sheets

# =====================================================================
# MAIN ENTRY POINT (RESTORED)
# =====================================================================

def apply(request_data, existing_view_names, existing_sheet_numbers,
          force_suffix=False, forced_suffix_number=None, forced_suffix_base=None,
          scheme=None):
    """Main naming-engine entry point.

    `scheme` selects the active numbering scheme (defaults to iso19650_4digit via
    `get_active_scheme()`). Callers in the request flow should resolve via
    `resolve_scheme_for_request(request)` and pass the result here so each
    project uses its detected scheme.
    """
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
        sheets_out = build_sheets_payload(request_data, existing_sheet_numbers, scheme=scheme)
        return {
            "views": [], "sheets": sheets_out,
            "sheet_type": request_data.get("sheet_category"),
            "stage": request_data.get("stage"), "final_template": None
        }

    return {"views": [], "sheets": [], "category": None, "stage": None, "final_template": None}

def get_default_template_for_stage(stage, view_type_raw):
    return None