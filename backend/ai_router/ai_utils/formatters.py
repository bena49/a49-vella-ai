# ai_router/ai_utils/formatters.py

from ..ai_core.session_manager import debug_session

def format_views_for_display(views, title="Model Views"):
    if not views:
        return f"{title}:\n( none )"

    categories = {
        "Floor Plans": [], "Ceiling Plans": [], "3D Views": [],
        "Sections": [], "Elevations": [], "Details": [], "Other": []
    }
    
    for v in views:
        name = v.get('name', 'Unknown')
        vtype = str(v.get('type', '')).lower() 

        if "floorplan" in vtype: categories["Floor Plans"].append(name)
        elif "ceilingplan" in vtype: categories["Ceiling Plans"].append(name)
        elif "threed" in vtype: categories["3D Views"].append(name)
        elif "section" in vtype: categories["Sections"].append(name)
        elif "elevation" in vtype: categories["Elevations"].append(name)
        elif "drafting" in vtype: categories["Details"].append(name)
        else: categories["Other"].append(name)

    lines = [f"{title}:"]
    for cat_name, items in categories.items():
        if items:
            lines.append(f"\n {cat_name} ({len(items)}):")
            for item in sorted(items):
                lines.append(f" {item}")

    return "\n".join(lines)

def format_sheets_for_display(sheets):
    if not sheets: return "No sheets found."
    # Sort by sheet number ascending so listings read low → high.
    from ..ai_engines.naming_engine import sort_key_sheet_number
    ordered = sorted(sheets, key=lambda s: sort_key_sheet_number(s.get("number")))
    lines = ["Sheets in Project:\n"]
    for s in ordered:
        lines.append(f" {s.get('number', '')} — {s.get('name', '')}")
    return "\n".join(lines)

def normalize_view_list(raw_list):
    """
    Robust normalizer that handles Name/name, Type/type keys.
    """
    normalized = []
    if not raw_list: return []

    for item in raw_list:
        if isinstance(item, dict):
            # Check all casing variations
            name = item.get("name") or item.get("Name") or item.get("ViewName") or ""
            level = item.get("level") or item.get("Level") or ""
            vtype = item.get("type") or item.get("Type") or item.get("ViewType") or ""
            normalized.append({"name": name, "level": level, "type": vtype})
        else:
            normalized.append({"name": str(item)})
    return normalized

def normalize_sheet_list(raw_list):
    normalized = []
    if not raw_list: return []

    for item in raw_list:
        if isinstance(item, dict):
            number = item.get("number") or item.get("Number") or item.get("SheetNumber") or ""
            name = item.get("name") or item.get("Name") or item.get("SheetName") or ""
            normalized.append({"number": number, "name": name})
        else:
            normalized.append({"name": str(item), "number": ""})
    return normalized

def update_last_known_views(request, list_result):
    """
    Forces session update even if list is empty, to confirm fetch occurred.
    """
    # Create simple list of names for naming engine
    views_list = [x.get("name") for x in list_result]
    request.session["ai_last_known_views"] = views_list
    request.session.modified = True
    
    # Force immediate save to database to prevent race conditions
    try: request.session.save()
    except: pass
    
    debug_session(request, f"UPDATED cache: {len(views_list)} views stored.")

def update_last_known_sheets(request, list_result):
    # Two parallel caches:
    #   ai_last_known_sheets       — list of numbers only ("A1.01", "1010", …).
    #     Backward-compatible shape used by every existing caller.
    #   ai_last_known_sheets_full  — list of "NUMBER - NAME" strings.
    #     Required by detect_duplicate_levels (added in the duplicate-handling
    #     rollout) which needs to match a proposed sheet name against
    #     existing names. Storing both keeps the new feature decoupled from
    #     the older numbers-only consumers.
    sheets_list = [x.get("number") for x in list_result]
    sheets_full = [
        f"{x.get('number') or ''} - {x.get('name') or ''}".strip(" -")
        for x in list_result
    ]
    request.session["ai_last_known_sheets"] = sheets_list
    request.session["ai_last_known_sheets_full"] = sheets_full
    request.session.modified = True
    try: request.session.save()
    except: pass
    debug_session(request, f"UPDATED cache: {len(sheets_list)} sheets stored.")