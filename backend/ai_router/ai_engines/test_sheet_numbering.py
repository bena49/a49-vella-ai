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
    build_sheets_payload,
    get_active_scheme,
    resolve_scheme_for_request,
    _detect_scheme_from_sheets,
    _parse_slot,
    SHEET_SET_MAP,
    SCHEMES,
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
    # NOTE: get_next_sheet_number defaults request_levels=None inside this
    # harness, so these cases hit the project-fallback path. The build_sheets
    # path passes request_levels for the in-batch case (covered by the next
    # block + manual Revit testing).
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

    # Basement collision: pushes DOWN (deeper), not UP into L1 territory
    ("A1 + B1 when 1009 taken (down)",  "A1", "LEVEL B1", PROJECT_L8, ["1009"],           "1008"),
    ("A1 + B1 when 1008+1009 taken",    "A1", "LEVEL B1", PROJECT_L8, ["1008", "1009"],   "1007"),
    # User-reported case: existing [SITE, B1, B1M, L1, L2] + duplicate B1 → 1007
    ("A1 + B1 user-reported case",      "A1", "LEVEL B1", PROJECT_L8,
                                        ["1000", "1008", "1009", "1010", "1020"],         "1007"),
    # A5 mirror: same direction
    ("A5 + B1 when 5009 taken (down)",  "A5", "LEVEL B1", PROJECT_L8, ["5009"],           "5008"),
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

    # A6 — first three slots have specific names, rest CUSTOM SHEET.
    # Reordered per the post-V2 spec: FLOOR PATTERN PLAN at 6010, then TOILET.
    ("A6[6010] = FLOOR PATTERN PLAN",    "A6", "6010", None,           "FLOOR PATTERN PLAN"),
    ("A6[6020] = ENLARGED TOILET PLAN",  "A6", "6020", None,           "ENLARGED TOILET PLAN"),
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


# ── BUILD-PAYLOAD INTEGRATION CASES ───────────────────────────────────────
# These exercise build_sheets_payload end-to-end (the path the chat command
# actually takes). Critical regression case: when the project cache is stale
# or contains hidden reference levels, the request-level max should win for
# ROOF placement.

PROJECT_STALE_HAS_L45 = [
    "+45.45 LEVEL 45",   # Stray reference level (e.g. from a prior project)
    "ROOF LEVEL", "LEVEL 3", "LEVEL 2", "LEVEL 1", "SITE",
]

USER_REPORTED_TH_PROJECT = [
    "+11.00 ระดับสูงสุดของอาคาร",
    "+7.50 ระดับพื้นชั้น 3",
    "+4.00 ระดับพื้นชั้น 2",
    "+0.50 ระดับพื้นชั้น 1",
    "+0.00 ระดับพื้นดิน",
    "-3.00 ระดับชั้นใต้ดิน B1M",
    "-6.00 ระดับชั้นใต้ดิน B1",
]

