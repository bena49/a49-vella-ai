# ============================================================================
# bilingual_dictionary.py — A49 architectural drawing terminology, EN ↔ TH
# ----------------------------------------------------------------------------
# Source-of-truth dictionary for translating sheet names between English and
# Thai. Used by the Renumber/Rename wizard (Phase 2) and any future bilingual
# sheet-name features.
#
# DESIGN
# ──────
# Two complementary mechanisms:
#
#   1. SHEET_NAME_TEMPLATES — whole-phrase patterns with {n}, {x} placeholders
#      that handle Thai grammar reordering (e.g. "LEVEL 1 FLOOR PLAN" needs to
#      become "ผังพื้นชั้นที่ 1", not "ชั้น 1 ผังพื้น"). Try templates FIRST.
#
#   2. TERM groups (DRAWING_TYPES, ROOM_TYPES, ELEMENT_TYPES, …) — substring
#      pairs for everything templates don't cover. Longest-match-first so
#      "FLOOR PLAN" wins over "FLOOR" alone.
#
# PROTECTED_WORDS are never translated — drawing-set codes (A0-A9, X0),
# view-type codes (FL, CP, EL, …), level codes (B1, B2, B1M), and
# identifier patterns (WS01, ST-01, AW01-05).
#
# HOW TO ADD A NEW TRANSLATION
# ────────────────────────────
#   1. Find the right group below (DRAWING_TYPES, LEVEL_TERMS, ROOM_TYPES,
#      ELEMENT_TYPES, GENERAL_TERMS) or add to SHEET_NAME_TEMPLATES if it
#      involves grammar reordering.
#   2. Add a tuple ("ENGLISH UPPERCASE", "ไทย"). English side is matched
#      case-insensitively; the Thai side is used verbatim.
#   3. Within each group the order doesn't matter — _compile() auto-sorts
#      by length (longest first) at module load.
#   4. Run `python -m ai_router.ai_engines.test_bilingual_dictionary` to
#      verify nothing regressed.
#
# CONTRIBUTORS: this file is meant to be edited by anyone, not just devs.
# Add as many translations as you find useful — they're just data.
# ============================================================================

import re

# ── PROTECTED words — never translated ──────────────────────────────────────
# Drawing-set codes, view-type codes, level codes, and identifier patterns
# the user will recognise as-is in any language. The translator skips over
# any token that matches these patterns or appears in this set.
PROTECTED_WORDS = {
    # Sheet category codes
    "A0", "A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8", "A9",
    "X0", "X1", "X2", "X3", "X4", "X5", "X6", "X7", "X8", "X9",
    # View type abbreviations (from naming_engine.VIEW_ABBREVIATIONS)
    "FL", "CP", "SE", "EL", "WS", "AP", "PT", "ST", "AD", "AW", "D1", "SC",
    # Stage codes
    "WV", "PD", "DD", "CD",
    # Common project-specific codes you'll see in the wild
    "EIA", "NFA", "GFA", "RCP", "TB",
}

# Identifier-shaped patterns we never touch (regex).
# Examples: WS01, ST-01, AW01-05, R-01,02, B1M, 5T, L7T, 1010, A1.03
PROTECTED_PATTERNS = [
    re.compile(r"^[A-Z]{1,3}\d{1,3}([-,]\d{1,3})*$"),     # WS01, AW01-05, R-01,02
    re.compile(r"^[A-Z]{1,3}-\d{1,3}([.\d]+)?$"),         # ST-01, R-01.1
    re.compile(r"^[BLPM]\d{1,2}[A-Z]?$"),                 # B1, B1M, L7T, P1
    re.compile(r"^\d+$"),                                  # any pure number
    re.compile(r"^[AX]\d\.\d{1,3}(\.\d{1,3})?$"),         # A1.03, A1.03.1
    re.compile(r"^\d{4,5}$"),                              # 1010, 10100
    re.compile(r"^X\d{3,4}$"),                             # X010, X0100
]


def _is_protected(token):
    """True if `token` is an identifier that should never be translated."""
    if not token:
        return False
    upper = token.upper().strip("(),.")
    if upper in PROTECTED_WORDS:
        return True
    return any(p.match(upper) for p in PROTECTED_PATTERNS)


