# ======================================================================
# level_matcher.py — Smart resolution of normalized level tokens against
# the project's ACTUAL Revit level names.
#
# Why: A49 projects use multiple naming conventions (English plain,
# English with elevation prefix, Thai with elevation prefix, intermediate
# suffixes like 7T / 6M / B1M). Rather than maintaining infinite parser
# variants, we cache the project's actual level names when the wizard
# fetches project info, then resolve user-supplied tokens (L1, L7T, RF,
# SITE, B1M, TOP) to those exact names.
#
# The downstream C# add-in then receives the EXACT project level name
# and can match it directly without heuristic name parsing.
#
# ── DESIGN CONTRACT ───────────────────────────────────────────────────
#   - Empty cache → return tokens unchanged (no regression)
#   - Token has no match in cache → pass through unchanged (current behavior)
#   - Token matches a project level → replace with that level's exact name
#
# ── THE TWO LISTS, AND WHEN TO ADD TO WHICH ───────────────────────────
#
#   _SPECIAL_PHRASES — for NAMED levels with NO digit
#     Maps a phrase → fixed token (RF, TOP, SITE, MZ, PD, AT).
#     Add an entry here when:
#       * The level has a unique name like "ROOF LEVEL" / "ระดับพื้นชั้นดาดฟ้า"
#       * It does NOT contain a digit that disambiguates it
#     Order matters: list LONGER phrases first so e.g. "ดาดฟ้า" inside
#     "ระดับพื้นชั้นดาดฟ้า" doesn't shadow the more specific entry.
#
#   _LANG_PREFIXES — for NUMBERED levels with a language wrapper
#     Strips the wrapper so the digit-extraction regex below can parse
#     out (prefix-letter, digit, suffix-letter).
#     Add an entry here when:
#       * The level is "<LANGUAGE_WORD> <DIGIT><OPTIONAL_SUFFIX>"
#         e.g. "LEVEL 7T", "ระดับพื้นชั้น 2", "ระดับชั้นใต้ดิน B1M"
#       * The wrapper word(s) is the only thing that needs removing
#     Basements DO live here (not in _SPECIAL_PHRASES) because B1, B2,
#     B1M, B2M etc. are digit-bearing and use the (B, N, suffix) tuple.
#
# ── ADDING A NEW LANGUAGE / NAMING CONVENTION ─────────────────────────
#   1. Add Thai/foreign special-name entries to _SPECIAL_PHRASES
#   2. Add Thai/foreign wrapper-word entries to _LANG_PREFIXES
#   3. Add equivalent regex patterns to level_engine.py's parse_levels
#      (so user input → token, not just project names → token)
#   4. Add a fixture row in test_level_matcher.py and run it
# ======================================================================

import re

# Same special-name table as level_engine.py SPECIAL_LEVEL_MAP, restated here
# so this module is independent. Keep these in sync if either is extended.
_SPECIAL_PHRASES = [
    # Longer phrases first so partial matches (e.g. "ดาดฟ้า" inside
    # "ระดับพื้นชั้นดาดฟ้า") don't shadow the more specific entry.
    ("ระดับพื้นชั้นดาดฟ้า", "RF"),
    ("ระดับสูงสุดของอาคาร", "TOP"),
    ("ชั้นดาดฟ้า",          "RF"),
    ("สูงสุดของอาคาร",      "TOP"),
    ("ระดับพื้นดิน",        "SITE"),
    ("ระดับสูงสุด",         "TOP"),
    ("ดาดฟ้า",              "RF"),
    ("พื้นดิน",             "SITE"),
    ("roof level",          "RF"),
    ("site level",          "SITE"),
    ("site plan",           "SITE"),
    ("top of building",     "TOP"),
    ("top level",           "TOP"),
    ("ground level",        "SITE"),
    ("ground floor",        "SITE"),
    ("roof",                "RF"),
    ("site",                "SITE"),
    ("top",                 "TOP"),
    ("parapet",             "TOP"),
    ("mezzanine",           "MZ"),
    ("podium",              "PD"),
    ("attic",               "AT"),
    # Short codes (length ≤ 2 → exact match only, see _check_special).
    # Required so the parser tokens RF / MZ / PD / AT round-trip back to a
    # signature when the matcher receives them as user input.
    ("rf", "RF"),
    ("mz", "MZ"),
    ("pd", "PD"),
    ("at", "AT"),
]

# Strip leading elevation prefix like "+45.45 " or "-2.42 "
_ELEV_PREFIX_RE = re.compile(r"^\s*[+-]?\d+\.\d+\s+")