PAYLOAD_CASES = [
    # User-reported bug: A1 batch with [SITE, L1, L2, L3, TOP] in a Thai
    # project — TOP should land at slot 1040 (max in batch is L3 → 4×10).
    {
        "desc": "TH project: A1 [SITE, L1, L2, L3, TOP] → TOP=1040",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": [
                "+0.00 ระดับพื้นดิน",
                "+0.50 ระดับพื้นชั้น 1",
                "+4.00 ระดับพื้นชั้น 2",
                "+7.50 ระดับพื้นชั้น 3",
                "+11.00 ระดับสูงสุดของอาคาร",
            ],
            "project_levels": USER_REPORTED_TH_PROJECT,
        },
        "expected_numbers": ["1000", "1010", "1020", "1030", "1040"],
    },
    # Same shape, A5 (no SITE sheet → only 4 sheets created)
    {
        "desc": "TH project: A5 [L1, L2, L3, TOP] → TOP=5040",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A5",
            "stage": "CD",
            "levels": [
                "+0.50 ระดับพื้นชั้น 1",
                "+4.00 ระดับพื้นชั้น 2",
                "+7.50 ระดับพื้นชั้น 3",
                "+11.00 ระดับสูงสุดของอาคาร",
            ],
            "project_levels": USER_REPORTED_TH_PROJECT,
        },
        "expected_numbers": ["5010", "5020", "5030", "5040"],
    },
    # Stale cache regression: request has L1-L3 + ROOF, but cache contains
    # a stray L45 (e.g. a hidden reference level or leftover from a prior
    # project). Request levels must win → ROOF=1040, not 1460.
    {
        "desc": "Stale cache (L45 in cache) shouldn't push ROOF to 1460",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": ["LEVEL 1", "LEVEL 2", "LEVEL 3", "ROOF LEVEL"],
            "project_levels": PROJECT_STALE_HAS_L45,
        },
        "expected_numbers": ["1010", "1020", "1030", "1040"],
    },
    # Fallback case: request only has ROOF — must use cache to know max.
    # Here cache is clean PROJECT_L5 (max L5), so ROOF → 1060.
    {
        "desc": "ROOF-only request falls back to project cache (max L5)",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": ["ROOF LEVEL"],
            "project_levels": PROJECT_L5,
        },
        "expected_numbers": ["1060"],
    },

    # ── Basement shift cases ─────────────────────────────────────────────
    # Suffix variant takes parent's natural slot; bare shifts DOWN.

    # User-reported case: B1 + B1M → B1M=1009 (was at parent's slot),
    # B1 shifts to 1008 (was at 1009).
    {
        "desc": "B1 + B1M → B1M=1009, B1=1008",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": ["LEVEL B1", "LEVEL B1M"],
            "project_levels": PROJECT_L5,
        },
        # Output sorted by number ascending
        "expected_numbers": ["1008", "1009"],
    },
    # B2 alone — natural slot preserved (no shift).
    {
        "desc": "B2 alone → 1008 (natural slot)",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": ["LEVEL B2"],
            "project_levels": PROJECT_L5,
        },
        "expected_numbers": ["1008"],
    },
    # B2 + B2M (no B1) — B2M takes B2's natural slot, B2 shifts down.
    {
        "desc": "B2 + B2M (no B1) → B2M=1008, B2=1007",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": ["LEVEL B2", "LEVEL B2M"],
            "project_levels": PROJECT_L5,
        },
        "expected_numbers": ["1007", "1008"],
    },
    # B1 + B2 + B2M — B1 unchanged (no M variant above it), B2/B2M shift.
    {
        "desc": "B1 + B2 + B2M → B1=1009, B2M=1008, B2=1007",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": ["LEVEL B1", "LEVEL B2", "LEVEL B2M"],
            "project_levels": PROJECT_L5,
        },
        "expected_numbers": ["1007", "1008", "1009"],
    },
    # Worst case: both basements have mezzanines — full cascade.
    {
        "desc": "B1 + B1M + B2 + B2M → B1M=1009, B1=1008, B2M=1007, B2=1006",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": ["LEVEL B1", "LEVEL B1M", "LEVEL B2", "LEVEL B2M"],
            "project_levels": PROJECT_L5,
        },
        "expected_numbers": ["1006", "1007", "1008", "1009"],
    },
    # User's exact reported case: B1 + B1M alongside the above-grade levels.
    # No more L1 collision because B1M=1009 (not 1010).
    {
        "desc": "User-reported full case: TH project [SITE,B1,B1M,L1,L2,L3,TOP]",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": [
                "+0.00 ระดับพื้นดิน",
                "-6.00 ระดับชั้นใต้ดิน B1",
                "-3.00 ระดับชั้นใต้ดิน B1M",
                "+0.50 ระดับพื้นชั้น 1",
                "+4.00 ระดับพื้นชั้น 2",
                "+7.50 ระดับพื้นชั้น 3",
                "+11.00 ระดับสูงสุดของอาคาร",
            ],
            "project_levels": USER_REPORTED_TH_PROJECT,
        },
        # SITE=1000, B2=skipped, B1=1008, B1M=1009, L1=1010, L2=1020, L3=1030, ROOF=1040
        "expected_numbers": ["1000", "1008", "1009", "1010", "1020", "1030", "1040"],
    },
    # Same shape on A5 (no SITE sheet — silently skipped)
    {
        "desc": "A5 mirror: [B1,B1M,L1,L2,L3,TOP] → 5008,5009,5010,5020,5030,5040",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A5",
            "stage": "CD",
            "levels": [
                "-6.00 ระดับชั้นใต้ดิน B1",
                "-3.00 ระดับชั้นใต้ดิน B1M",
                "+0.50 ระดับพื้นชั้น 1",
                "+4.00 ระดับพื้นชั้น 2",
                "+7.50 ระดับพื้นชั้น 3",
                "+11.00 ระดับสูงสุดของอาคาร",
            ],
            "project_levels": USER_REPORTED_TH_PROJECT,
        },
        "expected_numbers": ["5008", "5009", "5010", "5020", "5030", "5040"],
    },

    # ── view_type → sheet_category inference (Bug fix) ──────────────────
    # When create_and_place fires for "Create Ceiling Plan" but GPT drops
    # the sheet_category slot, build_sheets_payload must infer A5 from the
    # view_type — not silently default to A1.
    {
        "desc": "Ceiling Plan view + no sheet_category → A5 inferred",
        "request": {
            "command": "create_sheet",
            "sheet_category": None,
            "view_type": "Ceiling Plan",
            "stage": "CD",
            "levels": ["LEVEL 1"],
            "project_levels": PROJECT_L5,
        },
        "expected_numbers": ["5010"],
    },
    {
        "desc": "Floor Plan view + no sheet_category → A1 inferred (regression guard)",
        "request": {
            "command": "create_sheet",
            "sheet_category": None,
            "view_type": "Floor Plan",
            "stage": "CD",
            "levels": ["LEVEL 1"],
            "project_levels": PROJECT_L5,
        },
        "expected_numbers": ["1010"],
    },
]


# ============================================================================
# V2 (5-digit) FIXTURES
#
# Mirror the V1 cases above with V2-scaled slot values:
#   - All sheet numbers gain a digit (e.g. 1010 → 10100, X010 → X0100)
#   - Primary increment ×10 (10 → 100)
#   - Sub-level increment ×10 (1 → 10), so L1M = base+L*100+10 = 10110
#   - Basements: B<n> = base + (10-n) * 10 (B1=10090, B9=10010)
#   - SITE has 10 reserved slots in A1 (10000-10009)
#   - A6 reordered: FLOOR PATTERN PLAN (60100) before TOILET (60200)
# ============================================================================

V2 = SCHEMES["iso19650_5digit"]

SEQUENCE_CASES_V2 = [
    ("V2 A0 first sheet",                "A0", [],                                       "00000"),
    ("V2 A0 second sheet",               "A0", ["00000"],                                "00100"),
    ("V2 A0 third sheet",                "A0", ["00000", "00100"],                       "00200"),
    ("V2 A0 after gap (skips, no fill)", "A0", ["00000", "00100", "00300"],              "00400"),
    ("V2 A0 with insert",                "A0", ["00000", "00100", "00110", "00200"],    "00300"),
    ("V2 A0 ignores other categories",   "A0", ["00000", "10100", "20200"],              "00100"),

    ("V2 A2 first",                      "A2", [],                                       "20100"),
    ("V2 A2 second",                     "A2", ["20100"],                                "20200"),
    ("V2 A2 fifth",                      "A2", ["20100", "20200", "20300", "20400"],    "20500"),
    ("V2 A3 first",                      "A3", [],                                       "30100"),
    ("V2 A4 first",                      "A4", [],                                       "40100"),

    ("V2 A6 first",                      "A6", [],                                       "60100"),
    ("V2 A6 third",                      "A6", ["60100", "60200"],                       "60300"),
    ("V2 A7 first",                      "A7", [],                                       "70100"),
    ("V2 A8 first",                      "A8", [],                                       "80100"),
    ("V2 A8 second",                     "A8", ["80100"],                                "80200"),

    ("V2 X0 first",                      "X0", [],                                       "X0000"),
    ("V2 X0 second",                     "X0", ["X0000"],                                "X0100"),
    ("V2 X0 ignores numeric sheets",     "X0", ["X0000", "10100", "20200"],              "X0100"),

    ("V2 A3 isolated from A2",           "A3", ["20100", "20200", "20300"],              "30100"),
]

