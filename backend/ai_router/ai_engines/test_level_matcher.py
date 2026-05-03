# ======================================================================
# test_level_matcher.py — Self-contained sanity tests for the level
# resolution pipeline (level_engine.parse_levels + level_matcher).
#
# Covers all three A49 office naming conventions documented by Ben:
#   - BASE_OPTION:   "LEVEL 7T", "ROOF LEVEL", "LEVEL B1M", "SITE"
#   - OPTION_1_EN:   "+45.45 LEVEL 7T", "+52.85 ROOF LEVEL", "-2.42 LEVEL B1M"
#   - OPTION_2_TH:   "+5.50 ระดับพื้นชั้น 2", "+32.30 ระดับพื้นชั้นดาดฟ้า",
#                    "+0.00 ระดับพื้นดิน", "-2.42 ระดับชั้นใต้ดิน B1M"
#
# Run standalone:
#   cd backend
#   python -m ai_router.ai_engines.test_level_matcher
#
# A failing assertion prints the expected vs actual mismatch and exits
# with a non-zero status. A passing run prints a summary + exits 0.
# ======================================================================

import sys
import os

# Force UTF-8 stdout on Windows so DEBUG prints inside level_engine.py
# (which contain Thai characters when parsing project names) don't crash
# under the default cp1252 console encoding.
if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        pass

# Make the module runnable both via `python -m ai_router.ai_engines.test_level_matcher`
# AND via direct `python test_level_matcher.py` from the engines directory.
if __name__ == "__main__" and __package__ is None:
    sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))

from ai_router.ai_engines.level_engine import parse_levels
from ai_router.ai_engines.level_matcher import (
    extract_level_signature,
    resolve_tokens_to_project_levels,
)
from ai_router.ai_engines.naming_engine import level_to_code


# ── PROJECT FIXTURES ──────────────────────────────────────────────────

BASE_OPTION = [
    "ROOF LEVEL", "LEVEL 8", "LEVEL 7T", "LEVEL 7", "LEVEL 6M", "LEVEL 6",
    "LEVEL 5", "LEVEL 4", "LEVEL 3", "LEVEL 2", "LEVEL 1",
    "SITE",
    "LEVEL B1M", "LEVEL B1", "LEVEL B2",
]

OPTION_1_EN = [
    "+52.85 ROOF LEVEL",
    "+49.25 LEVEL 8",
    "+45.45 LEVEL 7T",
    "+38.95 LEVEL 7",
    "+35.95 LEVEL 6M",
    "+28.45 LEVEL 6",
    "+25.20 LEVEL 5",
    "+19.70 LEVEL 4",
    "+14.20 LEVEL 3",
    "+7.70 LEVEL 2",
    "+1.20 LEVEL 1",
    "-2.42 LEVEL B1M",
    "-5.82 LEVEL B1",
    "-9.22 LEVEL B2",
]

OPTION_2_TH = [
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
    "-2.42 ระดับชั้นใต้ดิน B1M",
    "-5.82 ระดับชั้นใต้ดิน B1",
    "-9.22 ระดับชั้นใต้ดิน B2",
]


# ── TEST CASES ────────────────────────────────────────────────────────
# Each tuple: (description, project_levels, user_input, expected_resolved_names)
# expected = list of strings that resolve_tokens_to_project_levels should output.

