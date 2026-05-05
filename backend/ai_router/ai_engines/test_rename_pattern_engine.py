# ============================================================================
# test_rename_pattern_engine.py — verify each pattern operation + the
# end-to-end preview/update pipeline.
#
# Run standalone:
#   cd backend
#   python -m ai_router.ai_engines.test_rename_pattern_engine
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

from ai_router.ai_engines.rename_pattern_engine import (
    apply_operation,
    compute_rename_preview,
    preview_to_updates,
    list_operations,
    _changes_only,
)


# ── Inventory fixtures (shape mirrors FetchProjectInventoryCommand output) ──

def _sheet(uid, number, name="LEVEL X FLOOR PLAN", category="A1", stage="CD"):
    return {
        "unique_id": uid,
        "number":    number,
        "name":      name,
        "category":  category,
        "stage":     stage,
    }


# Sample mixed-scheme inventory used across many tests.
INVENTORY_MIXED = [
    _sheet("u1", "1010", "LEVEL 1 FLOOR PLAN"),
    _sheet("u2", "1020", "LEVEL 2 FLOOR PLAN"),
    _sheet("u3", "1009", "LEVEL B1 FLOOR PLAN"),
    _sheet("u4", "1000", "SITE PLAN"),
    _sheet("u5", "0000", "COVER", category="A0"),
    _sheet("u6", "X010", "CUSTOM SHEET", category="X0"),
]


# ── OPERATION CASES ─────────────────────────────────────────────────────
# Each case: (description, op_spec, item, expected_diff_dict)
# expected_diff_dict only contains fields that changed; warnings ignored
# unless explicitly asserted.