LEVEL_CASES_V2 = [
    # A1 — Above grade
    ("V2 A1 + L1",                  "A1", "LEVEL 1",  PROJECT_L8, [], "10100"),
    ("V2 A1 + L2",                  "A1", "LEVEL 2",  PROJECT_L8, [], "10200"),
    ("V2 A1 + L5",                  "A1", "LEVEL 5",  PROJECT_L8, [], "10500"),
    ("V2 A1 + L8",                  "A1", "LEVEL 8",  PROJECT_L8, [], "10800"),
    ("V2 A1 + L1 (Thai)",           "A1", "+0.50 ระดับพื้นชั้น 1", PROJECT_L7_TH, [], "10100"),
    ("V2 A1 + L7 (Thai)",           "A1", "+28.10 ระดับพื้นชั้น 7", PROJECT_L7_TH, [], "10700"),

    # A1 — Mezzanine / Transfer (suffix → +sub_inc=10, FCFS)
    ("V2 A1 + L1M",                 "A1", "LEVEL 1M", PROJECT_L8, ["10100"],            "10110"),
    ("V2 A1 + L6M",                 "A1", "LEVEL 6M", PROJECT_L8, ["10600"],            "10610"),
    ("V2 A1 + L7T",                 "A1", "LEVEL 7T", PROJECT_L8, ["10700"],            "10710"),
    ("V2 A1 + L1T after L1M",       "A1", "LEVEL 1T", PROJECT_L8, ["10100", "10110"],   "10120"),

    # A1 — Below grade (B<n> = base + (10-n)*10)
    ("V2 A1 + B1",                  "A1", "LEVEL B1", PROJECT_L8, [], "10090"),
    ("V2 A1 + B2",                  "A1", "LEVEL B2", PROJECT_L8, [], "10080"),
    ("V2 A1 + B3",                  "A1", "LEVEL B3", PROJECT_L8, [], "10070"),
    ("V2 A1 + B9 (cap)",            "A1", "LEVEL B9", PROJECT_L8, [], "10010"),

    # A1 — Site
    ("V2 A1 + SITE",                "A1", "SITE",     PROJECT_L8, [], "10000"),
    ("V2 A1 + SITE (Thai)",         "A1", "+0.00 ระดับพื้นดิน", PROJECT_L7_TH, [], "10000"),

    # A1 — Roof: max above-grade L * level_inc + level_inc.
    # Project max L8 → base + 9*100 = 10900.
    ("V2 A1 + ROOF (max L8)",       "A1", "ROOF LEVEL", PROJECT_L8, [], "10900"),
    ("V2 A1 + ROOF (max L5)",       "A1", "ROOF LEVEL", PROJECT_L5, [], "10600"),
    ("V2 A1 + ดาดฟ้า (max L7)",      "A1", "+32.30 ระดับพื้นชั้นดาดฟ้า", PROJECT_L7_TH, [], "10800"),

    # A1 — TOP collides with RF, FCFS pushes +sub_inc=10
    ("V2 A1 + TOP after RF taken",  "A1", "+37.30 ระดับสูงสุดของอาคาร", PROJECT_L7_TH,
                                                                   ["10800"],         "10810"),

    # A5 — same pattern, base 50000
    ("V2 A5 + L1",                  "A5", "LEVEL 1",   PROJECT_L8, [], "50100"),
    ("V2 A5 + L2",                  "A5", "LEVEL 2",   PROJECT_L8, [], "50200"),
    ("V2 A5 + L5",                  "A5", "LEVEL 5",   PROJECT_L8, [], "50500"),
    ("V2 A5 + B1",                  "A5", "LEVEL B1",  PROJECT_L8, [], "50090"),
    ("V2 A5 + L1M",                 "A5", "LEVEL 1M",  PROJECT_L8, ["50100"],           "50110"),
    ("V2 A5 + ROOF (max L8)",       "A5", "ROOF LEVEL", PROJECT_L8, [], "50900"),

    # A5 + SITE — REJECTED
    ("V2 A5 + SITE rejected",       "A5", "SITE",      PROJECT_L8, [], None),
    ("V2 A5 + SITE (Thai) rejected","A5", "+0.00 ระดับพื้นดิน", PROJECT_L7_TH, [], None),

    # Collision: requested slot already taken → keep +sub_inc until free
    ("V2 A1 + L1 when 10100 taken",  "A1", "LEVEL 1",   PROJECT_L8, ["10100"],          "10110"),
    ("V2 A1 + L1 when 10100+10110 taken", "A1", "LEVEL 1", PROJECT_L8, ["10100", "10110"], "10120"),

    # Basement collision: pushes DOWN (deeper) by sub_inc=10
    ("V2 A1 + B1 when 10090 taken (down)",  "A1", "LEVEL B1", PROJECT_L8, ["10090"],           "10080"),
    ("V2 A1 + B1 when 10080+10090 taken",   "A1", "LEVEL B1", PROJECT_L8, ["10080", "10090"],  "10070"),
    ("V2 A1 + B1 user-reported case",       "A1", "LEVEL B1", PROJECT_L8,
                                            ["10000", "10080", "10090", "10100", "10200"],     "10070"),
    ("V2 A5 + B1 when 50090 taken (down)",  "A5", "LEVEL B1", PROJECT_L8, ["50090"],           "50080"),
]

