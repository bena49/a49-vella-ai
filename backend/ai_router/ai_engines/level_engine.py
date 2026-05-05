# ======================================================================
# level_engine.py — Advanced A49 Flexible Level Parsing Engine
# ======================================================================

import re

# ----------------------------------------------------------------------
# NORMALIZATION DEFINITIONS
# ----------------------------------------------------------------------

DASHES = ["-", "–", "—", "−", "~"]

SPECIAL_LEVEL_MAP = {
    # English short codes / common phrases
    "g": "L1", "gf": "L1", "ground": "L1", "ground floor": "L1",
    "1st floor": "L1", "first floor": "L1",
    "roof": "RF", "roof level": "RF", "rf": "RF",
    "mz": "MZ", "mezzanine": "MZ",
    "p1": "P1", "p2": "P2", "parking 1": "P1", "parking 2": "P2",
    "pd": "PD", "podium": "PD",
    "at": "AT", "attic": "AT",
    "site": "SITE", "site plan": "SITE", "site level": "SITE",
    "top": "TOP", "top of building": "TOP", "top level": "TOP", "parapet": "TOP",
    # Thai aliases (A49 office naming)
    "ดาดฟ้า": "RF",
    "ระดับพื้นชั้นดาดฟ้า": "RF",
    "ชั้นดาดฟ้า": "RF",
    "ระดับสูงสุดของอาคาร": "TOP",
    "สูงสุดของอาคาร": "TOP",
    "ระดับสูงสุด": "TOP",
    "พื้นดิน": "SITE",
    "ระดับพื้นดิน": "SITE",
}

# ----------------------------------------------------------------------
# HELPER: RANGE EXPANDER (SIMPLIFIED & ROBUST)
# ----------------------------------------------------------------------
def expand_ranges_in_text(text):
    """
    Detects ranges like "L1-L4" and expands them into "L1, L2, L3, L4".
    """
    if not text: return text
    
    original_text = text

    def expand_match(match):
        # Group 1: Prefix (e.g. "L", "Level ")
        # Group 2: Start Num
        # Group 3: Optional 2nd Prefix (e.g. "L")
        # Group 4: End Num
        
        prefix = match.group(1).strip()
        start = int(match.group(2))
        end = int(match.group(4))
        
        # DEBUG LOG
        print(f"DEBUG: Found Range Match! Prefix='{prefix}', Start={start}, End={end}")
        
        # Safety check
        if start > end or (end - start) > 50: return match.group(0)
        
        # Generate List
        expanded_items = [f"{prefix}{i}" for i in range(start, end + 1)]
        return ", ".join(expanded_items)

    # 💥 SIMPLIFIED REGEX (Removed \b constraints to ensure match)
    # Structure: (Letters)(Digits) - (Optional Letters)(Digits)
    # example: L1-L4, Level 1 - 4, P1-P5
    
    pattern = r'([a-zA-Z]+\s*)(\d+)\s*-\s*(?:([a-zA-Z]+\s*))?(\d+)'
    
    expanded_text = re.sub(pattern, expand_match, text, flags=re.IGNORECASE)
    
    if expanded_text != original_text:
        print(f"DEBUG: Range Expansion Result: '{expanded_text}'")
        
    return expanded_text

# ----------------------------------------------------------------------
# MAIN ENTRY POINT
# ----------------------------------------------------------------------

def sort_levels_for_sheet_creation(level_tokens):
    """Reorder levels per the a49_dotted sheet-allocation spec.

    Final order (top → bottom):
      1. SITE                       — anchored to A1.00 by site_slots logic.
      2. Basements, deepest first    — B2 before B1 (digit DESCENDING).
      3. Numbered floors, lowest up  — L1 before L2 (digit ASCENDING).
                                       Mezzanine (M-suffix) sorts immediately
                                       after its parent floor so the build-
                                       sheets-payload mezz router can attach
                                       it as a sub-part.
      4. ROOF / TOP                  — always last among recognised levels.
      5. Other named specials (MZ, PD, AT, …) — after ROOF.
      6. Unrecognised tokens         — at the very end, original order kept.

    Works on both parsed tokens (`SITE`, `B1`, `L2M`, `TOP`) and fully-
    resolved Revit level names (`+11.00 ระดับสูงสุดของอาคาร`,
    `-3.00 ระดับชั้นใต้ดิน B1M`, …) because extract_level_signature
    normalises both shapes through the same code path.
    """
    from .level_matcher import extract_level_signature

    def _key(lvl_token):
        sig = extract_level_signature(lvl_token)
        special = sig.get("special")
        digit = sig.get("digit") if sig.get("digit") is not None else 0
        suffix_rank = 1 if (sig.get("suffix") or "").upper() == "M" else 0

        if special == "SITE":
            return (0, 0, 0)
        if (sig.get("prefix") or "").upper() == "B":
            return (1, -digit, suffix_rank)
        if special in ("RF", "TOP"):
            return (3, 0, 0)
        if sig.get("digit") is not None:
            return (2, digit, suffix_rank)
        if special:
            return (4, special, 0)
        return (5, str(lvl_token), 0)

    return sorted(level_tokens or [], key=_key)