# ── SHEET_NAME_TEMPLATES — whole-phrase patterns with placeholders ──────────
# Try these BEFORE the term-substitution path. Placeholders:
#   {n}  → digits captured as a string
#   {x}  → any letter or short alphanumeric (e.g. section letter "A", "B")
#
# These handle Thai grammar reordering — Thai puts the level/qualifier AFTER
# the drawing type ("ผังพื้นชั้นที่ 1" = "FLOOR PLAN, LEVEL 1").
SHEET_NAME_TEMPLATES = [
    # ── Cover / index ─────────────────────────────────────────────────
    ("COVER",                         "ปก"),
    ("DRAWING INDEX",                 "สารบัญแบบ"),
    ("ARCHITECTURAL DRAWING INDEX",   "สารบัญแบบสถาปัตยกรรม"),

    # ── A0 General Information ───────────────────────────────────────
    ("SITE AND VICINITY PLAN",        "แผนที่สังเขป"),
    ("STANDARD SYMBOLS",              "สัญลักษณ์มาตรฐาน"),
    ("SAFETY PLAN",                   "มาตรการป้องกันความปลอดภัย"),
    ("WALL TYPES",                    "ประเภทผนัง"),
    ("MATERIAL SCHEDULE",             "ตารางรายการวัสดุประกอบแบบ"),
    ("LAND TITLE DEED",               "โฉนดที่ดิน"),
    ("CONSTRUCTION DRAWINGS",         "แบบก่อสร้าง"),

    # ── A1 Floor plans (level-aware patterns) ────────────────────────
    ("LEVEL ROOF FLOOR PLAN",         "ผังพื้นชั้นดาดฟ้า"),
    ("LEVEL ROOF PLAN",               "ผังพื้นดาดฟ้า"),
    ("ROOF PLAN",                     "ผังหลังคา"),
    ("OVERALL SITE PLAN",             "ผังบริเวณรวม"),
    ("SITE PLAN",                     "ผังบริเวณ"),
    ("LEVEL B{n} FLOOR PLAN",         "ผังพื้นชั้น B{n}"),
    ("LEVEL B{n}M FLOOR PLAN",        "ผังพื้นชั้น B{n}M"),
    ("LEVEL {n}M FLOOR PLAN",         "ผังพื้นชั้นลอย {n}"),
    ("LEVEL {n}T FLOOR PLAN",         "ผังพื้นชั้นถ่ายแรง {n}"),
    ("LEVEL {n} FLOOR PLAN",          "ผังพื้นชั้นที่ {n}"),
    ("FLOOR PLAN",                    "ผังพื้น"),

    # ── A5 Ceiling plans ─────────────────────────────────────────────
    ("LEVEL ROOF CEILING PLAN",       "ผังฝ้าเพดานดาดฟ้า"),
    ("LEVEL B{n} CEILING PLAN",       "ผังฝ้าเพดานชั้น B{n}"),
    ("LEVEL B{n}M CEILING PLAN",      "ผังฝ้าเพดานชั้น B{n}M"),
    ("LEVEL {n}M CEILING PLAN",       "ผังฝ้าเพดานชั้นลอย {n}"),
    ("LEVEL {n}T CEILING PLAN",       "ผังฝ้าเพดานชั้นถ่ายแรง {n}"),
    ("LEVEL {n} CEILING PLAN",        "ผังฝ้าเพดานชั้นที่ {n}"),
    ("CEILING PLAN",                  "ผังฝ้าเพดาน"),

    # ── A2 Elevations / A3 Building Sections / A4 Wall Sections ──────
    ("ELEVATION {x}",                 "รูปด้าน {x}"),
    ("ELEVATIONS",                    "รูปด้าน"),
    ("ELEVATION",                     "รูปด้าน"),
    ("INTERIOR ELEVATION",            "รูปด้านภายใน"),
    ("BUILDING SECTION {x}",          "รูปตัด {x}"),
    ("BUILDING SECTIONS",             "รูปตัดอาคาร"),
    ("BUILDING SECTION",              "รูปตัดอาคาร"),
    ("WALL SECTION {x}",              "รูปตัดผนัง {x}"),
    ("WALL SECTIONS",                 "รูปตัดผนัง"),
    ("WALL SECTION",                  "รูปตัดผนัง"),
    ("SECTION {x}",                   "รูปตัด {x}"),

    # ── A6 Enlarged plans / interior elevations ──────────────────────
    ("FLOOR PATTERN PLAN",            "ผังพื้น PATTERN"),
    ("ENLARGED TOILET PLAN",          "แบบขยายห้องน้ำ"),
    ("ENLARGED PLAN",                 "แบบขยาย"),
    ("CANOPY PLAN",                   "หลังคาคลุม"),

    # ── A7 Vertical circulation ──────────────────────────────────────
    ("ENLARGED STAIR PLAN",           "แบบขยายบันได"),
    ("ENLARGED STAIR SECTION",        "รูปตัดบันได"),
    ("ENLARGED RAMP PLAN",            "แบบขยายทางลาด"),
    ("ENLARGED LIFT PLAN",            "แบบขยายลิฟต์"),
    ("LIFT SECTION",                  "รูปตัดลิฟต์"),
    ("ELEVATOR SECTION",              "รูปตัดลิฟต์"),
    ("MAINTENANCE STAIR",             "บันไดซ่อมบำรุง"),
    ("WATER TANK ACCESS STAIR",       "บันไดลงถังเก็บน้ำ"),

    # ── A8 Door/Window schedules ─────────────────────────────────────
    ("DOOR SCHEDULE",                 "ตารางประตู"),
    ("WINDOW SCHEDULE",               "ตารางหน้าต่าง"),

    # ── A9 Details (general) ─────────────────────────────────────────
    ("GENERAL DETAILS",               "แบบขยายทั่วไป"),
    ("GENERAL DETAIL",                "แบบขยายทั่วไป"),
    ("DETAILS",                       "แบบขยาย"),
    ("DETAIL",                        "แบบขยาย"),
    ("MATERIAL JOINT DETAIL",         "แบบขยายรอยต่อวัสดุ"),
    ("COLUMN DETAIL",                 "แบบขยายเสา"),
    ("HANDRAIL DETAIL",               "แบบขยายราวกันตก"),
    ("GUARDRAIL DETAIL",              "แบบขยายราวกันตก"),
    ("TRAFFIC ARROW DETAIL",          "แบบขยายลูกศรทางเดินรถ"),
    ("WASTE ROOM DETAIL",             "แบบขยายห้องพักขยะ"),
    ("GUARD HOUSE DETAIL",            "แบบขยายป้อมยาม"),
    ("GAS TANK ROOM DETAIL",          "แบบขยายห้องเก็บถังแก๊ส"),
    ("SANITARYWARE INSTALLATION STANDARD", "มาตรฐานการติดตั้งสุขภัณฑ์"),

    # ── Sub-part suffix (a49_dotted A1.03.1) ──────────────────────────
    # Used in combination with the parent template by the wizard.
    ("(PART {n})",                    "( ส่วนที่ {n} )"),
]