NAME_CASES_V2 = [
    # A0 — name keyed to slot (5-digit)
    ("V2 A0[00000] = COVER",                  "A0", "00000", None,      "COVER"),
    ("V2 A0[00100] = DRAWING INDEX",          "A0", "00100", None,      "DRAWING INDEX"),
    ("V2 A0[00200] = SITE AND VICINITY PLAN", "A0", "00200", None,      "SITE AND VICINITY PLAN"),
    ("V2 A0[00300] = STANDARD SYMBOLS",       "A0", "00300", None,      "STANDARD SYMBOLS"),
    ("V2 A0[00400] = SAFETY PLAN",            "A0", "00400", None,      "SAFETY PLAN"),
    ("V2 A0[00500] = WALL TYPES",             "A0", "00500", None,      "WALL TYPES"),
    ("V2 A0[00600] = CUSTOM SHEET (overflow)","A0", "00600", None,      "CUSTOM SHEET"),

    # A1 — level-based
    ("V2 A1 + L1 = LEVEL 1 FLOOR PLAN",   "A1", "10100", "LEVEL 1",     "LEVEL 1 FLOOR PLAN"),
    ("V2 A1 + L5 = LEVEL 5 FLOOR PLAN",   "A1", "10500", "LEVEL 5",     "LEVEL 5 FLOOR PLAN"),
    ("V2 A1 + L1M = LEVEL 1M FLOOR PLAN", "A1", "10110", "LEVEL 1M",    "LEVEL 1M FLOOR PLAN"),
    ("V2 A1 + B1 = LEVEL B1 FLOOR PLAN",  "A1", "10090", "LEVEL B1",    "LEVEL B1 FLOOR PLAN"),
    ("V2 A1 + SITE = SITE PLAN",          "A1", "10000", "SITE",        "SITE PLAN"),
    ("V2 A1 + ROOF = LEVEL ROOF PLAN",    "A1", "10900", "ROOF LEVEL",  "LEVEL ROOF PLAN"),

    # A5 — same shape, "CEILING PLAN" suffix
    ("V2 A5 + L1 = LEVEL 1 CEILING PLAN",   "A5", "50100", "LEVEL 1",   "LEVEL 1 CEILING PLAN"),
    ("V2 A5 + B1 = LEVEL B1 CEILING PLAN",  "A5", "50090", "LEVEL B1",  "LEVEL B1 CEILING PLAN"),
    ("V2 A5 + ROOF = LEVEL ROOF CEILING PLAN","A5","50900","ROOF LEVEL","LEVEL ROOF CEILING PLAN"),

    # A2/A3/A4 — fixed names
    ("V2 A2[20100] = ELEVATIONS",         "A2", "20100", None,          "ELEVATIONS"),
    ("V2 A2[20300] = ELEVATIONS",         "A2", "20300", None,          "ELEVATIONS"),
    ("V2 A3[30100] = BUILDING SECTIONS",  "A3", "30100", None,          "BUILDING SECTIONS"),
    ("V2 A4[40100] = WALL SECTIONS",      "A4", "40100", None,          "WALL SECTIONS"),

    # A6 — REORDERED in V2 spec: FLOOR PATTERN PLAN first, TOILET second
    ("V2 A6[60100] = FLOOR PATTERN PLAN", "A6", "60100", None,          "FLOOR PATTERN PLAN"),
    ("V2 A6[60200] = ENLARGED TOILET PLAN","A6","60200", None,          "ENLARGED TOILET PLAN"),
    ("V2 A6[60300] = CANOPY PLAN",        "A6", "60300", None,          "CANOPY PLAN"),
    ("V2 A6[60400] = CUSTOM SHEET",       "A6", "60400", None,          "CUSTOM SHEET"),

    # A7 — same names, just 5-digit slots
    ("V2 A7[70100] = ENLARGED STAIR PLAN",   "A7", "70100", None,       "ENLARGED STAIR PLAN"),
    ("V2 A7[70200] = ENLARGED STAIR SECTION","A7", "70200", None,       "ENLARGED STAIR SECTION"),
    ("V2 A7[70300] = ENLARGED RAMP PLAN",    "A7", "70300", None,       "ENLARGED RAMP PLAN"),
    ("V2 A7[70400] = ENLARGED LIFT PLAN",    "A7", "70400", None,       "ENLARGED LIFT PLAN"),
    ("V2 A7[70500] = CUSTOM SHEET",          "A7", "70500", None,       "CUSTOM SHEET"),

    # A8 — same names, 5-digit slots
    ("V2 A8[80100] = DOOR SCHEDULE",      "A8", "80100", None,          "DOOR SCHEDULE"),
    ("V2 A8[80200] = WINDOW SCHEDULE",    "A8", "80200", None,          "WINDOW SCHEDULE"),
    ("V2 A8[80300] = CUSTOM SHEET",       "A8", "80300", None,          "CUSTOM SHEET"),

    # X0
    ("V2 X0[X0000] = CUSTOM SHEET",       "X0", "X0000", None,          "CUSTOM SHEET"),
    ("V2 X0[X0300] = CUSTOM SHEET",       "X0", "X0300", None,          "CUSTOM SHEET"),
]

