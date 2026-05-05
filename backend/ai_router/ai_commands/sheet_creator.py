import re
from rest_framework.response import Response
from ..ai_core.session_manager import debug_session, reset_pending
from ..ai_utils.envelope_builder import send_envelope, envelope_create_sheets
from ..ai_engines.naming_engine import apply, resolve_scheme_for_request
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
    #
    # Sheet allocation depends on level order (mezzanines must be processed
    # AFTER their parent floor so they can attach as sub-parts). The most
    # reliable signal is the actual Revit elevation, which the C# add-in
    # caches per session as ai_last_known_levels = [{name, elevation_mm}, …].
    # We use that when available; otherwise we fall back to a heuristic
    # that orders by category (basement → site → numbered floor → roof) and
    # uses the embedded digit as a tiebreaker.
    if levels:
        project_levels_cache = request.session.get("ai_last_known_levels") or []
        elev_lookup = {}
        for entry in project_levels_cache:
            if isinstance(entry, dict):
                nm = (entry.get("name") or "").strip()
                if nm:
                    elev_lookup[nm.upper()] = entry.get("elevation_mm")

        # Heuristic fallback. Substring checks are ordered carefully:
        # ROOF/TOP first so 'TOP' isn't pulled into the 'P' (parking) bucket.
        def heuristic_key(lvl_token):
            upper = lvl_token.upper()
            digit_match = re.search(r'\d+', upper)
            num = int(digit_match.group()) if digit_match else 0
            if "ROOF" in upper or upper in ("TOP", "RF"): return (4, 999)
            if upper.startswith("B"):  return (0, num)
            if upper.startswith("P"):  return (1, num)
            if "SITE" in upper:        return (2, 0)
            if upper.startswith("L") or "LEVEL" in upper: return (3, num)
            return (5, 0)

        def level_sort_key(lvl_token):
            elev = elev_lookup.get(lvl_token.upper())
            if isinstance(elev, (int, float)):
                # Real elevation wins. Tuple shape matches the heuristic so
                # a mixed batch (some known, some not) sorts coherently.
                return (0, float(elev))
            # Unknown level → fall back to the heuristic but tag with a
            # sentinel high value so known-elevation levels always sort first.
            heur = heuristic_key(lvl_token)
            return (1, heur)

        levels = sorted(levels, key=level_sort_key)

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

    # Resolve the active numbering scheme for this project (auto-detect from
    # cached sheets first, falls back to session override / iso19650_4digit default).
    scheme = resolve_scheme_for_request(request)

    try:
        for idx, cat in enumerate(categories):
            current_count = counts[idx] if idx < len(counts) else "1"

            final_name = user_name
            if levels and len(levels) > 0:
                final_name = None

            req = {
                "command": "create_sheet",
                "sheet_category": cat,
                "stage": stage,
                "titleblock_family": fam,
                "titleblock_type": typ,
                "batch_count": current_count,
                "titleblock_raw": raw_tb,
                "view_type": view_type,
                "levels": levels,
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
        
        return message("❌ No sheets could be created based on the input.")
    
    env = envelope_create_sheets(sheet_items)
    return send_envelope(request, env)