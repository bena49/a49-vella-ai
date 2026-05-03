# ======================================================================
# test_sheet_numbering.py — Self-contained tests for the new (post-2026-05-04
# launch) A49 sheet numbering format.
#
# Format summary (full spec lives in next_release_sheet_numbering_refactor.md):
#
#   A0 — General/Cover         sequence  0000, 0010, 0020 …  (+10)
#   A1 — Floor Plans           level     1000=SITE, 1010=L1, …, B1=1009 (-1 per level)
#   A2 — Elevations            sequence  2010, 2020 …
#   A3 — Building Sections     sequence  3010, 3020 …
#   A4 — Wall Sections         sequence  4010, 4020 …
#   A5 — Ceiling Plans         level     same shape as A1 (5xxx). NO sheet for SITE.
#   A6 — Enlarged Plans        sequence  6010, 6020 …
#   A7 — Vertical Circulation  sequence  7010, 7020 …
#   A8 — Door & Window Sched.  sequence  8010, 8020 …
#   X0 — Custom                sequence  X000, X010, X020 …  (3 digits after X)
#
# Level-based rules (A1/A5):
#   - SITE  → base + 0      (A1.SITE=1000, A5.SITE → REJECTED)
#   - L<N>  → base + N×10   (L1=1010, L99=1990; cap L99)
#   - B<N>  → base + (10−N) (B1=1009, B9=1001; cap B9)
#   - ROOF/TOP → base + (max_above_grade_level + 1)×10
#         (computed from PROJECT level inventory, not from request levels)
#   - Special-suffix levels (M, T, etc.) → base slot + 1, first-come-first-served.
#         Collision → keep incrementing by 1 until free.
#         Above-grade only: L1=1010, L1M=1011, L1T (if 1011 taken) → 1012.
#         Below-grade B1M is documented as KNOWN LIMITATION → falls back to
#         alphabetical 'a' suffix (1009a). Rare in practice; spec didn't define.
#
# Sequence-based rules:
#   - A0 starts at 0000 (cover slot). Others start at <series>010.
#   - Always pick max(existing) + 10 (no gap-filling — user inserts manually
#     between slots, or via a future wizard).
#
# Run standalone:
#   cd backend
#   python -m ai_router.ai_engines.test_sheet_numbering
# ======================================================================

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

from ai_router.ai_engines.naming_engine import (
    compute_sheet_slot,
    get_next_sheet_number,
    generate_smart_name,
    SHEET_SET_MAP,
)


# ── PROJECT FIXTURES (level inventories used to resolve ROOF) ────────────

PROJECT_L8 = [
    "ROOF LEVEL", "LEVEL 8", "LEVEL 7T", "LEVEL 7", "LEVEL 6M", "LEVEL 6",
    "LEVEL 5", "LEVEL 4", "LEVEL 3", "LEVEL 2", "LEVEL 1",
    "SITE",
    "LEVEL B1M", "LEVEL B1", "LEVEL B2",
]

PROJECT_L5 = [
    "ROOF LEVEL", "LEVEL 5", "LEVEL 4", "LEVEL 3", "LEVEL 2", "LEVEL 1",
    "SITE", "LEVEL B1",
]

PROJECT_L7_TH = [
    "+37.30 ระดับสูงสุดของอาคาร",
    "+32.30 ระดับพื้นชั้นดาดฟ้า",
    "+28.10 ระดับพื้นชั้น 7",
    "+23.90 ระดับพื้นชั้น 6",
    "+19.70 ระดับพื้นชั้น 5",
    "+15.50 ระดับพื้นชั้น 4",
    "+10.50 ระดับพื้นชั้น 3",
    "+5.50 ระดับพื้นชั้น 2",
    "+0.50 ระดับพื้นชั้น 1",
    "+0.00 ระดับพื้นดิน",
]


# ── SEQUENCE CASES ────────────────────────────────────────────────────────
# (description, sheet_type, existing_numbers) → expected next number