PAYLOAD_CASES_V2 = [
    # TH project: A1 [SITE, L1, L2, L3, TOP] → ROOF lands at 10400
    {
        "desc": "V2 TH project: A1 [SITE, L1, L2, L3, TOP] → TOP=10400",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": [
                "+0.00 ระดับพื้นดิน",
                "+0.50 ระดับพื้นชั้น 1",
                "+4.00 ระดับพื้นชั้น 2",
                "+7.50 ระดับพื้นชั้น 3",
                "+11.00 ระดับสูงสุดของอาคาร",
            ],
            "project_levels": USER_REPORTED_TH_PROJECT,
        },
        "expected_numbers": ["10000", "10100", "10200", "10300", "10400"],
    },
    {
        "desc": "V2 TH project: A5 [L1, L2, L3, TOP] → TOP=50400",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A5",
            "stage": "CD",
            "levels": [
                "+0.50 ระดับพื้นชั้น 1",
                "+4.00 ระดับพื้นชั้น 2",
                "+7.50 ระดับพื้นชั้น 3",
                "+11.00 ระดับสูงสุดของอาคาร",
            ],
            "project_levels": USER_REPORTED_TH_PROJECT,
        },
        "expected_numbers": ["50100", "50200", "50300", "50400"],
    },
    # Stale-cache regression
    {
        "desc": "V2 stale cache (L45 in cache) shouldn't push ROOF past request max",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": ["LEVEL 1", "LEVEL 2", "LEVEL 3", "ROOF LEVEL"],
            "project_levels": PROJECT_STALE_HAS_L45,
        },
        "expected_numbers": ["10100", "10200", "10300", "10400"],
    },
    # Roof-only request falls back to project cache
    {
        "desc": "V2 ROOF-only request falls back to project cache (max L5) → 10600",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": ["ROOF LEVEL"],
            "project_levels": PROJECT_L5,
        },
        "expected_numbers": ["10600"],
    },

    # ── Basement shift cases (sub_inc=10) ───────────────────────────────
    # B1 + B1M → B1M takes 10090 (B1's natural slot), B1 shifts down by sub_inc=10 → 10080
    {
        "desc": "V2 B1 + B1M → B1M=10090, B1=10080",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": ["LEVEL B1", "LEVEL B1M"],
            "project_levels": PROJECT_L5,
        },
        "expected_numbers": ["10080", "10090"],
    },
    {
        "desc": "V2 B2 alone → 10080 (natural slot)",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": ["LEVEL B2"],
            "project_levels": PROJECT_L5,
        },
        "expected_numbers": ["10080"],
    },
    {
        "desc": "V2 B2 + B2M (no B1) → B2M=10080, B2=10070",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": ["LEVEL B2", "LEVEL B2M"],
            "project_levels": PROJECT_L5,
        },
        "expected_numbers": ["10070", "10080"],
    },
    {
        "desc": "V2 B1 + B2 + B2M → B1=10090, B2M=10080, B2=10070",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": ["LEVEL B1", "LEVEL B2", "LEVEL B2M"],
            "project_levels": PROJECT_L5,
        },
        "expected_numbers": ["10070", "10080", "10090"],
    },
    {
        "desc": "V2 full cascade: B1 + B1M + B2 + B2M → 10060,10070,10080,10090",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": ["LEVEL B1", "LEVEL B1M", "LEVEL B2", "LEVEL B2M"],
            "project_levels": PROJECT_L5,
        },
        "expected_numbers": ["10060", "10070", "10080", "10090"],
    },
    # User's exact reported case at V2 scale
    {
        "desc": "V2 user-reported full case: TH project [SITE,B1,B1M,L1,L2,L3,TOP]",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A1",
            "stage": "CD",
            "levels": [
                "+0.00 ระดับพื้นดิน",
                "-6.00 ระดับชั้นใต้ดิน B1",
                "-3.00 ระดับชั้นใต้ดิน B1M",
                "+0.50 ระดับพื้นชั้น 1",
                "+4.00 ระดับพื้นชั้น 2",
                "+7.50 ระดับพื้นชั้น 3",
                "+11.00 ระดับสูงสุดของอาคาร",
            ],
            "project_levels": USER_REPORTED_TH_PROJECT,
        },
        "expected_numbers": ["10000", "10080", "10090", "10100", "10200", "10300", "10400"],
    },
    {
        "desc": "V2 A5 mirror: [B1,B1M,L1,L2,L3,TOP] → 50080,50090,50100-50300,50400",
        "request": {
            "command": "create_sheet",
            "sheet_category": "A5",
            "stage": "CD",
            "levels": [
                "-6.00 ระดับชั้นใต้ดิน B1",
                "-3.00 ระดับชั้นใต้ดิน B1M",
                "+0.50 ระดับพื้นชั้น 1",
                "+4.00 ระดับพื้นชั้น 2",
                "+7.50 ระดับพื้นชั้น 3",
                "+11.00 ระดับสูงสุดของอาคาร",
            ],
            "project_levels": USER_REPORTED_TH_PROJECT,
        },
        "expected_numbers": ["50080", "50090", "50100", "50200", "50300", "50400"],
    },

    # view_type → sheet_category inference (V2)
    {
        "desc": "V2 Ceiling Plan view + no sheet_category → A5 inferred → 50100",
        "request": {
            "command": "create_sheet",
            "sheet_category": None,
            "view_type": "Ceiling Plan",
            "stage": "CD",
            "levels": ["LEVEL 1"],
            "project_levels": PROJECT_L5,
        },
        "expected_numbers": ["50100"],
    },
    {
        "desc": "V2 Floor Plan view + no sheet_category → A1 inferred → 10100",
        "request": {
            "command": "create_sheet",
            "sheet_category": None,
            "view_type": "Floor Plan",
            "stage": "CD",
            "levels": ["LEVEL 1"],
            "project_levels": PROJECT_L5,
        },
        "expected_numbers": ["10100"],
    },
]


# ============================================================================
# a49_dotted (set_v3) FIXTURES — Phase 1
#
# Dotted format: A<series>.<NN>. A1/A5 use the new "level_sequence" type:
#   - A1 reserves slot 0 for SITE (A1.00); user creates floors via wizard
#     and gets sequential slots A1.01, A1.02 … (no level→slot determinism).
#   - A5 has no SITE; sequence starts at A5.01.
#   - Gap-fill is enabled — if A1.03 is deleted, the next sheet reuses .03.
#
# Sub-parts (A1.03.1, A1.03.2) deferred to Phase 2.
# ============================================================================

V3 = SCHEMES["a49_dotted"]

SEQUENCE_CASES_V3 = [
    # A0 — name-keyed slots, +1 increment, gap-fill on
    ("V3 A0 first sheet",               "A0", [],                                "A0.00"),
    ("V3 A0 second sheet",              "A0", ["A0.00"],                         "A0.01"),
    ("V3 A0 third sheet",               "A0", ["A0.00", "A0.01"],                "A0.02"),
    ("V3 A0 gap-fill (deleted .01)",    "A0", ["A0.00", "A0.02"],                "A0.01"),
    ("V3 A0 gap-fill prefers lowest",   "A0", ["A0.00", "A0.02", "A0.04"],       "A0.01"),
    ("V3 A0 ignores other categories",  "A0", ["A0.00", "A1.05", "X0.02"],       "A0.01"),

    # A2/A3/A4 — start at .01 (no slot 00)
    ("V3 A2 first",                     "A2", [],                                "A2.01"),
    ("V3 A2 second",                    "A2", ["A2.01"],                         "A2.02"),
    ("V3 A2 gap-fill",                  "A2", ["A2.01", "A2.03"],                "A2.02"),
    ("V3 A3 first",                     "A3", [],                                "A3.01"),
    ("V3 A4 first",                     "A4", [],                                "A4.01"),

    # A6/A7/A8 — same shape, with named_slots for first few
    ("V3 A6 first",                     "A6", [],                                "A6.01"),
    ("V3 A6 third",                     "A6", ["A6.01", "A6.02"],                "A6.03"),
    ("V3 A7 first",                     "A7", [],                                "A7.01"),
    ("V3 A8 first",                     "A8", [],                                "A8.01"),
    ("V3 A8 second",                    "A8", ["A8.01"],                         "A8.02"),
    ("V3 A9 first",                     "A9", [],                                "A9.01"),

    # X0 — dotted, starts at .00 like A0 (cover-slot semantics)
    ("V3 X0 first",                     "X0", [],                                "X0.00"),
    ("V3 X0 second",                    "X0", ["X0.00"],                         "X0.01"),

    # Cross-category isolation
    ("V3 A3 isolated from A2",          "A3", ["A2.01", "A2.02", "A2.03"],       "A3.01"),
]