OP_CASES = [
    # ─── find_replace ────────────────────────────────────────────────
    ("find_replace: simple substitute on name",
     {"operation": "find_replace", "params": {"field": "name", "find": "FLOOR", "replace": "GROUND"}},
     _sheet("u1", "1010", "LEVEL 1 FLOOR PLAN"),
     {"name": "LEVEL 1 GROUND PLAN"}),

    ("find_replace: case-insensitive by default",
     {"operation": "find_replace", "params": {"field": "name", "find": "floor", "replace": "GROUND"}},
     _sheet("u1", "1010", "LEVEL 1 FLOOR PLAN"),
     {"name": "LEVEL 1 GROUND PLAN"}),

    ("find_replace: case-sensitive flag respected",
     {"operation": "find_replace", "params": {"field": "name", "find": "floor", "replace": "GROUND",
                                              "case_sensitive": True}},
     _sheet("u1", "1010", "LEVEL 1 FLOOR PLAN"),
     {}),  # no change — 'floor' lower != 'FLOOR'

    ("find_replace: regex replace on name",
     {"operation": "find_replace", "params": {"field": "name", "find": r"LEVEL (\d+)",
                                              "replace": r"L\1", "regex": True}},
     _sheet("u1", "1010", "LEVEL 1 FLOOR PLAN"),
     {"name": "L1 FLOOR PLAN"}),

    ("find_replace: operates on number field too",
     {"operation": "find_replace", "params": {"field": "number", "find": "10", "replace": "20"}},
     _sheet("u1", "1010", "LEVEL 1 FLOOR PLAN"),
     {"number": "2020"}),  # 10 → 20 in both positions

    ("find_replace: empty find string warns and no-ops",
     {"operation": "find_replace", "params": {"field": "name", "find": "", "replace": "XX"}},
     _sheet("u1", "1010", "LEVEL 1 FLOOR PLAN"),
     {}),

    # ─── case_transform ──────────────────────────────────────────────
    ("case_transform: upper",
     {"operation": "case_transform", "params": {"field": "name", "transform": "upper"}},
     _sheet("u1", "1010", "Level 1 Floor Plan"),
     {"name": "LEVEL 1 FLOOR PLAN"}),

    ("case_transform: lower",
     {"operation": "case_transform", "params": {"field": "name", "transform": "lower"}},
     _sheet("u1", "1010", "LEVEL 1 FLOOR PLAN"),
     {"name": "level 1 floor plan"}),

    ("case_transform: title",
     {"operation": "case_transform", "params": {"field": "name", "transform": "title"}},
     _sheet("u1", "1010", "level 1 floor plan"),
     {"name": "Level 1 Floor Plan"}),

    # ─── prefix_suffix ───────────────────────────────────────────────
    ("prefix_suffix: add prefix",
     {"operation": "prefix_suffix", "params": {"field": "name", "add_prefix": "ARCH-"}},
     _sheet("u1", "1010", "LEVEL 1 FLOOR PLAN"),
     {"name": "ARCH-LEVEL 1 FLOOR PLAN"}),

    ("prefix_suffix: add suffix",
     {"operation": "prefix_suffix", "params": {"field": "name", "add_suffix": " (R0)"}},
     _sheet("u1", "1010", "LEVEL 1 FLOOR PLAN"),
     {"name": "LEVEL 1 FLOOR PLAN (R0)"}),

    ("prefix_suffix: strip prefix",
     {"operation": "prefix_suffix", "params": {"field": "name", "strip_prefix": "OLD-"}},
     _sheet("u1", "1010", "OLD-LEVEL 1 FLOOR PLAN"),
     {"name": "LEVEL 1 FLOOR PLAN"}),

    ("prefix_suffix: strip + add together",
     {"operation": "prefix_suffix", "params": {"field": "name", "strip_prefix": "OLD-",
                                               "add_prefix": "NEW-"}},
     _sheet("u1", "1010", "OLD-LEVEL 1 FLOOR PLAN"),
     {"name": "NEW-LEVEL 1 FLOOR PLAN"}),

    # ─── translate (uses bilingual_dictionary) ───────────────────────
    ("translate_en_to_th: LEVEL 1 FLOOR PLAN → ผังพื้นชั้นที่ 1",
     {"operation": "translate_en_to_th"},
     _sheet("u1", "1010", "LEVEL 1 FLOOR PLAN"),
     {"name": "ผังพื้นชั้นที่ 1"}),

    ("translate_en_to_th: SITE PLAN → ผังบริเวณ",
     {"operation": "translate_en_to_th"},
     _sheet("u4", "1000", "SITE PLAN"),
     {"name": "ผังบริเวณ"}),

    ("translate_th_to_en: ผังพื้นชั้นที่ 1 → LEVEL 1 FLOOR PLAN",
     {"operation": "translate_th_to_en"},
     _sheet("u1", "1010", "ผังพื้นชั้นที่ 1"),
     {"name": "LEVEL 1 FLOOR PLAN"}),

    # ─── add_stage_prefix ────────────────────────────────────────────
    ("add_stage_prefix: uses item.stage by default",
     {"operation": "add_stage_prefix"},
     _sheet("u1", "1010", "LEVEL 1 FLOOR PLAN", stage="CD"),
     {"name": "CD - LEVEL 1 FLOOR PLAN"}),

    ("add_stage_prefix: explicit stage param overrides item",
     {"operation": "add_stage_prefix", "params": {"stage": "DD"}},
     _sheet("u1", "1010", "LEVEL 1 FLOOR PLAN", stage="CD"),
     {"name": "DD - LEVEL 1 FLOOR PLAN"}),

    ("add_stage_prefix: strip existing CD - prefix when adding DD",
     {"operation": "add_stage_prefix", "params": {"stage": "DD"}},
     _sheet("u1", "1010", "CD - LEVEL 1 FLOOR PLAN", stage="DD"),
     {"name": "DD - LEVEL 1 FLOOR PLAN"}),

    ("add_stage_prefix: idempotent (already has correct prefix)",
     {"operation": "add_stage_prefix", "params": {"stage": "CD"}},
     _sheet("u1", "1010", "CD - LEVEL 1 FLOOR PLAN", stage="CD"),
     {}),

    ("add_stage_prefix: custom separator",
     {"operation": "add_stage_prefix", "params": {"stage": "CD", "separator": "_"}},
     _sheet("u1", "1010", "LEVEL 1 FLOOR PLAN", stage="CD"),
     {"name": "CD_LEVEL 1 FLOOR PLAN"}),

    ("add_stage_prefix: strip_existing=False keeps old stage",
     {"operation": "add_stage_prefix", "params": {"stage": "CD", "strip_existing": False}},
     _sheet("u1", "1010", "DD - LEVEL 1 FLOOR PLAN", stage="CD"),
     {"name": "CD - DD - LEVEL 1 FLOOR PLAN"}),

    # ─── offset_renumber ─────────────────────────────────────────────
    ("offset_renumber: +1000 on iso19650 4-digit",
     {"operation": "offset_renumber", "params": {"delta": 1000}},
     _sheet("u1", "1010"),
     {"number": "2010"}),

    ("offset_renumber: -10 on iso19650 4-digit",
     {"operation": "offset_renumber", "params": {"delta": -10}},
     _sheet("u1", "1020"),
     {"number": "1010"}),

    ("offset_renumber: preserves digit width",
     {"operation": "offset_renumber", "params": {"delta": -1000}},
     _sheet("u1", "1010"),
     {"number": "0010"}),  # not "10"

    ("offset_renumber: handles X-prefix",
     {"operation": "offset_renumber", "params": {"delta": 10}},
     _sheet("u6", "X010", "CUSTOM SHEET", category="X0"),
     {"number": "X020"}),

    ("offset_renumber: skip dotted with warning",
     {"operation": "offset_renumber", "params": {"delta": 10}},
     _sheet("u1", "A1.03", "1ST FLOOR PLAN"),
     {}),  # no diff — see warnings test below

    ("offset_renumber: zero delta no-op",
     {"operation": "offset_renumber", "params": {"delta": 0}},
     _sheet("u1", "1010"),
     {}),

    # ─── scheme_convert ──────────────────────────────────────────────
    ("scheme_convert: v1 1010 → v2 10100 (L1)",
     {"operation": "scheme_convert", "params": {"from_scheme": "iso19650_4digit",
                                                "to_scheme": "iso19650_5digit"}},
     _sheet("u1", "1010"),
     {"number": "10100"}),

    ("scheme_convert: v1 1009 → v2 10090 (B1)",
     {"operation": "scheme_convert", "params": {"from_scheme": "iso19650_4digit",
                                                "to_scheme": "iso19650_5digit"}},
     _sheet("u3", "1009"),
     {"number": "10090"}),

    ("scheme_convert: v1 1000 → v2 10000 (SITE)",
     {"operation": "scheme_convert", "params": {"from_scheme": "iso19650_4digit",
                                                "to_scheme": "iso19650_5digit"}},
     _sheet("u4", "1000"),
     {"number": "10000"}),

    ("scheme_convert: v1 1011 → v2 10110 (L1M sub-slot)",
     {"operation": "scheme_convert", "params": {"from_scheme": "iso19650_4digit",
                                                "to_scheme": "iso19650_5digit"}},
     _sheet("u1", "1011"),
     {"number": "10110"}),

    ("scheme_convert: v1 0000 → v2 00000 (cover)",
     {"operation": "scheme_convert", "params": {"from_scheme": "iso19650_4digit",
                                                "to_scheme": "iso19650_5digit"}},
     _sheet("u5", "0000"),
     {"number": "00000"}),

    ("scheme_convert: v1 X010 → v2 X0100",
     {"operation": "scheme_convert", "params": {"from_scheme": "iso19650_4digit",
                                                "to_scheme": "iso19650_5digit"}},
     _sheet("u6", "X010", category="X0"),
     {"number": "X0100"}),

    ("scheme_convert: v2 → v1 (reverse, lossy by design)",
     {"operation": "scheme_convert", "params": {"from_scheme": "iso19650_5digit",
                                                "to_scheme": "iso19650_4digit"}},
     _sheet("u1", "10100"),
     {"number": "1010"}),

    ("scheme_convert: v1 → a49_dotted (A1.01 from 1010)",
     {"operation": "scheme_convert", "params": {"from_scheme": "iso19650_4digit",
                                                "to_scheme": "a49_dotted"}},
     _sheet("u1", "1010"),
     {"number": "A1.01"}),

    ("scheme_convert: same scheme → no-op",
     {"operation": "scheme_convert", "params": {"from_scheme": "iso19650_4digit",
                                                "to_scheme": "iso19650_4digit"}},
     _sheet("u1", "1010"),
     {}),

    ("scheme_convert: unknown scheme warns",
     {"operation": "scheme_convert", "params": {"from_scheme": "fake_scheme",
                                                "to_scheme": "iso19650_5digit"}},
     _sheet("u1", "1010"),
     {}),  # no diff, just warning

    # ─── unknown operation ───────────────────────────────────────────
    ("unknown operation: graceful warning, no diff",
     {"operation": "no_such_op", "params": {}},
     _sheet("u1", "1010"),
     {}),
]