def parse_levels(raw_text: str):
    """
    Convert raw level text into normalized A49 tokens.
    """
    if not raw_text or not isinstance(raw_text, str):
        return []

    # 1. Normalize
    cleaned = raw_text.lower().strip()
    
    # 2. Force Dash Normalization
    for d in DASHES: cleaned = cleaned.replace(d, "-")
    cleaned = cleaned.replace(" to ", "-")

    print(f"DEBUG: Parsing Levels Input -> '{cleaned}'") # 🔍 Debug Input

    # 3. RUN EXPANSION
    cleaned = expand_ranges_in_text(cleaned)

    # 4. STRATEGY 1: PHRASE EXTRACTION
    phrase_hits = []

    # 4a. THAI special-name detection (must come before digit extraction so
    # "ระดับพื้นชั้นดาดฟ้า" doesn't get parsed as the digit-less prefix only).
    thai_special_phrases = [
        ("ระดับพื้นชั้นดาดฟ้า", "RF"),
        ("ชั้นดาดฟ้า",          "RF"),
        ("ดาดฟ้า",              "RF"),
        ("ระดับสูงสุดของอาคาร", "TOP"),
        ("สูงสุดของอาคาร",      "TOP"),
        ("ระดับสูงสุด",         "TOP"),
        ("ระดับพื้นดิน",        "SITE"),
        ("พื้นดิน",             "SITE"),
    ]
    for phrase, token in thai_special_phrases:
        if phrase in cleaned:
            phrase_hits.append(token)

    # 4b. THAI numbered floors:
    #   ระดับพื้นชั้น 2, ผังพื้นชั้น 2, ชั้นที่ 2, ชั้น 2  (with optional intermediate
    #   "ที่" between phrase and digit, and optional intermediate suffix letter).
    for m in re.findall(
        r"(?:ระดับพื้นชั้น|ผังพื้นชั้น|ชั้นที่|ชั้น)\s*(?:ที่\s*)?([0-9]+)([A-Z]?)",
        cleaned, re.IGNORECASE
    ):
        num, suffix = m
        phrase_hits.append(f"L{int(num)}{suffix.upper()}")

    # 4c. THAI basement: "ระดับชั้นใต้ดิน B1M"
    for m in re.findall(r"ระดับชั้นใต้ดิน\s*([Bb])([0-9]+)([A-Z]?)", cleaned, re.IGNORECASE):
        _, num, suffix = m
        phrase_hits.append(f"B{int(num)}{suffix.upper()}")

    # English phrases: "level 1", "floor 2", "L1", "level 7T", "level 6M"
    # Suffix capture preserves intermediate floor markers (T=transfer,
    # M=mezzanine, etc.). Project-specific — we don't try to define them.
    for m in re.findall(r"\b(level|lvl|floor|fl|story|storey|l)\s*([0-9]+)([A-Za-z]?)\b", cleaned):
        _, num, suffix = m
        phrase_hits.append(f"L{int(num)}{suffix.upper()}")

    # Basement (with optional suffix: B1, B1M, B2)
    for m in re.findall(r"\b(basement|b)\s*([0-9]+)([A-Za-z]?)\b", cleaned):
        _, num, suffix = m
        phrase_hits.append(f"B{int(num)}{suffix.upper()}")

    # Parking
    for m in re.findall(r"\b(parking|p)\s*([0-9]+)([A-Za-z]?)\b", cleaned):
        _, num, suffix = m
        phrase_hits.append(f"P{int(num)}{suffix.upper()}")

    # Explicit Code Style: "level B1M"
    for m in re.findall(r"(level|lvl)\s*([bpml])([0-9]+)([A-Za-z]?)", cleaned):
        _, prefix, num, suffix = m
        phrase_hits.append(f"{prefix.upper()}{int(num)}{suffix.upper()}")

    # Site / Roof / Top (English) — standalone keyword detection.
    if re.search(r"\bsite\b", cleaned): phrase_hits.append("SITE")
    if re.search(r"\broof\b", cleaned): phrase_hits.append("RF")
    if re.search(r"\b(top of building|parapet)\b", cleaned): phrase_hits.append("TOP")

    # 5. RETURN IF FOUND
    if phrase_hits:
        # Deduplicate & Sort
        seen = set()
        unique = []
        for x in phrase_hits:
            if x not in seen:
                unique.append(x)
                seen.add(x)
        
        print(f"DEBUG: Final Levels Found -> {unique}") # 🔍 Debug Output
        return unique

    # 6. STRATEGY 2: CHUNK PARSING (Fallback)
    chunks = re.split(r"[,\s]+and\s+|,|\s+and\s+", cleaned)
    normalized_tokens = []
    
    for chunk in chunks:
        chunk = chunk.strip()
        if not chunk: continue
        
        if "-" in chunk:
            expanded = expand_range(chunk)
            normalized_tokens.extend(expanded)
            continue
            
        token = normalize_single_level_token(chunk)
        if token:
            normalized_tokens.append(token)

    # Deduplicate
    final = []
    for t in normalized_tokens:
        if t not in final: final.append(t)
        
    return final