LEVEL_SEQUENCE_CASES_V3 = [
    # (description, sheet_type, level_name, project_levels, existing, expected)

    # A1 SITE → .00 anchor (level_sequence with site_slots=1)
    ("V3 A1 + SITE empty",         "A1", "SITE",     PROJECT_L8, [], "A1.00"),
    ("V3 A1 + SITE (Thai)",        "A1", "+0.00 ระดับพื้นดิน", PROJECT_L7_TH, [], "A1.00"),
    # A1 SITE collision: A1.00 already taken — return None (Phase 2 will allow A1.00.1 splits)
    ("V3 A1 + SITE when .00 taken","A1", "SITE",     PROJECT_L8, ["A1.00"], None),

    # A1 floors: sequence-allocated, NOT tied to level number
    ("V3 A1 + L1 empty (gets .01)","A1", "LEVEL 1",  PROJECT_L8, [],                                  "A1.01"),
    ("V3 A1 + L1 after SITE",      "A1", "LEVEL 1",  PROJECT_L8, ["A1.00"],                           "A1.01"),
    ("V3 A1 + L2 after SITE+L1",   "A1", "LEVEL 2",  PROJECT_L8, ["A1.00", "A1.01"],                  "A1.02"),
    # gap-fill for floors: deleted A1.02, next L4 fills .02 not .04
    ("V3 A1 floor gap-fill",       "A1", "LEVEL 4",  PROJECT_L8, ["A1.00", "A1.01", "A1.03"],         "A1.02"),
    # B1 — also just sequence allocation (no special basement math in level_sequence)
    ("V3 A1 + B1 first non-SITE",  "A1", "LEVEL B1", PROJECT_L8, ["A1.00"],                           "A1.01"),
    # ROOF — sequence allocation, takes next free slot
    ("V3 A1 + ROOF after L1+L2",   "A1", "ROOF LEVEL", PROJECT_L8, ["A1.00", "A1.01", "A1.02"],       "A1.03"),

    # A5 — no SITE; sequence starts at .01
    ("V3 A5 + SITE rejected",      "A5", "SITE",     PROJECT_L8, [], None),
    ("V3 A5 + SITE (Thai) rej",    "A5", "+0.00 ระดับพื้นดิน", PROJECT_L7_TH, [], None),
    ("V3 A5 + L1 empty",           "A5", "LEVEL 1",  PROJECT_L8, [],          "A5.01"),
    ("V3 A5 + L2 after L1",        "A5", "LEVEL 2",  PROJECT_L8, ["A5.01"],   "A5.02"),
    ("V3 A5 + B1 sequence",        "A5", "LEVEL B1", PROJECT_L8, ["A5.01"],   "A5.02"),
]

NAME_CASES_V3 = [
    # A0 named slots — same content as V1/V2, just different format
    ("V3 A0[A0.00] = COVER",                    "A0", "A0.00", None, "COVER"),
    ("V3 A0[A0.01] = DRAWING INDEX",            "A0", "A0.01", None, "DRAWING INDEX"),
    ("V3 A0[A0.02] = SITE AND VICINITY PLAN",   "A0", "A0.02", None, "SITE AND VICINITY PLAN"),
    ("V3 A0[A0.06] = CUSTOM SHEET (overflow)",  "A0", "A0.06", None, "CUSTOM SHEET"),

    # A1/A5 — level-driven naming (uses sheet_type + level_name regardless of scheme type)
    ("V3 A1 + L1 = LEVEL 1 FLOOR PLAN",         "A1", "A1.03", "LEVEL 1",     "LEVEL 1 FLOOR PLAN"),
    ("V3 A1 + B1 = LEVEL B1 FLOOR PLAN",        "A1", "A1.01", "LEVEL B1",    "LEVEL B1 FLOOR PLAN"),
    ("V3 A1 + SITE = SITE PLAN",                "A1", "A1.00", "SITE",        "SITE PLAN"),
    ("V3 A1 + ROOF = LEVEL ROOF PLAN",          "A1", "A1.05", "ROOF LEVEL",  "LEVEL ROOF PLAN"),
    ("V3 A5 + L1 = LEVEL 1 CEILING PLAN",       "A5", "A5.01", "LEVEL 1",     "LEVEL 1 CEILING PLAN"),

    # A6 — uses V2 reorder (FLOOR PATTERN PLAN first, then TOILET)
    # Note named_slots indexing: idx 0 = slot 0, idx 1 = slot 1, etc.
    # V3 A6 named_slots = [None, "FLOOR PATTERN PLAN", "ENLARGED TOILET PLAN", "CANOPY PLAN"]
    ("V3 A6[A6.01] = FLOOR PATTERN PLAN",       "A6", "A6.01", None, "FLOOR PATTERN PLAN"),
    ("V3 A6[A6.02] = ENLARGED TOILET PLAN",     "A6", "A6.02", None, "ENLARGED TOILET PLAN"),
    ("V3 A6[A6.03] = CANOPY PLAN",              "A6", "A6.03", None, "CANOPY PLAN"),
    ("V3 A6[A6.04] = CUSTOM SHEET",             "A6", "A6.04", None, "CUSTOM SHEET"),

    # A7
    ("V3 A7[A7.01] = ENLARGED STAIR PLAN",      "A7", "A7.01", None, "ENLARGED STAIR PLAN"),
    ("V3 A7[A7.04] = ENLARGED LIFT PLAN",       "A7", "A7.04", None, "ENLARGED LIFT PLAN"),
    ("V3 A7[A7.05] = CUSTOM SHEET",             "A7", "A7.05", None, "CUSTOM SHEET"),

    # A8
    ("V3 A8[A8.01] = DOOR SCHEDULE",            "A8", "A8.01", None, "DOOR SCHEDULE"),
    ("V3 A8[A8.02] = WINDOW SCHEDULE",          "A8", "A8.02", None, "WINDOW SCHEDULE"),
]

PARSE_CASES_V3 = [
    # _parse_slot recognises the dotted format up front, returns (slot_int, category)
    ("V3 parse A0.00",  "A0.00",  (0, "A0")),
    ("V3 parse A0.05",  "A0.05",  (5, "A0")),
    ("V3 parse A1.03",  "A1.03",  (3, "A1")),
    ("V3 parse A1.99",  "A1.99",  (99, "A1")),
    ("V3 parse A5.01",  "A5.01",  (1, "A5")),
    ("V3 parse A9.10",  "A9.10",  (10, "A9")),
    ("V3 parse X0.02",  "X0.02",  (2, "X0")),
    # 'NUM - NAME' wrappers aren't part of _parse_slot (caller strips first),
    # but still useful to confirm raw dotted input round-trips.
    ("V3 parse rejects bare '1010' as iso, not dotted",
        "1010", (1010, "A1")),  # falls through to numeric path
]