# ── WARNING-EMITTING CASES (just check that warnings list is non-empty) ──
WARNING_CASES = [
    ("offset_renumber on dotted",
     {"operation": "offset_renumber", "params": {"delta": 10}},
     _sheet("u1", "A1.03"), True),
    ("scheme_convert unknown scheme",
     {"operation": "scheme_convert", "params": {"from_scheme": "fake",
                                                "to_scheme": "iso19650_5digit"}},
     _sheet("u1", "1010"), True),
    ("add_stage_prefix without stage info",
     {"operation": "add_stage_prefix"},
     {"unique_id": "u9", "number": "1010", "name": "Foo", "category": "A1"},  # no stage
     True),
    ("find_replace with empty find",
     {"operation": "find_replace", "params": {"find": "", "replace": "X"}},
     _sheet("u1", "1010"), True),
    ("normal find_replace — no warnings",
     {"operation": "find_replace", "params": {"find": "FLOOR", "replace": "GROUND"}},
     _sheet("u1", "1010", "LEVEL 1 FLOOR PLAN"), False),
]


# ── PREVIEW PIPELINE CASES ──────────────────────────────────────────────
# End-to-end: inventory + op_spec → preview rows → updates list

PREVIEW_CASES = [
    {
        "desc": "v1→v2 across mixed inventory: all sheets get scaled",
        "inventory": INVENTORY_MIXED,
        "operation": {"operation": "scheme_convert",
                      "params": {"from_scheme": "iso19650_4digit",
                                 "to_scheme": "iso19650_5digit"}},
        "selection": None,  # all
        "deselected": None,
        "expected_changes": {
            "u1": {"number": "10100"},
            "u2": {"number": "10200"},
            "u3": {"number": "10090"},
            "u4": {"number": "10000"},
            "u5": {"number": "00000"},
            "u6": {"number": "X0100"},
        },
    },
    {
        "desc": "selection limits which items are previewed",
        "inventory": INVENTORY_MIXED,
        "operation": {"operation": "scheme_convert",
                      "params": {"from_scheme": "iso19650_4digit",
                                 "to_scheme": "iso19650_5digit"}},
        "selection": ["u1", "u2"],
        "deselected": None,
        "expected_changes": {
            "u1": {"number": "10100"},
            "u2": {"number": "10200"},
        },
    },
    {
        "desc": "deselection drops rows from final updates",
        "inventory": INVENTORY_MIXED,
        "operation": {"operation": "scheme_convert",
                      "params": {"from_scheme": "iso19650_4digit",
                                 "to_scheme": "iso19650_5digit"}},
        "selection": None,
        "deselected": ["u3", "u4", "u5", "u6"],
        "expected_changes": {
            "u1": {"number": "10100"},
            "u2": {"number": "10200"},
        },
    },
    {
        "desc": "EN→TH translation across mixed inventory",
        "inventory": INVENTORY_MIXED,
        "operation": {"operation": "translate_en_to_th"},
        "selection": None,
        "deselected": None,
        "expected_changes": {
            "u1": {"name": "ผังพื้นชั้นที่ 1"},
            "u2": {"name": "ผังพื้นชั้นที่ 2"},
            "u3": {"name": "ผังพื้นชั้น B1"},
            "u4": {"name": "ผังบริเวณ"},
            "u5": {"name": "ปก"},
            # u6 "CUSTOM SHEET" — has no template/term match, skipped
        },
    },
    {
        "desc": "add_stage_prefix CD across all sheets in a CD project",
        "inventory": INVENTORY_MIXED,
        "operation": {"operation": "add_stage_prefix"},
        "selection": None,
        "deselected": None,
        "expected_changes": {
            "u1": {"name": "CD - LEVEL 1 FLOOR PLAN"},
            "u2": {"name": "CD - LEVEL 2 FLOOR PLAN"},
            "u3": {"name": "CD - LEVEL B1 FLOOR PLAN"},
            "u4": {"name": "CD - SITE PLAN"},
            "u5": {"name": "CD - COVER"},
            "u6": {"name": "CD - CUSTOM SHEET"},
        },
    },
]


