# ============================================================================
# rename_pattern_engine.py — pattern-based bulk rename/renumber for sheets
# ----------------------------------------------------------------------------
# Powers the Phase 2 Renumber/Rename Wizard. Pure-function module: takes an
# inventory of sheets + an operation spec and returns a preview of changes.
# The wizard renders the preview as a diff table; user deselects unwanted
# rows, then submits the surviving updates via the existing
# `execute_batch_update` envelope (handled by ExecuteBatchUpdateCommand.cs).
#
# DESIGN PRINCIPLES
# ─────────────────
# 1. Pure functions. No I/O, no Revit calls. Inventory in, preview out.
# 2. Operation registry. Adding a new op = adding one entry to OPERATIONS.
# 3. Preview shows BOTH old and new for each field — frontend renders diff.
# 4. Warnings are per-row, not aborting. The wizard surfaces them so the user
#    can decide row-by-row.
# 5. Sheet number formats stay consistent with naming_engine.SCHEMES — uses
#    `_parse_slot` / `_format_slot` so dotted/v1/v2 are all understood.
#
# OPERATION SPEC SHAPE
# ────────────────────
#   {
#     "operation": "find_replace" | "translate_en_to_th" | "scheme_convert" | …,
#     "params":    { …op-specific… }
#   }
#
# INVENTORY ITEM SHAPE (from FetchProjectInventoryCommand.cs)
# ───────────────────────────────────────────────────────────
#   { "unique_id": "...", "number": "1010", "name": "LEVEL 1 FLOOR PLAN",
#     "category": "A1", "stage": "CD" }
#
# PREVIEW ROW SHAPE
# ─────────────────
#   { "unique_id": "...",
#     "old_number": "1010", "new_number": "10100",
#     "old_name":   "...",  "new_name":   "...",
#     "changed":    True,
#     "warnings":   [] }
# ============================================================================

import re

from .bilingual_dictionary import translate_en_to_th, translate_th_to_en
from .naming_engine import SCHEMES, _parse_slot, _format_slot, _range_size


# ── Field helpers ────────────────────────────────────────────────────────

_VALID_FIELDS = ("name", "number")
_STAGE_CODES = ("CD", "DD", "PD", "WV")
# Existing-stage-prefix pattern: "CD - …", "DD_…", "PD: …", etc. Match any
# of CD/DD/PD/WV at start, followed by an optional separator, then content.
_STAGE_PREFIX_RE = re.compile(
    r"^(?:CD|DD|PD|WV)\s*[-_:]?\s*", re.IGNORECASE
)


def _get_field(item, field):
    """Safe field accessor with empty-string default."""
    return str(item.get(field, "") or "")


def _changes_only(item, candidate):
    """Strip candidate dict to fields that actually changed vs item."""
    out = {}
    for f, v in candidate.items():
        if f in _VALID_FIELDS and v != _get_field(item, f):
            out[f] = v
    return out


# ── Operations ───────────────────────────────────────────────────────────
# Each op: (item, params) → {<field>: new_value, "warnings": [...]}.
# Keys for fields that DIDN'T change can be omitted; the engine compares
# against `item` to compute the actual diff.

def op_find_replace(item, params):
    """Find/replace on either name or number.

    Params:
      field:          "name" | "number"  (default "name")
      find:           text or pattern to match
      replace:        replacement text  (default "")
      regex:          treat `find` as regex  (default False)
      case_sensitive: case-sensitive match  (default False)
    """
    field = params.get("field", "name")
    find = params.get("find", "")
    replace = params.get("replace", "")
    use_regex = params.get("regex", False)
    case_sensitive = params.get("case_sensitive", False)
    if not find:
        return {"warnings": ["find_replace: empty 'find' string — no-op"]}

    old = _get_field(item, field)
    flags = 0 if case_sensitive else re.IGNORECASE
    try:
        pattern = find if use_regex else re.escape(find)
        new = re.sub(pattern, replace, old, flags=flags)
    except re.error as ex:
        return {"warnings": [f"find_replace: invalid regex '{find}' — {ex}"]}
    return {field: new}