SEQUENCE_CASES = [
    # A0 — first slot is 0000 (Cover)
    ("A0 first sheet",                "A0", [],                                "0000"),
    ("A0 second sheet",               "A0", ["0000"],                          "0010"),
    ("A0 third sheet",                "A0", ["0000", "0010"],                  "0020"),
    ("A0 after gap (skips, no fill)", "A0", ["0000", "0010", "0030"],          "0040"),
    ("A0 with insert",                "A0", ["0000", "0010", "0011", "0020"],  "0030"),
    ("A0 ignores other categories",   "A0", ["0000", "1010", "2020"],          "0010"),

    # A2/A3/A4 — start at <series>010
    ("A2 first",                      "A2", [],                                "2010"),
    ("A2 second",                     "A2", ["2010"],                          "2020"),
    ("A2 fifth",                      "A2", ["2010", "2020", "2030", "2040"],  "2050"),
    ("A3 first",                      "A3", [],                                "3010"),
    ("A4 first",                      "A4", [],                                "4010"),

    # A6/A7/A8 — same pattern
    ("A6 first",                      "A6", [],                                "6010"),
    ("A6 third",                      "A6", ["6010", "6020"],                  "6030"),
    ("A7 first",                      "A7", [],                                "7010"),
    ("A8 first",                      "A8", [],                                "8010"),
    ("A8 second",                     "A8", ["8010"],                          "8020"),

    # X0 — letter prefix, 3 digits
    ("X0 first",                      "X0", [],                                "X000"),
    ("X0 second",                     "X0", ["X000"],                          "X010"),
    ("X0 ignores numeric sheets",     "X0", ["X000", "1010", "2020"],          "X010"),

    # Cross-category isolation: A2's existing don't affect A3
    ("A3 isolated from A2",           "A3", ["2010", "2020", "2030"],          "3010"),
]


# ── LEVEL CASES ───────────────────────────────────────────────────────────
# (description, sheet_type, level_name, project_levels, existing_numbers) → expected

LEVEL_CASES = [
    # A1 — Above grade
    ("A1 + L1",                  "A1", "LEVEL 1",  PROJECT_L8, [], "1010"),
    ("A1 + L2",                  "A1", "LEVEL 2",  PROJECT_L8, [], "1020"),
    ("A1 + L5",                  "A1", "LEVEL 5",  PROJECT_L8, [], "1050"),
    ("A1 + L8",                  "A1", "LEVEL 8",  PROJECT_L8, [], "1080"),
    ("A1 + L1 (Thai)",           "A1", "+0.50 ระดับพื้นชั้น 1", PROJECT_L7_TH, [], "1010"),
    ("A1 + L7 (Thai)",           "A1", "+28.10 ระดับพื้นชั้น 7", PROJECT_L7_TH, [], "1070"),

    # A1 — Mezzanine / Transfer (suffix → +1, FCFS)
    ("A1 + L1M",                 "A1", "LEVEL 1M", PROJECT_L8, ["1010"],            "1011"),
    ("A1 + L6M",                 "A1", "LEVEL 6M", PROJECT_L8, ["1060"],            "1061"),
    ("A1 + L7T",                 "A1", "LEVEL 7T", PROJECT_L8, ["1070"],            "1071"),
    ("A1 + L1T after L1M",       "A1", "LEVEL 1T", PROJECT_L8, ["1010", "1011"],    "1012"),

    # A1 — Below grade
    ("A1 + B1",                  "A1", "LEVEL B1", PROJECT_L8, [], "1009"),
    ("A1 + B2",                  "A1", "LEVEL B2", PROJECT_L8, [], "1008"),
    ("A1 + B3",                  "A1", "LEVEL B3", PROJECT_L8, [], "1007"),
    ("A1 + B9 (cap)",            "A1", "LEVEL B9", PROJECT_L8, [], "1001"),

    # A1 — Site
    ("A1 + SITE",                "A1", "SITE",     PROJECT_L8, [], "1000"),
    ("A1 + SITE (Thai)",         "A1", "+0.00 ระดับพื้นดิน", PROJECT_L7_TH, [], "1000"),

    # A1 — Roof: max above-grade L + 10. Project max = L8 → 1090.
    ("A1 + ROOF (max L8)",       "A1", "ROOF LEVEL", PROJECT_L8, [], "1090"),
    ("A1 + ROOF (max L5)",       "A1", "ROOF LEVEL", PROJECT_L5, [], "1060"),
    ("A1 + ดาดฟ้า (max L7)",      "A1", "+32.30 ระดับพื้นชั้นดาดฟ้า", PROJECT_L7_TH, [], "1080"),

    # A1 — TOP (when both RF and TOP exist, both share the max+10 logic
    # and FCFS resolves the collision)
    ("A1 + TOP after RF taken",  "A1", "+37.30 ระดับสูงสุดของอาคาร", PROJECT_L7_TH,
                                                                   ["1080"],         "1081"),

    # A5 — same pattern, base 5000
    ("A5 + L1",                  "A5", "LEVEL 1",   PROJECT_L8, [], "5010"),
    ("A5 + L2",                  "A5", "LEVEL 2",   PROJECT_L8, [], "5020"),
    ("A5 + L5",                  "A5", "LEVEL 5",   PROJECT_L8, [], "5050"),
    ("A5 + B1",                  "A5", "LEVEL B1",  PROJECT_L8, [], "5009"),
    ("A5 + L1M",                 "A5", "LEVEL 1M",  PROJECT_L8, ["5010"],            "5011"),
    ("A5 + ROOF (max L8)",       "A5", "ROOF LEVEL", PROJECT_L8, [], "5090"),

    # A5 + SITE — REJECTED (returns None — caller handles error message / disabled UI)
    ("A5 + SITE rejected",       "A5", "SITE",      PROJECT_L8, [], None),
    ("A5 + SITE (Thai) rejected","A5", "+0.00 ระดับพื้นดิน", PROJECT_L7_TH, [], None),

    # Collision: requested slot already taken → keep +1 until free
    ("A1 + L1 when 1010 taken",  "A1", "LEVEL 1",   PROJECT_L8, ["1010"],            "1011"),
    ("A1 + L1 when 1010+1011 taken", "A1", "LEVEL 1", PROJECT_L8, ["1010", "1011"],  "1012"),
]