# ── TERM groups — substring fallback for everything templates don't cover ──
# Within each group the order doesn't matter; _compile() sorts longest-first
# so multi-word terms win over single-word substrings.

DRAWING_TYPES = [
    ("FLOOR PLAN",                    "ผังพื้น"),
    ("CEILING PLAN",                  "ผังฝ้าเพดาน"),
    ("SITE PLAN",                     "ผังบริเวณ"),
    ("ROOF PLAN",                     "ผังหลังคา"),
    ("ELEVATION",                     "รูปด้าน"),
    ("BUILDING SECTION",              "รูปตัดอาคาร"),
    ("WALL SECTION",                  "รูปตัดผนัง"),
    ("SECTION",                       "รูปตัด"),
    ("PLAN",                          "ผัง"),
    ("DETAIL",                        "แบบขยาย"),
    ("ENLARGED",                      "แบบขยาย"),
    ("SCHEDULE",                      "ตาราง"),
]

LEVEL_TERMS = [
    ("LEVEL ROOF",                    "ดาดฟ้า"),
    ("BASEMENT",                      "ชั้นใต้ดิน"),
    ("MEZZANINE",                     "ชั้นลอย"),
    ("TRANSFER",                      "ชั้นถ่ายแรง"),
    ("ROOFTOP",                       "ดาดฟ้า"),
    ("LEVEL",                         "ชั้น"),
    ("FLOOR",                         "ชั้น"),
    ("ROOF",                          "หลังคา"),
    ("PART",                          "ส่วนที่"),
]

ROOM_TYPES = [
    ("TOILET",                        "ห้องน้ำ"),
    ("RESTROOM",                      "ห้องน้ำ"),
    ("GUARD HOUSE",                   "ป้อมยาม"),
    ("WASTE ROOM",                    "ห้องพักขยะ"),
    ("GARBAGE ROOM",                  "ห้องพักขยะ"),
    ("GAS TANK ROOM",                 "ห้องเก็บถังแก๊ส"),
    ("WATER TANK",                    "ถังเก็บน้ำ"),
    ("PARKING",                       "ที่จอดรถ"),
]