def op_case_transform(item, params):
    """Apply case transform to a field.

    Params:
      field:     "name" | "number"  (default "name")
      transform: "upper" | "lower" | "title"
    """
    field = params.get("field", "name")
    transform = params.get("transform", "upper").lower()
    old = _get_field(item, field)
    if transform == "upper":
        new = old.upper()
    elif transform == "lower":
        new = old.lower()
    elif transform == "title":
        new = old.title()
    else:
        return {"warnings": [f"case_transform: unknown transform '{transform}'"]}
    return {field: new}


def op_prefix_suffix(item, params):
    """Add / strip prefix and/or suffix on a field. All four ops are optional;
    apply order is: strip_prefix → strip_suffix → add_prefix → add_suffix.

    Params:
      field:        "name" | "number"  (default "name")
      add_prefix:   string to prepend  (optional)
      add_suffix:   string to append   (optional)
      strip_prefix: string to remove if it starts with this  (optional)
      strip_suffix: string to remove if it ends with this    (optional)
    """
    field = params.get("field", "name")
    add_prefix = params.get("add_prefix", "")
    add_suffix = params.get("add_suffix", "")
    strip_prefix = params.get("strip_prefix", "")
    strip_suffix = params.get("strip_suffix", "")

    new = _get_field(item, field)
    if strip_prefix and new.startswith(strip_prefix):
        new = new[len(strip_prefix):]
    if strip_suffix and new.endswith(strip_suffix):
        new = new[:-len(strip_suffix)]
    if add_prefix:
        new = add_prefix + new
    if add_suffix:
        new = new + add_suffix
    return {field: new}


def op_translate_en_to_th(item, params):
    """Translate the sheet name from English to Thai using
    bilingual_dictionary. Always operates on `name` (numbers are codes)."""
    old = _get_field(item, "name")
    return {"name": translate_en_to_th(old)}


def op_translate_th_to_en(item, params):
    """Translate the sheet name from Thai to English."""
    old = _get_field(item, "name")
    return {"name": translate_th_to_en(old)}


def op_add_stage_prefix(item, params):
    """Add a stage code prefix to the sheet name, e.g. 'CD - LEVEL 1 PLAN'.

    Params:
      stage:           explicit stage code  (default: read from item['stage'])
      separator:       text between prefix and name  (default " - ")
      strip_existing:  if True, remove any existing CD/DD/PD/WV prefix first
                       (default True — so a project switching from DD → CD
                       gets clean names)
    """
    stage = params.get("stage") or item.get("stage")
    separator = params.get("separator", " - ")
    strip_existing = params.get("strip_existing", True)

    if not stage:
        return {"warnings": [
            f"add_stage_prefix: no stage specified and no 'stage' on item "
            f"(unique_id={item.get('unique_id')!r})"
        ]}
    stage = stage.upper()
    if stage not in _STAGE_CODES:
        return {"warnings": [f"add_stage_prefix: unknown stage code {stage!r}"]}

    old = _get_field(item, "name")
    new = old
    if strip_existing:
        new = _STAGE_PREFIX_RE.sub("", new)
    expected_prefix = f"{stage}{separator}"
    if not new.startswith(expected_prefix):
        new = expected_prefix + new
    return {"name": new}


def op_offset_renumber(item, params):
    """Add a fixed integer delta to a numeric sheet number.

    Works on iso19650 4-digit / 5-digit numbers (pure-numeric or X-prefixed).
    Skips a49_dotted with a warning — use scheme_convert for dotted projects.

    Params:
      delta: integer to add (negative to subtract)
    """
    try:
        delta = int(params.get("delta", 0))
    except (TypeError, ValueError):
        return {"warnings": [f"offset_renumber: delta must be an integer"]}
    if delta == 0:
        return {}

    old = _get_field(item, "number").upper().strip()
    if not old:
        return {}

    # Dotted format — skip with warning (positions don't shift cleanly).
    if "." in old and old[0].isalpha():
        return {"warnings": [
            f"offset_renumber: skipped dotted number {old!r} — "
            f"use scheme_convert for dotted projects"
        ]}

    # X-prefix (X010, X0100)
    if old.startswith("X"):
        try:
            n = int(old[1:])
        except ValueError:
            return {"warnings": [f"offset_renumber: cannot parse {old!r}"]}
        new_n = n + delta
        if new_n < 0:
            return {"warnings": [
                f"offset_renumber: {old!r} + {delta} would be negative"
            ]}
        # Preserve digit width
        width = len(old) - 1
        return {"number": f"X{new_n:0{width}d}"}

    # Pure numeric
    try:
        n = int(old)
    except ValueError:
        return {"warnings": [f"offset_renumber: cannot parse {old!r}"]}
    new_n = n + delta
    if new_n < 0:
        return {"warnings": [f"offset_renumber: {old!r} + {delta} would be negative"]}
    # Preserve digit width
    width = len(old)
    return {"number": f"{new_n:0{width}d}"}