# ============================================================================
# SCHEME-DETECTION TESTS
#
# Exercises _detect_scheme_from_sheets directly + resolve_scheme_for_request
# against a fake request object so the priority order (auto-detect → override
# → default) is verified.
# ============================================================================

DETECT_CASES = [
    # (description, sheets list, expected scheme name or None)
    ("Empty list → None (caller falls back)",         [],                                  None),
    ("All 4-digit numbers → iso19650_4digit",          ["1010", "1020", "5010"],            "iso19650_4digit"),
    ("All 5-digit numbers → iso19650_5digit",          ["10100", "10200", "50100"],         "iso19650_5digit"),
    ("Mixed 4+5-digit → iso19650_5digit (decisive)",   ["1010", "10100"],                   "iso19650_5digit"),
    ("4-digit with 'NUM - NAME' shape",                ["1010 - LEVEL 1", "1020 - LEVEL 2"], "iso19650_4digit"),
    ("5-digit with 'NUM - NAME' shape",                ["10100 - LEVEL 1"],                 "iso19650_5digit"),
    ("X-series 4-digit (X010)",                        ["X010", "X020"],                    "iso19650_4digit"),
    ("X-series 5-digit (X0100)",                       ["X0100", "X0200"],                  "iso19650_5digit"),
    ("Only non-A49 names → None (cache uninformative)",["My Sheet", "Untitled"],            None),
    ("Mix of A49 + garbage → ignores garbage",         ["My Sheet", "1010"],                "iso19650_4digit"),
    ("5-digit shape mixed with garbage",               ["My Sheet", "10100"],               "iso19650_5digit"),
    ("Cover slot 4-digit ('0000')",                    ["0000"],                            "iso19650_4digit"),
    ("Cover slot 5-digit ('00000')",                   ["00000"],                           "iso19650_5digit"),
    # a49_dotted (set_v3) detection
    ("Dotted A1.03 → a49_dotted",                      ["A1.03"],                           "a49_dotted"),
    ("Dotted A0.00 (cover) → a49_dotted",              ["A0.00"],                           "a49_dotted"),
    ("Dotted with 'NUM - NAME' shape",                 ["A1.03 - 1ST FLOOR PLAN"],          "a49_dotted"),
    ("Dotted X0.02 → a49_dotted",                      ["X0.02"],                           "a49_dotted"),
    ("Dotted wins over iso (mixed project, set_v3 first)", ["A1.03", "1010"],               "a49_dotted"),
    ("Dotted wins over iso (5-digit also present)",    ["A1.03", "10100"],                  "a49_dotted"),
]


class _FakeSession(dict):
    """Stand-in for Django session — supports .get() (already a dict) and the
    attribute-like access pattern used by resolve_scheme_for_request."""
    pass


class _FakeRequest:
    def __init__(self, session_dict=None):
        self.session = _FakeSession(session_dict or {})


RESOLVE_CASES = [
    # (description, session dict, expected scheme name)
    ("No request → default iso19650_4digit",
        None, "iso19650_4digit"),
    ("Empty session, no override → default iso19650_4digit",
        {}, "iso19650_4digit"),
    ("Empty session + override 5-digit → 5-digit (override wins for empty)",
        {"ai_numbering_scheme": "iso19650_5digit"}, "iso19650_5digit"),
    ("Empty session + override 4-digit → 4-digit",
        {"ai_numbering_scheme": "iso19650_4digit"}, "iso19650_4digit"),
    ("4-digit sheets cached, no override → 4-digit",
        {"ai_last_known_sheets": ["1010", "1020"]}, "iso19650_4digit"),
    ("5-digit sheets cached, no override → 5-digit",
        {"ai_last_known_sheets": ["10100", "10200"]}, "iso19650_5digit"),
    ("5-digit cached + override 4-digit → 5-digit (auto-detect wins)",
        {"ai_last_known_sheets": ["10100"], "ai_numbering_scheme": "iso19650_4digit"}, "iso19650_5digit"),
    ("4-digit cached + override 5-digit → 4-digit (auto-detect wins)",
        {"ai_last_known_sheets": ["1010"], "ai_numbering_scheme": "iso19650_5digit"}, "iso19650_4digit"),
    ("Garbage-only cached + override 5-digit → 5-digit (cache uninformative)",
        {"ai_last_known_sheets": ["My Sheet"], "ai_numbering_scheme": "iso19650_5digit"}, "iso19650_5digit"),
    ("Invalid override value → falls back to default 4-digit",
        {"ai_numbering_scheme": "v3_unknown"}, "iso19650_4digit"),

    # ── Legacy session-key migration ───────────────────────────────────
    # Sessions stored before the v1_small/v2_large → iso19650_* rename.
    # Migration shim in resolve_scheme_for_request rewrites them in place.
    ("Legacy override 'v1_small' → migrated to iso19650_4digit",
        {"ai_numbering_scheme": "v1_small"}, "iso19650_4digit"),
    ("Legacy override 'v2_large' → migrated to iso19650_5digit",
        {"ai_numbering_scheme": "v2_large"}, "iso19650_5digit"),
    ("Legacy override + auto-detect: cached 4-digit beats legacy 'v2_large'",
        {"ai_last_known_sheets": ["1010"], "ai_numbering_scheme": "v2_large"}, "iso19650_4digit"),

    # ── a49_dotted (set_v3) resolution ─────────────────────────────────
    ("Empty session + override a49_dotted → a49_dotted",
        {"ai_numbering_scheme": "a49_dotted"}, "a49_dotted"),
    ("Dotted sheets cached, no override → a49_dotted",
        {"ai_last_known_sheets": ["A1.03"]}, "a49_dotted"),
    ("Dotted cached + override 5-digit → a49_dotted (auto-detect wins)",
        {"ai_last_known_sheets": ["A1.03"], "ai_numbering_scheme": "iso19650_5digit"}, "a49_dotted"),
    ("Mixed cache (dotted + 4-digit) → a49_dotted (decisive)",
        {"ai_last_known_sheets": ["A1.03", "1010"]}, "a49_dotted"),
]