CASES = [
    # ── BASE OPTION ──────────────────────────────────────────────────
    ("Base · L2 plain",            BASE_OPTION, "LEVEL 2",                     ["LEVEL 2"]),
    ("Base · L2 short",            BASE_OPTION, "L2",                          ["LEVEL 2"]),
    ("Base · L7T (transfer)",      BASE_OPTION, "L7T",                         ["LEVEL 7T"]),
    ("Base · L6M (mezz)",          BASE_OPTION, "level 6M",                    ["LEVEL 6M"]),
    ("Base · ROOF",                BASE_OPTION, "roof",                        ["ROOF LEVEL"]),
    ("Base · SITE",                BASE_OPTION, "site",                        ["SITE"]),
    ("Base · B1",                  BASE_OPTION, "B1",                          ["LEVEL B1"]),
    ("Base · B1M",                 BASE_OPTION, "B1M",                         ["LEVEL B1M"]),
    ("Base · level B2",            BASE_OPTION, "level B2",                    ["LEVEL B2"]),
    ("Base · multi list",          BASE_OPTION, "LEVEL 1, LEVEL 2, LEVEL 3",   ["LEVEL 1", "LEVEL 2", "LEVEL 3"]),
    ("Base · mixed list",          BASE_OPTION, "L1, B1, ROOF",                ["LEVEL 1", "LEVEL B1", "ROOF LEVEL"]),

    # ── OPTION 1 (English with elevation prefix) ─────────────────────
    ("Opt1 · L2 short → elev",     OPTION_1_EN, "L2",                          ["+7.70 LEVEL 2"]),
    ("Opt1 · L7T → elev",          OPTION_1_EN, "L7T",                         ["+45.45 LEVEL 7T"]),
    ("Opt1 · L6M → elev",          OPTION_1_EN, "level 6m",                    ["+35.95 LEVEL 6M"]),
    ("Opt1 · ROOF → elev",         OPTION_1_EN, "roof",                        ["+52.85 ROOF LEVEL"]),
    ("Opt1 · B1 → elev",           OPTION_1_EN, "B1",                          ["-5.82 LEVEL B1"]),
    ("Opt1 · B1M → elev",          OPTION_1_EN, "B1M",                         ["-2.42 LEVEL B1M"]),
    ("Opt1 · ranged L1-L3",        OPTION_1_EN, "L1-L3",                       ["+1.20 LEVEL 1", "+7.70 LEVEL 2", "+14.20 LEVEL 3"]),

    # ── OPTION 2 (Thai with elevation prefix) ────────────────────────
    ("Opt2 · L2 EN → Thai",        OPTION_2_TH, "L2",                          ["+5.50 ระดับพื้นชั้น 2"]),
    ("Opt2 · level 2 EN → Thai",   OPTION_2_TH, "level 2",                     ["+5.50 ระดับพื้นชั้น 2"]),
    ("Opt2 · ชั้น 2",              OPTION_2_TH, "ชั้น 2",                       ["+5.50 ระดับพื้นชั้น 2"]),
    ("Opt2 · ชั้นที่ 2",           OPTION_2_TH, "ชั้นที่ 2",                    ["+5.50 ระดับพื้นชั้น 2"]),
    ("Opt2 · ระดับพื้นชั้น 2",      OPTION_2_TH, "ระดับพื้นชั้น 2",              ["+5.50 ระดับพื้นชั้น 2"]),
    ("Opt2 · ROOF EN → Thai",      OPTION_2_TH, "roof",                        ["+32.30 ระดับพื้นชั้นดาดฟ้า"]),
    ("Opt2 · ดาดฟ้า",              OPTION_2_TH, "ดาดฟ้า",                      ["+32.30 ระดับพื้นชั้นดาดฟ้า"]),
    ("Opt2 · TOP EN → Thai",       OPTION_2_TH, "top of building",             ["+37.30 ระดับสูงสุดของอาคาร"]),
    ("Opt2 · สูงสุด",              OPTION_2_TH, "ระดับสูงสุดของอาคาร",          ["+37.30 ระดับสูงสุดของอาคาร"]),
    ("Opt2 · SITE EN → Thai",      OPTION_2_TH, "site",                        ["+0.00 ระดับพื้นดิน"]),
    ("Opt2 · พื้นดิน",             OPTION_2_TH, "พื้นดิน",                     ["+0.00 ระดับพื้นดิน"]),
    ("Opt2 · B1 EN → Thai",        OPTION_2_TH, "B1",                          ["-5.82 ระดับชั้นใต้ดิน B1"]),
    ("Opt2 · B1M EN → Thai",       OPTION_2_TH, "B1M",                         ["-2.42 ระดับชั้นใต้ดิน B1M"]),
    ("Opt2 · ระดับชั้นใต้ดิน B1",   OPTION_2_TH, "ระดับชั้นใต้ดิน B1",           ["-5.82 ระดับชั้นใต้ดิน B1"]),
    ("Opt2 · multi mix EN list",   OPTION_2_TH, "L1, L2, L3",                  ["+0.50 ระดับพื้นชั้น 1", "+5.50 ระดับพื้นชั้น 2", "+10.50 ระดับพื้นชั้น 3"]),

    # ── EDGE CASES ───────────────────────────────────────────────────
    ("Empty cache passthrough",    [],          "L2",                          ["L2"]),
    ("Token has no project match", BASE_OPTION, "L99",                         ["L99"]),  # passes through
]

