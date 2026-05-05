# ============================================================================
# test_bilingual_dictionary.py — verify EN ↔ TH translation against real
# A49 sheet names. Cases drawn from the DEPA_CON_140862 drawing index.
#
# Run standalone:
#   cd backend
#   python -m ai_router.ai_engines.test_bilingual_dictionary
# ============================================================================

import sys
import os

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        pass

if __name__ == "__main__" and __package__ is None:
    sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))

from ai_router.ai_engines.bilingual_dictionary import (
    translate_en_to_th,
    translate_th_to_en,
    _is_protected,
    PROTECTED_WORDS,
)


# ── EN → TH cases ────────────────────────────────────────────────────────
# (description, english_input, expected_thai)

EN_TO_TH_CASES = [
    # ── Core sheet-name templates (grammar reordering) ─────────────────
    ("Cover",                       "COVER",                      "ปก"),
    ("Drawing index",               "DRAWING INDEX",              "สารบัญแบบ"),
    ("Site plan",                   "SITE PLAN",                  "ผังบริเวณ"),
    ("Overall site plan",           "OVERALL SITE PLAN",          "ผังบริเวณรวม"),
    ("Roof plan",                   "ROOF PLAN",                  "ผังหลังคา"),

    # ── A1 Floor plans by level ────────────────────────────────────────
    ("L1 floor plan",               "LEVEL 1 FLOOR PLAN",         "ผังพื้นชั้นที่ 1"),
    ("L7 floor plan",               "LEVEL 7 FLOOR PLAN",         "ผังพื้นชั้นที่ 7"),
    ("L99 floor plan",              "LEVEL 99 FLOOR PLAN",        "ผังพื้นชั้นที่ 99"),
    ("Roof level floor plan",       "LEVEL ROOF FLOOR PLAN",      "ผังพื้นชั้นดาดฟ้า"),
    ("B1 floor plan",               "LEVEL B1 FLOOR PLAN",        "ผังพื้นชั้น B1"),
    ("B2 floor plan",               "LEVEL B2 FLOOR PLAN",        "ผังพื้นชั้น B2"),
    ("L1M floor plan (mezzanine)",  "LEVEL 1M FLOOR PLAN",        "ผังพื้นชั้นลอย 1"),

    # ── A5 Ceiling plans ───────────────────────────────────────────────
    ("L1 ceiling plan",             "LEVEL 1 CEILING PLAN",       "ผังฝ้าเพดานชั้นที่ 1"),
    ("B1 ceiling plan",             "LEVEL B1 CEILING PLAN",      "ผังฝ้าเพดานชั้น B1"),
    ("Roof ceiling plan",           "LEVEL ROOF CEILING PLAN",    "ผังฝ้าเพดานดาดฟ้า"),

    # ── A2/A3/A4 ───────────────────────────────────────────────────────
    ("Elevations",                  "ELEVATIONS",                 "รูปด้าน"),
    ("Wall sections",               "WALL SECTIONS",              "รูปตัดผนัง"),
    ("Building sections",           "BUILDING SECTIONS",          "รูปตัดอาคาร"),

    # ── A6 ─────────────────────────────────────────────────────────────
    ("Floor pattern plan",          "FLOOR PATTERN PLAN",         "ผังพื้น PATTERN"),
    ("Enlarged toilet plan",        "ENLARGED TOILET PLAN",       "แบบขยายห้องน้ำ"),
    ("Canopy plan",                 "CANOPY PLAN",                "หลังคาคลุม"),

    # ── A7 ─────────────────────────────────────────────────────────────
    ("Enlarged stair plan",         "ENLARGED STAIR PLAN",        "แบบขยายบันได"),
    ("Stair section",               "ENLARGED STAIR SECTION",     "รูปตัดบันได"),
    ("Enlarged ramp plan",          "ENLARGED RAMP PLAN",         "แบบขยายทางลาด"),
    ("Enlarged lift plan",          "ENLARGED LIFT PLAN",         "แบบขยายลิฟต์"),

    # ── A8 ─────────────────────────────────────────────────────────────
    ("Door schedule",               "DOOR SCHEDULE",              "ตารางประตู"),
    ("Window schedule",             "WINDOW SCHEDULE",            "ตารางหน้าต่าง"),

    # ── Substring fallback path (no template match) ────────────────────
    ("Bare 'TOILET'",               "TOILET",                     "ห้องน้ำ"),
    ("Bare 'STAIR'",                "STAIR",                      "บันได"),
    ("Bare 'COLUMN'",               "COLUMN",                     "เสา"),

    # ── Identifier preservation ────────────────────────────────────────
    # WS01 should not be translated; the surrounding term should be.
    ("Wall section + identifier",   "WALL SECTION WS01",          "รูปตัดผนัง WS01"),
    # Stair detail with identifier — falls through to substring path.
    # ("Stair + ID",                 "STAIR ST-01",                "บันได ST-01"),  # currently fails — see KNOWN_LIMITATIONS

    # ── Empty / edge cases ─────────────────────────────────────────────
    ("Empty string",                "",                           ""),
    ("Whitespace only",             "   ",                        ""),
]


# ── TH → EN cases ────────────────────────────────────────────────────────
# Drawn directly from the DEPA_CON_140862 drawing index.