# ----------------------------------------------------------------------
# HELPERS
# ----------------------------------------------------------------------

def expand_range(text: str):
    # (Same as before, simplified for brevity)
    parts = text.split("-")
    if len(parts) != 2: return []
    start, end = parts[0].strip(), parts[1].strip()
    
    start_norm = normalize_single_level_token(start)
    end_norm = normalize_single_level_token(end)
    
    if not start_norm or not end_norm: return []
    
    prefix_s, num_s = extract_prefix_num(start_norm)
    prefix_e, num_e = extract_prefix_num(end_norm)
    
    if prefix_s == "SITE" or prefix_e == "SITE": return [start_norm, end_norm]
    if prefix_s != prefix_e: return [start_norm, end_norm]
    
    try:
        n1, n2 = int(num_s), int(num_e)
    except: return [start_norm, end_norm]
    
    if n2 < n1: n1, n2 = n2, n1
    return [f"{prefix_s}{n}" for n in range(n1, n2 + 1)]

def normalize_single_level_token(token: str):
    t = token.lower().strip()
    # Strip elevation prefix like "+45.45 " or "-2.42 "
    t = re.sub(r"^[+-]?\d+\.\d+\s+", "", t)
    # Strip language prefix
    t = re.sub(r"^(level|lvl|floor|fl|storey|story)\s+", "", t)
    t = re.sub(r"^(ระดับพื้นชั้น|ผังพื้นชั้น|ระดับชั้นใต้ดิน|ชั้นที่|ชั้น)\s*(?:ที่\s*)?", "", t)

    # Special-name lookup (handles English + Thai)
    if t in SPECIAL_LEVEL_MAP: return SPECIAL_LEVEL_MAP[t]
    # Thai special-name substring (in case stripping consumed too aggressively)
    for phrase, tok in SPECIAL_LEVEL_MAP.items():
        if len(phrase) >= 5 and phrase in t:
            return tok

    if re.fullmatch(r"\d+", t): return f"L{int(t)}"

    # Capture optional intermediate suffix letter (T, M, etc.)
    m = re.fullmatch(r"([a-z]+)?(\d+)([a-z]?)", t)
    if m:
        prefix = (m.group(1) or "").upper()
        num = m.group(2)
        suffix = (m.group(3) or "").upper()
        if prefix == "": return f"L{int(num)}{suffix}"
        if prefix in ["L", "B", "P", "UL", "PD", "MZ", "AT"]: return f"{prefix}{int(num)}{suffix}"
        return None
    return None

def extract_prefix_num(tok: str):
    if tok == "SITE": return ("SITE", 0)
    m = re.fullmatch(r"([A-Z]+)(\d+)", tok)
    if not m: return ("L", 1)
    return (m.group(1), m.group(2))