# ── level_to_code TEST CASES ──────────────────────────────────────────
# (description, input_level_name, expected_code)
# Used in view names like CD_A1_FL_<code>. The resolver above produces
# the input level name; level_to_code reduces it to the suffix.

CODE_CASES = [
    # SITE (the bug Ben reported — was "00", should be "SITE")
    ("SITE token",                  "SITE",                          "SITE"),
    ("SITE EN with elev",           "+0.00 SITE",                    "SITE"),
    ("SITE Thai",                   "ระดับพื้นดิน",                   "SITE"),
    ("SITE Thai with elev",         "+0.00 ระดับพื้นดิน",             "SITE"),

    # ROOF (was broken in Thai projects — returned "32" instead of "RF")
    ("ROOF token",                  "RF",                            "RF"),
    ("ROOF EN long",                "ROOF LEVEL",                    "RF"),
    ("ROOF EN with elev",           "+52.85 ROOF LEVEL",             "RF"),
    ("ROOF Thai",                   "ระดับพื้นชั้นดาดฟ้า",            "RF"),
    ("ROOF Thai with elev",         "+32.30 ระดับพื้นชั้นดาดฟ้า",     "RF"),

    # TOP (highest point of building — new code)
    ("TOP token",                   "TOP",                           "TOP"),
    ("TOP Thai with elev",          "+37.30 ระดับสูงสุดของอาคาร",     "TOP"),

    # Above-ground numbered (zero-padded to 2 digits)
    ("L1 token",                    "L1",                            "01"),
    ("L7 EN",                       "LEVEL 7",                       "07"),
    ("L7 EN with elev",             "+38.95 LEVEL 7",                "07"),
    ("L2 Thai with elev",           "+5.50 ระดับพื้นชั้น 2",          "02"),
    ("L7T intermediate",            "LEVEL 7T",                      "07T"),
    ("L7T EN with elev",            "+45.45 LEVEL 7T",               "07T"),
    ("L6M intermediate",            "LEVEL 6M",                      "06M"),

    # Basement
    ("B1 token",                    "B1",                            "B1"),
    ("B1 EN with elev",             "-5.82 LEVEL B1",                "B1"),
    ("B1 Thai with elev",           "-5.82 ระดับชั้นใต้ดิน B1",       "B1"),
    ("B1M intermediate",            "LEVEL B1M",                     "B1M"),
    ("B1M Thai with elev",          "-2.42 ระดับชั้นใต้ดิน B1M",      "B1M"),
    ("B2",                          "LEVEL B2",                      "B2"),
]


# ── HARNESS ───────────────────────────────────────────────────────────

def _run():
    passed, failed = 0, 0
    failures = []

    # ── Resolver tests ───────────────────────────────────────────────
    for desc, project, user_input, expected in CASES:
        tokens = parse_levels(user_input)
        resolved = resolve_tokens_to_project_levels(tokens, project)
        if resolved == expected:
            passed += 1
        else:
            failed += 1
            failures.append({
                "case":     f"[resolver] {desc}",
                "input":    user_input,
                "tokens":   tokens,
                "resolved": resolved,
                "expected": expected,
            })

    # ── level_to_code tests ──────────────────────────────────────────
    for desc, level_input, expected_code in CODE_CASES:
        actual_code = level_to_code(level_input)
        if actual_code == expected_code:
            passed += 1
        else:
            failed += 1
            failures.append({
                "case":     f"[code] {desc}",
                "input":    level_input,
                "tokens":   "—",
                "resolved": actual_code,
                "expected": expected_code,
            })

    total = len(CASES) + len(CODE_CASES)
    print("=" * 70)
    print(f"level_matcher + level_to_code tests:  {passed} passed, {failed} failed  (of {total})")
    print("=" * 70)

    if failures:
        for f in failures:
            print(f"\n  ❌ {f['case']}")
            print(f"     input:    {f['input']!r}")
            print(f"     tokens:   {f['tokens']}")
            print(f"     resolved: {f['resolved']}")
            print(f"     expected: {f['expected']}")
        print()
        return 1

    print("\nAll cases pass ✓")
    return 0


if __name__ == "__main__":
    sys.exit(_run())