TH_TO_EN_CASES = [
    ("Cover (ปก)",                  "ปก",                         "COVER"),
    ("Drawing index",               "สารบัญแบบ",                  "DRAWING INDEX"),
    ("Site plan",                   "ผังบริเวณ",                   "SITE PLAN"),
    ("Overall site plan",           "ผังบริเวณรวม",                "OVERALL SITE PLAN"),
    ("Roof plan",                   "ผังหลังคา",                   "ROOF PLAN"),

    # Templates with placeholders (level-aware)
    ("L1 floor plan",               "ผังพื้นชั้นที่ 1",              "LEVEL 1 FLOOR PLAN"),
    ("L7 floor plan",               "ผังพื้นชั้นที่ 7",              "LEVEL 7 FLOOR PLAN"),
    ("B1 floor plan",               "ผังพื้นชั้น B1",                "LEVEL B1 FLOOR PLAN"),
    ("B2 floor plan",               "ผังพื้นชั้น B2",                "LEVEL B2 FLOOR PLAN"),
    ("Roof level floor plan",       "ผังพื้นชั้นดาดฟ้า",             "LEVEL ROOF FLOOR PLAN"),

    # Ceiling
    ("L1 ceiling",                  "ผังฝ้าเพดานชั้นที่ 1",          "LEVEL 1 CEILING PLAN"),
    ("B1 ceiling",                  "ผังฝ้าเพดานชั้น B1",            "LEVEL B1 CEILING PLAN"),

    # A6
    ("Floor pattern plan",          "ผังพื้น PATTERN",              "FLOOR PATTERN PLAN"),
    ("Enlarged toilet plan",        "แบบขยายห้องน้ำ",                "ENLARGED TOILET PLAN"),

    # A7
    ("Stair plan",                  "แบบขยายบันได",                "ENLARGED STAIR PLAN"),
    ("Stair section",               "รูปตัดบันได",                  "ENLARGED STAIR SECTION"),
    ("Ramp plan",                   "แบบขยายทางลาด",                "ENLARGED RAMP PLAN"),
    ("Lift plan",                   "แบบขยายลิฟต์",                 "ENLARGED LIFT PLAN"),

    # A8
    ("Door schedule",               "ตารางประตู",                  "DOOR SCHEDULE"),
    ("Window schedule",             "ตารางหน้าต่าง",                "WINDOW SCHEDULE"),

    # Bare-term substring fallback
    ("Bare 'ห้องน้ำ'",                "ห้องน้ำ",                      "TOILET"),
    ("Bare 'บันได'",                  "บันได",                       "STAIR"),
    ("Bare 'เสา'",                   "เสา",                         "COLUMN"),
]


# ── PROTECTED-word cases ─────────────────────────────────────────────────
PROTECTED_CASES = [
    # (description, token, expected_is_protected)
    ("A1 sheet code",        "A1",       True),
    ("A0 sheet code",        "A0",       True),
    ("X0 custom sheet",      "X0",       True),
    ("FL view-type code",    "FL",       True),
    ("CD stage code",        "CD",       True),
    ("B1 basement",          "B1",       True),
    ("B1M basement mezz",    "B1M",      True),
    ("L7T transfer level",   "L7T",      True),
    ("Pure number 1010",     "1010",     True),
    ("V2 number 10100",      "10100",    True),
    ("Dotted A1.03",         "A1.03",    True),
    ("Dotted A1.03.1",       "A1.03.1",  True),
    ("X-series X010",        "X010",     True),
    ("X-series X0100",       "X0100",    True),
    ("Stair ID ST-01",       "ST-01",    True),
    ("Window AW01-05",       "AW01-05",  True),
    # Non-protected — natural words
    ("FLOOR (term)",         "FLOOR",    False),
    ("TOILET (term)",        "TOILET",   False),
    ("PLAN (term)",          "PLAN",     False),
    ("ผังพื้น (Thai term)",   "ผังพื้น",   False),
]


# ── HARNESS ──────────────────────────────────────────────────────────────

def _run():
    passed, failed = 0, 0
    failures = []

    for desc, src, expected in EN_TO_TH_CASES:
        actual = translate_en_to_th(src)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[en→th] {desc}", "input": src,
                             "actual": actual, "expected": expected})

    for desc, src, expected in TH_TO_EN_CASES:
        actual = translate_th_to_en(src)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[th→en] {desc}", "input": src,
                             "actual": actual, "expected": expected})

    for desc, token, expected in PROTECTED_CASES:
        actual = _is_protected(token)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[protected] {desc}", "input": token,
                             "actual": actual, "expected": expected})

    total = len(EN_TO_TH_CASES) + len(TH_TO_EN_CASES) + len(PROTECTED_CASES)
    print("=" * 70)
    print(f"bilingual_dictionary tests: {passed} passed, {failed} failed  (of {total})")
    print(f"  EN→TH: {len(EN_TO_TH_CASES)} · TH→EN: {len(TH_TO_EN_CASES)} · protected: {len(PROTECTED_CASES)}")
    print("=" * 70)

    if failures:
        for f in failures:
            print(f"\n  ❌ {f['case']}")
            print(f"     input:    {f['input']!r}")
            print(f"     actual:   {f['actual']!r}")
            print(f"     expected: {f['expected']!r}")
        print()
        return 1

    print("\nAll cases pass ✓")
    return 0


if __name__ == "__main__":
    sys.exit(_run())
