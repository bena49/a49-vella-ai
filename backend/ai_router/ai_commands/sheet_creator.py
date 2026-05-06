import re
from rest_framework.response import Response
from ..ai_core.session_manager import debug_session, reset_pending
from ..ai_utils.envelope_builder import send_envelope, envelope_create_sheets
from ..ai_engines.naming_engine import (
    apply,
    resolve_scheme_for_request,
    detect_duplicate_levels,
    build_subpart_sheets,
    sort_key_sheet_number,
)
from ..ai_engines.titleblock_engine import parse_titleblock_from_user_text, get_standard_titleblocks
from ..ai_engines.level_engine import parse_levels

def message(text):
    return Response({"message": text})

def request_titleblock_choice(request=None):
    options = get_standard_titleblocks()
    option_list_str = "\n".join([f"{i+1}. {opt}" for i, opt in enumerate(options)])
    # Set the flag + store options so the views.py interceptor can match the
    # user's reply on the next turn (numeric or exact-string match).
    if request is not None:
        request.session["ai_expecting_titleblock_selection"] = True
        request.session["ai_pending_titleblock_options"] = options
        request.session.modified = True
    return message(f"Please choose a titleblock for this sheet:\n{option_list_str}")

def finalize_create_sheets(request):
    # 1. PREPARE RAW INPUTS
    raw_msg = request.data.get("message", "").lower()
    vt_raw = request.session.get("ai_pending_view_type") or ""
    name_raw = request.session.get("ai_pending_user_provided_name") or ""
    cat = request.session.get("ai_pending_sheet_category")
    
    # 💥 FIX 1: Better Batch Regex
    # Matches: "5 sheets", "5 custom sheets", "5 new sheets"
    if not request.session.get("ai_pending_batch_count"):
        count_match = re.search(r"\b(\d+)\s+(?:[a-z]+\s+)?sheets?\b", raw_msg)
        if count_match:
            request.session["ai_pending_batch_count"] = count_match.group(1)
            request.session.modified = True

    levels = request.session.get("ai_pending_levels_parsed") 
    search_text = (raw_msg + " " + vt_raw + " " + name_raw).strip()

    # Remove Titleblock text
    tb_raw = request.session.get("ai_pending_titleblock")
    if tb_raw:
        try:
            pattern = re.escape(tb_raw)
            search_text = re.sub(pattern, "", search_text, flags=re.IGNORECASE).strip()
        except:
            search_text = search_text.replace(tb_raw.lower(), "")

    # 2. DETECT CUSTOM MODE
    # 💥 Added "custom sheets" or just "custom" logic explicitly
    is_custom_mode = "custom" in search_text or request.session.get("ai_pending_stage") == "NONE"
    if is_custom_mode:
        request.session["ai_pending_stage"] = "NONE"
        request.session["ai_pending_sheet_category"] = "X0" 
        request.session["ai_pending_user_provided_name"] = None 
        request.session.modified = True
        cat = "X0" 

    # 3. DETECT CATEGORY RANGE
    range_source = cat if cat else search_text
    cat_range_match = re.search(r"\b([a-z])(\d+)\s*(?:-|to|–|—)\s*\1(\d+)\b", range_source, re.IGNORECASE)
    if cat_range_match:
        prefix = cat_range_match.group(1).upper()
        start = int(cat_range_match.group(2))
        end = int(cat_range_match.group(3))
        if start < end:
            expanded_cats = [f"{prefix}{i}" for i in range(start, end + 1)]
            cat = ", ".join(expanded_cats)
            request.session["ai_pending_sheet_category"] = cat
            request.session.modified = True

    # 4. NORMALIZE LIST SEPARATORS
    if cat:
        cat = re.sub(r"\s+(?:and|&|\+)\s+", ",", cat, flags=re.IGNORECASE)
        cat = re.sub(r"\b([A-Z]\d)\s+([A-Z]\d)\b", r"\1, \2", cat, flags=re.IGNORECASE)
        request.session["ai_pending_sheet_category"] = cat
        request.session.modified = True

    # 5. ROBUST LEVEL DETECTION
    if not levels:
        extracted_levels = parse_levels(search_text)
        if extracted_levels:
            # Safety Check: Don't treat batch count as level
            is_count_intent = re.search(r"\b\d+\s+(?:[a-z]+\s+)?sheets\b", raw_msg)
            if not is_count_intent:
                levels = extracted_levels
                request.session["ai_pending_levels_parsed"] = levels
                request.session.modified = True
                debug_session(request, f"🎯 Levels Detected via Engine: {levels}")

    # 6. SANITIZE CATEGORY
    if cat:
        cat_clean = cat.strip().upper()
        valid_codes = ["A0","A1","A2","A3","A4","A5","A6","A7","A8","A9","X0","X1","X2","X3","X4","X5","X6","X7","X8","X9"]
        parts = [c.strip() for c in cat_clean.split(",")]
        if not all(p in valid_codes for p in parts):
            request.session["ai_pending_user_provided_name"] = cat 
            request.session["ai_pending_sheet_category"] = None
            request.session.modified = True
            cat = None

    # 7. SMART INFERENCE
    if not cat:
        if search_text:
            inferred = []
            keywords = [
                ("cover", "A0"), ("index", "A0"), ("drawing list", "A0"), ("site", "A0"), 
                ("vicinity", "A0"), ("symbol", "A0"), ("safety", "A0"), ("wall type", "A0"),
                ("toilet", "A6"), ("restroom", "A6"), ("pattern", "A6"), ("flooring", "A6"),
                ("canopy", "A6"), ("roof detail", "A6"), ("enlarged plan", "A6"),
                ("stair", "A7"), ("ramp", "A7"), ("lift", "A7"), ("elevator", "A7"),
                ("door", "A8"), ("window", "A8"), ("schedule", "A8"),
                ("ceiling", "A5"), ("wall section", "A4"), ("building section", "A3"), 
                ("section", "A3"), ("elev", "A2"), ("detail", "A9"),
                ("floor", "A1"), ("plan", "A1")
            ]
            for kw, code in keywords:
                if kw in search_text:
                    if code not in inferred: inferred.append(code)
            
            # Conflict Resolution
            if "A4" in inferred and "A3" in inferred and "wall section" in search_text: inferred.remove("A3")
            if "A1" in inferred and any(sp in inferred for sp in ["A5", "A6", "A7"]) and "floor plan" not in search_text:
                inferred.remove("A1")

            if inferred:
                cat = ", ".join(inferred)
                request.session["ai_pending_sheet_category"] = cat
                request.session.modified = True

    # 8. ASK STAGE (Skipped if X0)
    # 💥 FIX 2: Allow NONE if category is X0 (Custom)
    stage = request.session.get("ai_pending_stage")
    if not stage:
        if cat != "X0": # Only ask if NOT custom
            return message("Which design stage? (WV, PD, DD, CD)")
        else:
            # Auto-fill NONE for X0 so execution proceeds
            request.session["ai_pending_stage"] = "NONE"
            request.session.modified = True
    
    # 9. AUTO-PILOT FOR COVER
    if cat == "A0" and not request.session.get("ai_pending_titleblock"):
        name_raw = request.session.get("ai_pending_user_provided_name") or ""
        vt_raw = request.session.get("ai_pending_view_type") or ""
        if "cover" in name_raw.lower() or "cover" in vt_raw.lower():
            request.session["ai_pending_titleblock"] = "A49_TB_A1_Horizontal_Cover : Cover"
            request.session.modified = True

    # 10. AUTO-DETECT CATEGORY FROM LEVELS
    if not cat and levels:
        if "ceiling" in search_text or "rcp" in search_text:
            cat = "A5"
        else:
            cat = "A1"
        request.session["ai_pending_sheet_category"] = cat
        request.session.modified = True

    if not cat: return message("Please select a sheet category.")

    return execute_sheet_creation(request)