def op_scheme_convert(item, params):
    """Convert a sheet number from one scheme to another.

    Easy direction (deterministic math):
      iso19650_4digit ↔ iso19650_5digit
      iso19650_4digit / iso19650_5digit → a49_dotted (slot position mapping)

    Hard direction (best-effort, may need user verification):
      a49_dotted → iso19650 — slot positions don't carry level semantics.
      A1.05 might be "5TH FLOOR" or "B5" depending on creation order.
      Warns the user; preview will show the naive mapping.

    Params:
      from_scheme: "iso19650_4digit" | "iso19650_5digit" | "a49_dotted"
      to_scheme:   same
    """
    from_name = params.get("from_scheme")
    to_name = params.get("to_scheme")
    if from_name not in SCHEMES:
        return {"warnings": [f"scheme_convert: unknown from_scheme {from_name!r}"]}
    if to_name not in SCHEMES:
        return {"warnings": [f"scheme_convert: unknown to_scheme {to_name!r}"]}
    if from_name == to_name:
        return {}

    from_scheme = SCHEMES[from_name]
    to_scheme = SCHEMES[to_name]

    old = _get_field(item, "number").strip()
    if not old:
        return {}

    slot, category = _parse_slot(old, from_scheme)
    if slot is None or category not in to_scheme["categories"]:
        return {"warnings": [
            f"scheme_convert: cannot map {old!r} from {from_name} to {to_name}"
        ]}

    from_cat = from_scheme["categories"].get(category, {})
    to_cat = to_scheme["categories"][category]

    # Pick the right "step" granularity for the conversion direction:
    #   - same type (numeric↔numeric or dotted↔dotted): use FINE granularity
    #     (sub_level_increment / sub_increment) so sub-slots like L1M = 1011
    #     map cleanly to v2 10110.
    #   - cross type (numeric↔dotted): use COARSE granularity (level_increment
    #     / primary_increment) — dotted has no sub-positions, so we map by
    #     primary position only. v1 1010 (L1) → A1.01 (position 1), not A1.10.
    from_dotted = from_scheme.get("digit_count") is None
    to_dotted = to_scheme.get("digit_count") is None
    same_type = from_dotted == to_dotted

    def _step(cat, fine):
        if fine:
            return (cat.get("sub_level_increment")
                    or cat.get("sub_increment")
                    or cat.get("primary_increment", 1))
        return cat.get("level_increment") or cat.get("primary_increment", 1)

    from_step = _step(from_cat, fine=same_type)
    to_step = _step(to_cat, fine=same_type)
    from_base = from_cat.get("base", 0) or 0
    to_base = to_cat.get("base", 0) or 0

    if from_step == 0:
        return {"warnings": [f"scheme_convert: zero increment in {from_name}.{category}"]}

    offset = slot - from_base
    position = offset // from_step  # integer division — drops sub-position info if present
    new_slot = to_base + position * to_step

    # Validate new_slot fits within target category's range.
    # Each category occupies [base, base + range_size). X0 uses its own format
    # so range check is relaxed (X-prefix has its own digit allowance).
    range_size = _range_size(to_scheme)
    if category != "X0" and new_slot >= to_base + range_size:
        return {"warnings": [
            f"scheme_convert: {old!r} → slot {new_slot} overflows "
            f"{to_name}.{category} range"
        ]}

    new_number = _format_slot(category, new_slot, to_scheme)

    warnings = []
    # Warn if the source had sub-position info that won't survive cross-type conversion.
    if from_step > 0 and (offset % from_step) != 0:
        warnings.append(
            f"scheme_convert: {old!r} had sub-position info that can't map "
            f"cleanly to {to_name}; rounded down"
        )
    # Warn for dotted → numeric — level semantics aren't preserved.
    if from_dotted and not to_dotted:
        warnings.append(
            f"scheme_convert: {old!r} → {new_number!r} uses naive position "
            f"mapping; verify level assignment matches your intent"
        )

    return {"number": new_number, "warnings": warnings} if warnings else {"number": new_number}