# Strip language-specific level prefixes (English + Thai)
_LANG_PREFIXES = [
    re.compile(r"^\s*level\s+", re.IGNORECASE),
    re.compile(r"^\s*lvl\s+",   re.IGNORECASE),
    re.compile(r"^\s*floor\s+", re.IGNORECASE),
    re.compile(r"^\s*ระดับพื้นชั้น\s*"),
    re.compile(r"^\s*ผังพื้นชั้น\s*"),
    re.compile(r"^\s*ระดับชั้นใต้ดิน\s*"),
    re.compile(r"^\s*ชั้นที่\s*"),
    re.compile(r"^\s*ชั้น\s*"),
]


def extract_level_signature(level_name):
    """
    Reduce ANY level name (user input OR project Revit name) to a comparable
    signature dict:

        {special, prefix, digit, suffix, raw}

    where (special) is set for named levels (RF, SITE, TOP, MZ, PD, AT)
    and (prefix, digit, suffix) is set for numbered levels (L7T → L,7,T).

    Both fields will be None on parse failure — caller falls back to raw.
    """
    if not level_name or not isinstance(level_name, str):
        return {"special": None, "prefix": None, "digit": None, "suffix": None, "raw": ""}

    raw = level_name.strip()
    cleaned = raw

    # 1. Strip elevation prefix (works on the raw string, not lowercased)
    cleaned = _ELEV_PREFIX_RE.sub("", cleaned).strip()

    # 2. Special-name lookup. Long phrases use substring match (longest first
    # in the list to avoid the shorter-phrase shadow). Short codes (≤ 2 chars)
    # use exact-match only — substring matching "at" against "atrium" / "patio"
    # or "rf" against arbitrary level names would create false positives.
    cleaned_low = cleaned.lower()
    cleaned_stripped = cleaned_low.strip()
    for phrase, token in _SPECIAL_PHRASES:
        if len(phrase) <= 2:
            if cleaned_stripped == phrase:
                return {"special": token, "prefix": None, "digit": None, "suffix": None, "raw": raw}
        else:
            if phrase in cleaned_low:
                return {"special": token, "prefix": None, "digit": None, "suffix": None, "raw": raw}

    # 3. Strip language prefixes
    for pat in _LANG_PREFIXES:
        new_cleaned = pat.sub("", cleaned).strip()
        if new_cleaned != cleaned:
            cleaned = new_cleaned
            break

    # 4. Extract (prefix letter, digit, suffix letter)
    m = re.match(r"^([A-Za-z]?)(\d+)([A-Za-z]?)$", cleaned.strip())
    if m:
        prefix = (m.group(1) or "L").upper()
        digit = int(m.group(2))
        suffix = (m.group(3) or "").upper()
        return {"special": None, "prefix": prefix, "digit": digit, "suffix": suffix, "raw": raw}

    # 5. Couldn't reduce — return raw only; matching falls back to literal compare.
    return {"special": None, "prefix": None, "digit": None, "suffix": None, "raw": raw}


def signatures_match(user_sig, project_sig):
    """Two signatures match if BOTH are special and equal, or if all of
    (prefix, digit, suffix) are equal. Raw-only sigs match by string equality."""
    if user_sig["special"] and project_sig["special"]:
        return user_sig["special"] == project_sig["special"]
    if user_sig["digit"] is not None and project_sig["digit"] is not None:
        return (user_sig["prefix"] == project_sig["prefix"] and
                user_sig["digit"]  == project_sig["digit"]  and
                user_sig["suffix"] == project_sig["suffix"])
    if user_sig["raw"] and project_sig["raw"]:
        return user_sig["raw"].strip().lower() == project_sig["raw"].strip().lower()
    return False


def resolve_tokens_to_project_levels(tokens, project_levels):
    """
    Given a list of normalized tokens (e.g. ["L2", "L7T", "RF"]) and a list
    of actual project level names from Revit (e.g. ["+5.50 ระดับพื้นชั้น 2",
    "+45.45 LEVEL 7T", "+52.85 ROOF LEVEL"]), return a list of project level
    names that correspond to each token. Tokens with no match pass through
    unchanged (caller can fall back to existing behavior).

    Order is preserved. Duplicates in the input tokens are kept (caller is
    responsible for deduping if desired).
    """
    if not tokens:
        return []
    if not project_levels:
        return list(tokens)

    project_sigs = [(name, extract_level_signature(name)) for name in project_levels]

    resolved = []
    for tok in tokens:
        tok_sig = extract_level_signature(tok)
        matched_name = None
        for name, sig in project_sigs:
            if signatures_match(tok_sig, sig):
                matched_name = name
                break
        resolved.append(matched_name if matched_name else tok)
    return resolved