def execute_sheet_creation(request):
    """Runs the naming engine and builds the envelope."""
    cat_raw = request.session.get("ai_pending_sheet_category")
    stage = request.session.get("ai_pending_stage")
    raw_tb = request.session.get("ai_pending_titleblock")
    batch_raw = request.session.get("ai_pending_batch_count")
    user_name = request.session.get("ai_pending_user_provided_name")
    view_type = request.session.get("ai_pending_view_type")
    
    # Reload levels
    levels = request.session.get("ai_pending_levels_parsed")

    # SORT LEVELS LOGIC
    # Delegated to sort_levels_for_sheet_creation so the rule is unit-tested
    # in isolation. See its docstring for the full ordering spec.
    if levels:
        from ..ai_engines.level_engine import sort_levels_for_sheet_creation
        levels = sort_levels_for_sheet_creation(levels)

    fam, typ = None, None
    if raw_tb == "A49_TB_A1_Horizontal_Cover : Cover":
        fam, typ = "A49_TB_A1_Horizontal_Cover", "Cover"
    elif raw_tb:
        fam, typ = parse_titleblock_from_user_text(raw_tb)

    if not fam or not typ:
        return request_titleblock_choice(request)

    # Level Cache Check
    # Chat-typed commands may arrive with parsed level tokens (SITE, TOP,
    # L1, B1M ...) but no project-level cache. Sheet numbering for level-
    # based categories (A1, A5) and the ROOF/TOP slot computation both
    # depend on knowing the actual project levels, so fetch them once
    # before proceeding. The callback resolves tokens and re-enters here.
    fetch_attempted = request.session.get("ai_levels_fetch_attempted")
    if levels and not request.session.get("ai_last_known_levels") and not fetch_attempted:
        request.session["ai_levels_fetch_attempted"] = True
        request.session["ai_pending_request_data"] = {
            "intent": request.session.get("ai_pending_intent"),
            "sheet_category": cat_raw,
            "stage": stage,
            "titleblock": raw_tb,
            "batch": batch_raw,
            "levels": levels,
            "view_type": view_type
        }
        request.session.modified = True
        return send_envelope(request, {"command": "get_levels"})

    # Cache Check
    if not request.session.get("ai_last_known_sheets"):
        request.session["ai_pending_request_data"] = {
            "intent": request.session.get("ai_pending_intent"),
            "sheet_category": cat_raw, 
            "stage": stage, 
            "titleblock": raw_tb, 
            "batch": batch_raw,
            "levels": levels,          
            "view_type": view_type     
        }
        request.session.modified = True
        return send_envelope(request, {"command": "list_sheets"})

    existing_views = request.session.get("ai_last_known_views", [])
    existing_sheets = request.session.get("ai_last_known_sheets", [])
    # Project-wide level inventory — needed by the naming engine to compute
    # ROOF/TOP slot (= max above-grade level + 10) regardless of which levels
    # are in this request.
    project_levels = request.session.get("ai_last_known_levels", [])
    categories = [c.strip() for c in cat_raw.split(",")]

    counts = [b.strip() for b in str(batch_raw).split(",")] if batch_raw else ["1"] * len(categories)
    if len(counts) == 1 and len(categories) > 1:
        counts = [counts[0]] * len(categories)

    all_created_sheets = []
    skipped_subpart_levels = []   # populated by Sub-parts branch when basements can't fit

    # Resolve the active numbering scheme for this project (auto-detect from
    # cached sheets first, falls back to session override / iso19650_4digit default).
    scheme = resolve_scheme_for_request(request)

    # ────────────────────────────────────────────────────────────
    # DUPLICATE DETECTION (only applies when we have levels — primary
    # gating signal for level-based categories A1/A5).
    #
    # First entry: detect, prompt, set ai_expecting_duplicate_choice, return.
    # Re-entry after the user replies: ai_duplicate_choice is set, we branch:
    #   "cancel"   → abort, clear state, friendly message.
    #   "skip"     → strip duplicates from `levels`, allocate non-duplicates.
    #   "subparts" → pre-allocate sub-slots for each duplicate (gap-fill,
    #                 basements skipped with a per-level note), then allocate
    #                 non-duplicates normally. Both lists merge in the output.
    # ────────────────────────────────────────────────────────────
    duplicate_choice = request.session.get("ai_duplicate_choice")
    duplicate_info = request.session.get("ai_pending_duplicate_info") or []

    # 🛡️ DEFENSIVE FALLBACK — if we have stored duplicate_info from a previous
    # prompt but no resolved choice in session, the user IS replying to that
    # prompt; extract the choice from their raw message text directly. This
    # catches edge cases where ai_expecting_duplicate_choice gets cleared
    # earlier in the request pipeline (e.g. by GPT slot extraction running
    # before the views-level interceptor on certain inputs). Idempotent:
    # has no effect when there's no pending duplicate info OR a choice is
    # already set.
    if duplicate_info and not duplicate_choice:
        raw_msg_l = (request.data.get("message", "") or "").strip().lower()
        if re.search(r'\b(cancel|abort|stop|nevermind|never\s*mind)\b', raw_msg_l):
            duplicate_choice = "cancel"
        elif re.search(r'\b(sub[\s\-]?(?:parts?|sheets?))\b', raw_msg_l) or "create as sub" in raw_msg_l:
            duplicate_choice = "subparts"
        elif re.search(r'\bskip(\s+dup\w*)?\b', raw_msg_l):
            duplicate_choice = "skip"
        if duplicate_choice:
            debug_session(request,
                f"🛡️ execute_sheet_creation fallback resolved duplicate-choice='{duplicate_choice}' from raw msg")
            request.session["ai_duplicate_choice"] = duplicate_choice
            request.session["ai_expecting_duplicate_choice"] = False
            request.session.modified = True

    if levels and not duplicate_choice:
        # Use the parallel "NUMBER - NAME" cache populated by formatters.update_last_known_sheets.
        existing_inventory = request.session.get("ai_last_known_sheets_full") or []
        # Detect against the FIRST category in the request (A1/A5 — these
        # are the user's level-based intents). Custom categories don't have
        # level-driven duplicates so we skip detection entirely.
        primary_cat = categories[0] if categories else None
        duplicates = (
            detect_duplicate_levels(primary_cat, levels, existing_inventory, scheme=scheme)
            if primary_cat else []
        )
        if duplicates:
            request.session["ai_pending_duplicate_info"] = duplicates
            request.session["ai_expecting_duplicate_choice"] = True
            request.session.modified = True
            # Force-save now so the flag is durably stored before the prompt
            # response goes back to the client. Defensive against edge cases
            # in DRF/Django middleware ordering that could otherwise let the
            # flag drop between this request and the user's reply.
            try: request.session.save()
            except Exception: pass
            lines = ["⚠ Sheets already exist for these levels:"]
            for d in duplicates:
                lines.append(f"  • {d['existing_number']} — {d['existing_name']}")
            lines.append("")
            lines.append("What would you like to do? Reply with one of:")
            lines.append("  • ** cancel ** — abort, no sheets created")
            lines.append("  • ** skip ** — only create sheets for new (non-duplicate) levels")
            lines.append("  • ** sub-sheets ** — create duplicates as sub-parts of the existing sheets")
            return Response({
                "message": "\n".join(lines),
                "options": ["Cancel", "Skip duplicates", "Create as sub-sheets"],
            })

    # Choice resolved (or never needed). Apply branch behavior.
    duplicate_levels_set = {d["level"] for d in duplicate_info}

    if duplicate_choice == "cancel":
        request.session["ai_duplicate_choice"] = None
        request.session["ai_pending_duplicate_info"] = None
        request.session["ai_expecting_duplicate_choice"] = False
        request.session.modified = True
        reset_pending(request)
        return message("✋ Cancelled. No sheets were created.")

    if duplicate_choice == "skip" and levels and duplicate_levels_set:
        levels = [l for l in levels if l not in duplicate_levels_set]
        if not levels:
            request.session["ai_duplicate_choice"] = None
            request.session["ai_pending_duplicate_info"] = None
            request.session["ai_expecting_duplicate_choice"] = False
            request.session.modified = True
            reset_pending(request)
            return message("ℹ All requested levels already have sheets — nothing new to create.")

    try:
        for idx, cat in enumerate(categories):
            current_count = counts[idx] if idx < len(counts) else "1"

            final_name = user_name
            if levels and len(levels) > 0:
                final_name = None

            # Sub-parts branch: pre-allocate sub-slots for the duplicate
            # levels in THIS category, then strip them from the `levels`
            # list passed to apply() so the normal allocator only sees
            # non-duplicates. Order in the success message ends up
            # correct because build_sheets_payload sorts by sheet number
            # at the end and we extend it with the sub-part entries.
            sub_levels_in_cat = []
            if duplicate_choice == "subparts" and duplicate_levels_set:
                cat_dups = [d for d in duplicate_info if d["level"] in duplicate_levels_set]
                # Only run the sub-part allocator on the matching category;
                # detection was scoped to categories[0] above so this is
                # effectively that one category.
                if cat == (categories[0] if categories else None):
                    sub_payloads, sub_skipped = build_subpart_sheets(
                        cat, cat_dups, existing_sheets,
                        scheme=scheme, stage=stage,
                        titleblock_family=fam, titleblock_type=typ,
                    )
                    all_created_sheets.extend(sub_payloads)
                    skipped_subpart_levels.extend(sub_skipped)
                    sub_levels_in_cat = [d["level"] for d in cat_dups]
                    # Levels that successfully became sub-parts are now in
                    # existing_sheets — exclude them from the primary loop.
                    # Levels skipped (basements) also get filtered so we
                    # don't accidentally re-create them at a fresh primary.
                    levels_for_primary = [l for l in levels if l not in duplicate_levels_set]
                else:
                    levels_for_primary = levels
            else:
                levels_for_primary = levels

            # If we already filled this category via sub-parts AND every
            # original level was a duplicate (so levels_for_primary is now
            # empty), DON'T call apply() — it would fall through to the
            # generic batch-count path and create a spurious "CUSTOM SHEET"
            # at the next free primary slot. We only skip this when the
            # input WAS level-based; pure-batch (count-only) requests still
            # go through apply().
            input_was_level_based = bool(levels)
            if (duplicate_choice == "subparts"
                    and input_was_level_based
                    and not levels_for_primary
                    and sub_levels_in_cat):
                debug_session(
                    request,
                    f"⏭ Skip apply() for {cat}: all {len(sub_levels_in_cat)} "
                    "input levels became sub-parts; no primaries to allocate.",
                )
                continue

            req = {
                "command": "create_sheet",
                "sheet_category": cat,
                "stage": stage,
                "titleblock_family": fam,
                "titleblock_type": typ,
                "batch_count": current_count,
                "titleblock_raw": raw_tb,
                "view_type": view_type,
                "levels": levels_for_primary,
                "project_levels": project_levels,
                "sheet_name": final_name
            }
            naming = apply(req, existing_views, existing_sheets, scheme=scheme)
            if naming.get("sheets"):
                new_sheets = naming["sheets"]
                all_created_sheets.extend(new_sheets)
                for s in new_sheets: existing_sheets.append(s.get("sheet_number"))
    except Exception as e:
        return message(f"Error in naming engine: {e}")

    # Sort the combined list (sub-parts + primaries) by sheet number so the
    # success message reads top-to-bottom in slot order.
    all_created_sheets.sort(key=lambda p: sort_key_sheet_number(p.get("sheet_number", "")))

    # Clear duplicate-handling pending state now that we've applied the choice.
    request.session["ai_duplicate_choice"] = None
    request.session["ai_pending_duplicate_info"] = None
    request.session["ai_expecting_duplicate_choice"] = False

    request.session["ai_pending_request_data"] = None
    request.session["ai_last_known_sheets"] = [] 
    request.session.modified = True
    reset_pending(request)
    
    # Note: build_sheets_payload already returns sheets sorted by number,
    # so no extra sort is needed here.

    sheet_items = []
    for s in all_created_sheets:
        sheet_items.append({
            "sheet_number": s.get("sheet_number", ""),
            "sheet_name": s.get("sheet_name", ""),
            "sheet_type": s.get("sheet_type", ""),
            "project_phase": s.get("project_phase", ""),
            "sheet_set": s.get("sheet_set", ""),
            "discipline": s.get("discipline", ""),
            "titleblock_family": s.get("titleblock_family", ""),
            "titleblock_type": s.get("titleblock_type", "")
        })
    
    if not sheet_items:
        if "A5" in cat_raw.upper() and levels and any("SITE" in str(l).upper() for l in levels):
            return message("❌ Ceiling Plan for Site Level is not required.")

        # If we got here via the Sub-parts branch and EVERY duplicate was a
        # basement (or otherwise unattachable), produce a clearer message.
        if skipped_subpart_levels:
            skipped_lines = "\n".join(
                f"  • {s.get('level')}: {s.get('reason')}"
                for s in skipped_subpart_levels
            )
            return message(
                "⚠ Could not create sub-sheets for the following levels — "
                "this scheme has no sub-slot room for them:\n" + skipped_lines +
                "\n\nUse the Manual Edit wizard if you need to add them at a different slot."
            )

        return message("❌ No sheets could be created based on the input.")

    env = envelope_create_sheets(sheet_items)
    response = send_envelope(request, env)

    # If sub-parts were requested and some levels couldn't be allocated
    # (basements in iso19650 schemes, etc.), surface that as a chat-side
    # notice alongside the envelope. The envelope still runs and creates
    # what we could; the user just sees the partial-skip notice first.
    if skipped_subpart_levels:
        skipped_lines = "\n".join(
            f"  • {s.get('level')}: {s.get('reason')}"
            for s in skipped_subpart_levels
        )
        response.data["message"] = (
            "ℹ Some levels couldn't be added as sub-sheets under their parent and were skipped:\n"
            + skipped_lines +
            "\n(Use Manual Edit if you need to give them a custom slot.)"
        )

    return response