# ── SMART NAME CASES ──────────────────────────────────────────────────────
# (description, sheet_type, sheet_number, level_name) → expected name

NAME_CASES = [
    # A0 — name keyed to slot
    ("A0[0000] = COVER",                 "A0", "0000", None,           "COVER"),
    ("A0[0010] = DRAWING INDEX",         "A0", "0010", None,           "DRAWING INDEX"),
    ("A0[0020] = SITE AND VICINITY PLAN","A0", "0020", None,           "SITE AND VICINITY PLAN"),
    ("A0[0030] = STANDARD SYMBOLS",      "A0", "0030", None,           "STANDARD SYMBOLS"),
    ("A0[0040] = SAFETY PLAN",           "A0", "0040", None,           "SAFETY PLAN"),
    ("A0[0050] = WALL TYPES",            "A0", "0050", None,           "WALL TYPES"),
    ("A0[0060] = CUSTOM SHEET (overflow)","A0","0060", None,           "CUSTOM SHEET"),

    # A1 — level-based
    ("A1 + L1 = LEVEL 1 FLOOR PLAN",     "A1", "1010", "LEVEL 1",      "LEVEL 1 FLOOR PLAN"),
    ("A1 + L5 = LEVEL 5 FLOOR PLAN",     "A1", "1050", "LEVEL 5",      "LEVEL 5 FLOOR PLAN"),
    ("A1 + L1M = LEVEL 1M FLOOR PLAN",   "A1", "1011", "LEVEL 1M",     "LEVEL 1M FLOOR PLAN"),
    ("A1 + B1 = LEVEL B1 FLOOR PLAN",    "A1", "1009", "LEVEL B1",     "LEVEL B1 FLOOR PLAN"),
    ("A1 + SITE = SITE PLAN",            "A1", "1000", "SITE",         "SITE PLAN"),
    ("A1 + ROOF = LEVEL ROOF PLAN",      "A1", "1090", "ROOF LEVEL",   "LEVEL ROOF PLAN"),

    # A5 — same shape, "CEILING PLAN" suffix
    ("A5 + L1 = LEVEL 1 CEILING PLAN",   "A5", "5010", "LEVEL 1",      "LEVEL 1 CEILING PLAN"),
    ("A5 + B1 = LEVEL B1 CEILING PLAN",  "A5", "5009", "LEVEL B1",     "LEVEL B1 CEILING PLAN"),
    ("A5 + ROOF = LEVEL ROOF CEILING PLAN","A5","5090","ROOF LEVEL",  "LEVEL ROOF CEILING PLAN"),

    # A2/A3/A4 — fixed names (sequence position determines name)
    ("A2[2010] = ELEVATIONS",            "A2", "2010", None,           "ELEVATIONS"),
    ("A2[2030] = ELEVATIONS",            "A2", "2030", None,           "ELEVATIONS"),
    ("A3[3010] = BUILDING SECTIONS",     "A3", "3010", None,           "BUILDING SECTIONS"),
    ("A4[4010] = WALL SECTIONS",         "A4", "4010", None,           "WALL SECTIONS"),

    # A6 — first three slots have specific names, rest CUSTOM SHEET
    ("A6[6010] = ENLARGED TOILET PLAN",  "A6", "6010", None,           "ENLARGED TOILET PLAN"),
    ("A6[6020] = FLOOR PATTERN PLAN",    "A6", "6020", None,           "FLOOR PATTERN PLAN"),
    ("A6[6030] = CANOPY PLAN",           "A6", "6030", None,           "CANOPY PLAN"),
    ("A6[6040] = CUSTOM SHEET",          "A6", "6040", None,           "CUSTOM SHEET"),

    # A7 — four predefined slots
    ("A7[7010] = ENLARGED STAIR PLAN",   "A7", "7010", None,           "ENLARGED STAIR PLAN"),
    ("A7[7020] = ENLARGED STAIR SECTION","A7", "7020", None,           "ENLARGED STAIR SECTION"),
    ("A7[7030] = ENLARGED RAMP PLAN",    "A7", "7030", None,           "ENLARGED RAMP PLAN"),
    ("A7[7040] = ENLARGED LIFT PLAN",    "A7", "7040", None,           "ENLARGED LIFT PLAN"),
    ("A7[7050] = CUSTOM SHEET",          "A7", "7050", None,           "CUSTOM SHEET"),

    # A8 — two predefined slots
    ("A8[8010] = DOOR SCHEDULE",         "A8", "8010", None,           "DOOR SCHEDULE"),
    ("A8[8020] = WINDOW SCHEDULE",       "A8", "8020", None,           "WINDOW SCHEDULE"),
    ("A8[8030] = CUSTOM SHEET",          "A8", "8030", None,           "CUSTOM SHEET"),

    # X0 — always CUSTOM SHEET
    ("X0[X000] = CUSTOM SHEET",          "X0", "X000", None,           "CUSTOM SHEET"),
    ("X0[X030] = CUSTOM SHEET",          "X0", "X030", None,           "CUSTOM SHEET"),
]