# ── HARNESS ───────────────────────────────────────────────────────────────

def _run():
    passed, failed = 0, 0
    failures = []

    # ─── V1 (default scheme) cases ─────────────────────────────────────
    # Don't pass scheme= so we exercise the get_active_scheme() default path.

    for desc, st, existing, expected in SEQUENCE_CASES:
        actual = get_next_sheet_number(st, existing)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[seq]  {desc}", "input": (st, existing),
                             "actual": actual, "expected": expected})

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

    for desc, st, num, lvl, expected in NAME_CASES:
        actual = generate_smart_name(st, sheet_number=num, level_name=lvl)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[name] {desc}", "input": (st, num, lvl),
                             "actual": actual, "expected": expected})

    for st, expected in SHEET_SET_CASES:
        actual = SHEET_SET_MAP.get(st)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[set]  {st}", "input": st,
                             "actual": actual, "expected": expected})

    for case in PAYLOAD_CASES:
        existing = []
        sheets = build_sheets_payload(case["request"], existing)
        actual_numbers = [s.get("sheet_number") for s in sheets]
        if actual_numbers == case["expected_numbers"]:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[bld]  {case['desc']}",
                             "input": case["request"]["levels"],
                             "actual": actual_numbers,
                             "expected": case["expected_numbers"]})

    # ─── V2 (large) cases ───────────────────────────────────────────────
    # Pass scheme=V2 explicitly to exercise the iso19650_5digit path while keeping
    # the active scheme default unchanged for the rest of the codebase.

    for desc, st, existing, expected in SEQUENCE_CASES_V2:
        actual = get_next_sheet_number(st, existing, scheme=V2)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[seq]  {desc}", "input": (st, existing),
                             "actual": actual, "expected": expected})

    for desc, st, lvl, project_levels, existing, expected in LEVEL_CASES_V2:
        actual = get_next_sheet_number(st, existing, level_name=lvl,
                                       project_levels=project_levels, scheme=V2)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[lvl]  {desc}",
                             "input": (st, lvl, project_levels, existing),
                             "actual": actual, "expected": expected})

    for desc, st, num, lvl, expected in NAME_CASES_V2:
        actual = generate_smart_name(st, sheet_number=num, level_name=lvl, scheme=V2)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[name] {desc}", "input": (st, num, lvl),
                             "actual": actual, "expected": expected})

    for case in PAYLOAD_CASES_V2:
        existing = []
        sheets = build_sheets_payload(case["request"], existing, scheme=V2)
        actual_numbers = [s.get("sheet_number") for s in sheets]
        if actual_numbers == case["expected_numbers"]:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[bld]  {case['desc']}",
                             "input": case["request"]["levels"],
                             "actual": actual_numbers,
                             "expected": case["expected_numbers"]})

    # ─── V3 (a49_dotted) cases ──────────────────────────────────────────
    # Pass scheme=V3 explicitly to exercise the a49_dotted path.

    for desc, st, existing, expected in SEQUENCE_CASES_V3:
        actual = get_next_sheet_number(st, existing, scheme=V3)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[seq]  {desc}", "input": (st, existing),
                             "actual": actual, "expected": expected})

    for desc, st, lvl, project_levels, existing, expected in LEVEL_SEQUENCE_CASES_V3:
        actual = get_next_sheet_number(st, existing, level_name=lvl,
                                       project_levels=project_levels, scheme=V3)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[lvl]  {desc}",
                             "input": (st, lvl, project_levels, existing),
                             "actual": actual, "expected": expected})

    for desc, st, num, lvl, expected in NAME_CASES_V3:
        actual = generate_smart_name(st, sheet_number=num, level_name=lvl, scheme=V3)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[name] {desc}", "input": (st, num, lvl),
                             "actual": actual, "expected": expected})

    # _parse_slot dotted-format recognition
    for desc, sheet_num, expected in PARSE_CASES_V3:
        actual = _parse_slot(sheet_num)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[parse] {desc}", "input": sheet_num,
                             "actual": actual, "expected": expected})

    # ─── Scheme-detection unit cases ────────────────────────────────────
    for desc, sheets, expected in DETECT_CASES:
        actual = _detect_scheme_from_sheets(sheets)
        if actual == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[detect] {desc}", "input": sheets,
                             "actual": actual, "expected": expected})

    # ─── Per-request scheme resolution ──────────────────────────────────
    for desc, sess, expected_name in RESOLVE_CASES:
        req = _FakeRequest(sess) if sess is not None else None
        actual_scheme = resolve_scheme_for_request(req)
        # Map the returned scheme dict back to its name for assertion clarity.
        actual_name = next(
            (name for name, cfg in SCHEMES.items() if cfg is actual_scheme),
            "UNKNOWN")
        if actual_name == expected_name:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[resolve] {desc}", "input": sess,
                             "actual": actual_name, "expected": expected_name})

    total = (len(SEQUENCE_CASES) + len(LEVEL_CASES) +
             len(NAME_CASES) + len(SHEET_SET_CASES) + len(PAYLOAD_CASES) +
             len(SEQUENCE_CASES_V2) + len(LEVEL_CASES_V2) +
             len(NAME_CASES_V2) + len(PAYLOAD_CASES_V2) +
             len(SEQUENCE_CASES_V3) + len(LEVEL_SEQUENCE_CASES_V3) +
             len(NAME_CASES_V3) + len(PARSE_CASES_V3) +
             len(DETECT_CASES) + len(RESOLVE_CASES))
    print("=" * 70)
    print(f"sheet_numbering tests: {passed} passed, {failed} failed  (of {total})")
    print("  V1: {} cases, V2: {} cases, V3: {} cases, scheme-detect: {} cases".format(
        len(SEQUENCE_CASES) + len(LEVEL_CASES) + len(NAME_CASES) + len(SHEET_SET_CASES) + len(PAYLOAD_CASES),
        len(SEQUENCE_CASES_V2) + len(LEVEL_CASES_V2) + len(NAME_CASES_V2) + len(PAYLOAD_CASES_V2),
        len(SEQUENCE_CASES_V3) + len(LEVEL_SEQUENCE_CASES_V3) + len(NAME_CASES_V3) + len(PARSE_CASES_V3),
        len(DETECT_CASES) + len(RESOLVE_CASES),
    ))
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