# ── HARNESS ──────────────────────────────────────────────────────────────

def _run():
    passed, failed = 0, 0
    failures = []

    # Per-operation cases
    for desc, op_spec, item, expected in OP_CASES:
        result = apply_operation(item, op_spec)
        actual_diff = _changes_only(item, result)
        if actual_diff == expected:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[op]      {desc}", "input": item,
                             "actual": actual_diff, "expected": expected})

    # Warning emission cases
    for desc, op_spec, item, should_warn in WARNING_CASES:
        result = apply_operation(item, op_spec)
        warnings = result.get("warnings", [])
        actual_warned = bool(warnings)
        if actual_warned == should_warn:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[warn]    {desc}",
                             "input": item,
                             "actual": f"warned={actual_warned} ({warnings!r})",
                             "expected": f"warned={should_warn}"})

    # End-to-end preview pipeline cases
    for case in PREVIEW_CASES:
        rows = compute_rename_preview(case["inventory"], case["operation"],
                                      selection=case.get("selection"))
        updates = preview_to_updates(rows, deselected_ids=case.get("deselected"))
        actual_changes = {u["unique_id"]: u["changes"] for u in updates}
        if actual_changes == case["expected_changes"]:
            passed += 1
        else:
            failed += 1
            failures.append({"case": f"[preview] {case['desc']}",
                             "input": case["operation"],
                             "actual": actual_changes,
                             "expected": case["expected_changes"]})

    # Operations registry sanity
    expected_ops = {
        "find_replace", "case_transform", "prefix_suffix",
        "translate_en_to_th", "translate_th_to_en", "add_stage_prefix",
        "offset_renumber", "scheme_convert",
    }
    actual_ops = set(list_operations())
    if actual_ops == expected_ops:
        passed += 1
    else:
        failed += 1
        failures.append({"case": "[registry] list_operations() returns expected set",
                         "input": None,
                         "actual": actual_ops, "expected": expected_ops})

    total = len(OP_CASES) + len(WARNING_CASES) + len(PREVIEW_CASES) + 1
    print("=" * 70)
    print(f"rename_pattern_engine tests: {passed} passed, {failed} failed  (of {total})")
    print(f"  ops: {len(OP_CASES)} · warnings: {len(WARNING_CASES)} · "
          f"preview pipeline: {len(PREVIEW_CASES)} · registry: 1")
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