# ── SHEET SET NAME CASES ──────────────────────────────────────────────────
# Confirm the canonical sheet-set names are restored after the spec sync.

SHEET_SET_CASES = [
    ("A0",  "A0_GENERAL INFORMATION"),
    ("A1",  "A1_FLOOR PLANS"),
    ("A2",  "A2_BUILDING ELEVATIONS"),
    ("A3",  "A3_BUILDING SECTIONS"),
    ("A4",  "A4_WALL SECTIONS"),
    ("A5",  "A5_CEILING PLANS"),
    ("A6",  "A6_ENLARGED PLANS AND INTERIOR ELEVATIONS"),
    ("A7",  "A7_VERTICAL CIRCULATION"),
    ("A8",  "A8_DOOR AND WINDOW SCHEDULE"),
    ("A9",  "A9_DETAILS"),
]


# ── HARNESS ───────────────────────────────────────────────────────────────

def _run():
    passed, failed = 0, 0
    failures = []

    # Sequence cases
    for desc, st, existing, expected in SEQUENCE_CASES:
        actual = get_next_sheet_number(st, existing)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[seq]  {desc}", "input": (st, existing),
                             "actual": actual, "expected": expected})

    # Level cases
    for desc, st, lvl, project_levels, existing, expected in LEVEL_CASES:
        actual = get_next_sheet_number(st, existing, level_name=lvl,
                                       project_levels=project_levels)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[lvl]  {desc}",
                             "input": (st, lvl, project_levels, existing),
                             "actual": actual, "expected": expected})

    # Smart name cases
    for desc, st, num, lvl, expected in NAME_CASES:
        actual = generate_smart_name(st, sheet_number=num, level_name=lvl)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[name] {desc}", "input": (st, num, lvl),
                             "actual": actual, "expected": expected})

    # Sheet-set name cases
    for st, expected in SHEET_SET_CASES:
        actual = SHEET_SET_MAP.get(st)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[set]  {st}", "input": st,
                             "actual": actual, "expected": expected})

    total = (len(SEQUENCE_CASES) + len(LEVEL_CASES) +
             len(NAME_CASES) + len(SHEET_SET_CASES))
    print("=" * 70)
    print(f"sheet_numbering tests: {passed} passed, {failed} failed  (of {total})")
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
