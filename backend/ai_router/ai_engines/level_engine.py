# ======================================================================
# level_engine.py — Advanced A49 Flexible Level Parsing Engine
# ======================================================================

import re

# ----------------------------------------------------------------------
# NORMALIZATION DEFINITIONS
# ----------------------------------------------------------------------

DASHES = ["-", "–", "—", "−", "~"]

SPECIAL_LEVEL_MAP = {
    "g": "L1", "gf": "L1", "ground": "L1", "ground floor": "L1",
    "1st floor": "L1", "first floor": "L1",
    "roof": "RF", "roof level": "RF", "rf": "RF",
    "mz": "MZ", "mezzanine": "MZ",
    "p1": "P1", "p2": "P2", "parking 1": "P1", "parking 2": "P2",
    "pd": "PD", "podium": "PD",
    "at": "AT", "attic": "AT",
    "site": "SITE", "site plan": "SITE", "site level": "SITE"
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

    # English phrases: "level 1", "floor 2", "L1"
    # Added strict \b to ensure we don't match half-words, but relaxed for "l1"
    for m in re.findall(r"\b(level|lvl|floor|fl|story|storey|l)\s*([0-9]+)\b", cleaned):
        _, num = m
        phrase_hits.append(f"L{int(num)}")

    # Basement
    for m in re.findall(r"\b(basement|b)\s*([0-9]+)\b", cleaned):
        _, num = m
        phrase_hits.append(f"B{int(num)}")

    # Parking
    for m in re.findall(r"\b(parking|p)\s*([0-9]+)\b", cleaned):
        _, num = m
        phrase_hits.append(f"P{int(num)}")
        
    # Explicit Code Style
    for m in re.findall(r"(level|lvl)\s*([bpml])([0-9]+)", cleaned):
        _, prefix, num = m
        phrase_hits.append(f"{prefix.upper()}{int(num)}")

    # Site
    if "site" in cleaned:
        phrase_hits.append("SITE")

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
    t = re.sub(r"^(level|lvl|floor|fl|storey|story)\s+", "", t)
    
    if t in SPECIAL_LEVEL_MAP: return SPECIAL_LEVEL_MAP[t]
    if re.fullmatch(r"\d+", t): return f"L{int(t)}"
    
    m = re.fullmatch(r"([a-z]+)?(\d+)", t)
    if m:
        prefix = (m.group(1) or "").upper()
        num = m.group(2)
        if prefix == "": return f"L{int(num)}"
        if prefix in ["L", "B", "P", "UL", "PD", "MZ", "AT"]: return f"{prefix}{int(num)}"
        return None 
    return None

def extract_prefix_num(tok: str):
    if tok == "SITE": return ("SITE", 0)
    m = re.fullmatch(r"([A-Z]+)(\d+)", tok)
    if not m: return ("L", 1)
    return (m.group(1), m.group(2))