ELEMENT_TYPES = [
    ("HANDRAIL",                      "ราวกันตก"),
    ("GUARDRAIL",                     "ราวกันตก"),
    ("STAIR",                         "บันได"),
    ("RAMP",                          "ทางลาด"),
    ("LIFT",                          "ลิฟต์"),
    ("ELEVATOR",                      "ลิฟต์"),
    ("COLUMN",                        "เสา"),
    ("DOOR",                          "ประตู"),
    ("WINDOW",                        "หน้าต่าง"),
    ("CANOPY",                        "หลังคาคลุม"),
    ("COVERED ROOF",                  "หลังคาคลุม"),
    ("ARROW",                         "ลูกศร"),
    ("ROADWAY",                       "ทางเดินรถ"),
    ("DRIVEWAY",                      "ทางเดินรถ"),
    ("MATERIAL",                      "วัสดุ"),
    ("JOINT",                         "รอยต่อ"),
    ("MAINTENANCE",                   "ซ่อมบำรุง"),
    ("STANDARD",                      "มาตรฐาน"),
    ("SANITARYWARE",                  "สุขภัณฑ์"),
    ("WALL",                          "ผนัง"),
]

GENERAL_TERMS = [
    ("ARCHITECTURAL",                 "สถาปัตยกรรม"),
    ("ARCHITECTURE",                  "สถาปัตยกรรม"),
    ("CONSTRUCTION",                  "ก่อสร้าง"),
    ("INTERIOR",                      "ภายใน"),
    ("EXTERIOR",                      "ภายนอก"),
    ("OVERALL",                       "รวม"),
    ("GENERAL",                       "ทั่วไป"),
    ("INSTALLATION",                  "การติดตั้ง"),
    ("AND",                           "และ"),
    ("OR",                            "หรือ"),
]


# ── Internal: compile groups into longest-first lists ───────────────────────

def _compile():
    """Flatten all term groups into two oriented lists:
      - _EN_TERMS: list of (EN_source, TH_target) pairs, sorted by EN length
      - _TH_TERMS: list of (TH_source, EN_target) pairs, sorted by TH length

    Both directions need the SOURCE side as the first tuple element so that
    `_term_replace(text, terms)` can iterate `for src, dst in terms`. Sorting
    longest-first ensures multi-word phrases match before single-word
    constituents (e.g. "FLOOR PLAN" wins over a stray "FLOOR")."""
    all_pairs = (
        DRAWING_TYPES + LEVEL_TERMS + ROOM_TYPES + ELEMENT_TYPES + GENERAL_TERMS
    )
    # EN → TH: source is English (left side of the tuple)
    en_to_th = sorted(set(all_pairs), key=lambda p: -len(p[0]))
    # TH → EN: flip the tuple so source is Thai (left side after flip)
    th_to_en = sorted({(th, en) for en, th in all_pairs},
                      key=lambda p: -len(p[0]))
    return en_to_th, th_to_en


_EN_TERMS, _TH_TERMS = _compile()


# ── Template engine ─────────────────────────────────────────────────────────

# Compile templates once at module load. Each template becomes (regex, output_template).
def _compile_templates():
    """Convert SHEET_NAME_TEMPLATES into (compiled_regex, target_template) pairs.
    Placeholders {n} → (\d+), {x} → ([A-Za-z0-9]+).
    Sorted longest-EN-first so 'LEVEL B{n} FLOOR PLAN' matches before
    'LEVEL {n} FLOOR PLAN' when both could fit (e.g. 'LEVEL B1 FLOOR PLAN').
    """
    PLACEHOLDER_RE = re.compile(r"\{(\w+)\}")

    en_to_th = []
    th_to_en = []
    for en_pat, th_pat in SHEET_NAME_TEMPLATES:
        # Build a regex from the EN pattern (case-insensitive, anchored).
        en_regex = re.escape(en_pat)
        # Re-replace escaped placeholders with capture groups.
        en_regex = re.sub(r"\\\{n\\\}", r"(?P<n>\\d+)", en_regex)
        en_regex = re.sub(r"\\\{x\\\}", r"(?P<x>[A-Za-z0-9]+)", en_regex)
        en_to_th.append((re.compile(rf"^{en_regex}$", re.IGNORECASE), th_pat))

        # Same in the other direction.
        th_regex = re.escape(th_pat)
        th_regex = re.sub(r"\\\{n\\\}", r"(?P<n>\\d+)", th_regex)
        th_regex = re.sub(r"\\\{x\\\}", r"(?P<x>[A-Za-z0-9]+)", th_regex)
        th_to_en.append((re.compile(rf"^{th_regex}$"), en_pat))

    # Longest-pattern-first so specific patterns win over generic ones.
    en_to_th.sort(key=lambda p: -len(p[0].pattern))
    th_to_en.sort(key=lambda p: -len(p[0].pattern))
    return en_to_th, th_to_en