# ── Operation registry ───────────────────────────────────────────────────

OPERATIONS = {
    "find_replace":          op_find_replace,
    "case_transform":        op_case_transform,
    "prefix_suffix":         op_prefix_suffix,
    "translate_en_to_th":    op_translate_en_to_th,
    "translate_th_to_en":    op_translate_th_to_en,
    "add_stage_prefix":      op_add_stage_prefix,
    "offset_renumber":       op_offset_renumber,
    "scheme_convert":        op_scheme_convert,
}


def apply_operation(item, operation_spec):
    """Apply a single operation to a single item. Returns the operation's
    raw output dict (may include unchanged fields + warnings).
    Caller uses _changes_only() to filter to actual diffs."""
    op_name = operation_spec.get("operation")
    params = operation_spec.get("params") or {}
    handler = OPERATIONS.get(op_name)
    if not handler:
        return {"warnings": [f"unknown operation: {op_name!r}"]}
    try:
        return handler(item, params)
    except Exception as ex:
        return {"warnings": [f"{op_name}: unexpected error — {ex}"]}


# ── Preview generation ───────────────────────────────────────────────────

def compute_rename_preview(inventory, operation_spec, selection=None):
    """Return preview rows for the wizard's diff table.

    Args:
      inventory:       list of inventory items (from FetchProjectInventoryCommand)
      operation_spec:  {"operation": "...", "params": {...}}
      selection:       optional set of unique_ids to include (None = all)

    Returns:
      List of dicts:
        { "unique_id": str,
          "old_number": str, "new_number": str,
          "old_name":   str, "new_name":   str,
          "changed":    bool,
          "warnings":   [str, ...] }

    Items not in `selection` are skipped entirely (not returned). Items in
    selection but with no changes ARE returned (changed=False) so the wizard
    can show the user what was excluded.
    """
    if not isinstance(inventory, list):
        return []
    selection_set = set(selection) if selection is not None else None

    rows = []
    for item in inventory:
        uid = item.get("unique_id")
        if not uid:
            continue
        if selection_set is not None and uid not in selection_set:
            continue

        result = apply_operation(item, operation_spec)
        warnings = list(result.get("warnings", []))
        diff = _changes_only(item, result)

        old_number = _get_field(item, "number")
        old_name = _get_field(item, "name")
        new_number = diff.get("number", old_number)
        new_name = diff.get("name", old_name)

        rows.append({
            "unique_id":  uid,
            "old_number": old_number,
            "new_number": new_number,
            "old_name":   old_name,
            "new_name":   new_name,
            "changed":    bool(diff),
            "warnings":   warnings,
        })
    return rows


def preview_to_updates(preview_rows, deselected_ids=None):
    """Convert preview rows into ExecuteBatchUpdateCommand-compatible updates.

    Filters out:
      - rows where `changed` is False
      - rows whose unique_id is in `deselected_ids`

    Returns:
      [{ "unique_id": str,
         "element_type": "SHEET",
         "changes": { "number"?: str, "name"?: str } }, ...]
    """
    deselected = set(deselected_ids or [])
    updates = []
    for row in preview_rows:
        if not row.get("changed"):
            continue
        if row["unique_id"] in deselected:
            continue
        changes = {}
        if row["new_number"] != row["old_number"]:
            changes["number"] = row["new_number"]
        if row["new_name"] != row["old_name"]:
            changes["name"] = row["new_name"]
        if not changes:
            continue
        updates.append({
            "unique_id":    row["unique_id"],
            "element_type": "SHEET",
            "changes":      changes,
        })
    return updates


# ── Public introspection (for the wizard UI) ─────────────────────────────

def list_operations():
    """Return the list of registered operation names. Used by the wizard's
    operation-picker dropdown."""
    return sorted(OPERATIONS.keys())