_TEMPLATES_EN_TH, _TEMPLATES_TH_EN = _compile_templates()


def _try_template(text, templates):
    """If `text` matches any template, render the corresponding output.
    Returns the rendered string or None on no match."""
    for compiled, target in templates:
        m = compiled.match(text.strip())
        if m:
            return target.format(**m.groupdict())
    return None


# ── Public translation API ──────────────────────────────────────────────────

# Token regex: split on whitespace + commas while preserving punctuation.
# Strings like "LEVEL 7,ดาดฟ้า" tokenise as ["LEVEL", "7", ",", "ดาดฟ้า"].
_TOKEN_RE = re.compile(r"[A-Za-z฀-๿]+|\d+|[^\w\s]|\s+", re.UNICODE)


def _term_replace(text, terms):
    """Apply substring replacement using `terms` (longest-first).
    Skips PROTECTED tokens. Case-insensitive on the source side.
    """
    if not text:
        return text
    tokens = _TOKEN_RE.findall(text)
    out_parts = []
    i = 0
    while i < len(tokens):
        # Try to greedily match the longest term starting at position i.
        replaced = False
        for src, dst in terms:
            # Match against the joined token sequence starting at i.
            matched_len = _match_phrase(tokens, i, src)
            if matched_len > 0:
                # Skip if any captured token is protected.
                captured = tokens[i:i + matched_len]
                if not any(_is_protected(t) for t in captured if t.strip()):
                    out_parts.append(dst)
                    i += matched_len
                    replaced = True
                    break
        if not replaced:
            out_parts.append(tokens[i])
            i += 1
    return "".join(out_parts)


def _match_phrase(tokens, start, src_phrase):
    """Return how many tokens (from `start`) form `src_phrase`, ignoring case
    and treating internal whitespace as flexible. Returns 0 if no match."""
    src_tokens = _TOKEN_RE.findall(src_phrase)
    # Trim trailing whitespace tokens from the source pattern.
    while src_tokens and src_tokens[-1].isspace():
        src_tokens.pop()

    j = 0  # index into src_tokens
    k = 0  # tokens consumed from `tokens`
    while j < len(src_tokens):
        if start + k >= len(tokens):
            return 0
        s_tok = src_tokens[j]
        if s_tok.isspace():
            # Allow any amount of whitespace in the haystack.
            if not tokens[start + k].isspace():
                return 0
            k += 1
            j += 1
            continue
        if tokens[start + k].isspace():
            # Skip whitespace in haystack only if source isn't expecting it.
            k += 1
            continue
        if tokens[start + k].upper() != s_tok.upper():
            return 0
        k += 1
        j += 1
    return k


def translate_en_to_th(text):
    """Translate an English drawing-name string to Thai.

    Tries SHEET_NAME_TEMPLATES first (whole-phrase patterns with grammar
    reordering). Falls back to longest-match substring replacement using
    the term groups for anything templates don't cover. PROTECTED words
    (drawing codes, identifiers) pass through untranslated.

    Returns the translated string. Untranslatable tokens stay in English.
    """
    if not text:
        return text
    text = text.strip()
    # Template path — handles grammar-correct sheet names.
    rendered = _try_template(text, _TEMPLATES_EN_TH)
    if rendered is not None:
        return rendered
    # Fallback path — substring replacement for everything else.
    return _term_replace(text, _EN_TERMS)


def translate_th_to_en(text):
    """Translate a Thai drawing-name string to English. Same dispatch order
    as translate_en_to_th: templates first, then substring fallback."""
    if not text:
        return text
    text = text.strip()
    rendered = _try_template(text, _TEMPLATES_TH_EN)
    if rendered is not None:
        return rendered
    return _term_replace(text, _TH_TERMS)


def all_terms_en():
    """Return the full sorted (longest-first) EN→TH term list. For inspection
    in tests / dictionary editor UI."""
    return list(_EN_TERMS)


def all_terms_th():
    return list(_TH_TERMS)


def all_templates():
    """Return SHEET_NAME_TEMPLATES as-is. For inspection."""
    return list(SHEET_NAME_TEMPLATES)
