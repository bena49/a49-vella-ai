# =====================================================================
# scope_box_engine.py
# Matches user input to Revit Scope Box IDs
# =====================================================================
import re

def format_scope_boxes_for_chat(scope_boxes):
    """
    Takes a list of dicts [{'name': 'SB1', 'id': '...'}, ...]
    Returns a numbered string list.
    """
    if not scope_boxes:
        return None
    
    lines = ["Found available Scope Boxes. Please choose one (or say 'No'):"]
    for i, sb in enumerate(scope_boxes):
        name = sb.get('name', 'Unknown') if isinstance(sb, dict) else str(sb)
        lines.append(f"{i+1}. {name}")
    
    return "\n".join(lines)

def parse_scope_box_selection(user_text, scope_boxes):
    """
    Matches "1", "SB_OVERALL", "No" to a specific UniqueId.
    Returns (id, name) or (None, None).
    """
    if not user_text or not scope_boxes:
        return None, None
        
    text = user_text.strip().lower()
    
    # 💥 FIX 1: Direct Keyword Fail-Safe (Absolute matches)
    if text in ["no", "none", "skip", "n/a", "nope", "dont", "stop", "zero"]:
        return "SKIP", "None"

    # 💥 FIX 2: Robust Regex (Catch "-no", "no.", "skip it", etc.)
    if re.search(r'^(?:-|\.|_)?\s*(no|none|skip|n/a|nope|dont|stop)\b', text):
        return "SKIP", "None"

    # 3. Check for Digit Selection ("1", "2", "#1")
    digit_match = re.search(r'^#?(\d+)\.?$', text)
    if digit_match:
        idx = int(digit_match.group(1)) - 1
        if 0 <= idx < len(scope_boxes):
            return scope_boxes[idx]['id'], scope_boxes[idx]['name']

    # 4. Check for Name Match (Fuzzy)
    for sb in scope_boxes:
        sb_name_lower = sb['name'].lower()
        
        # A. Exact substring match (User typed "Overall")
        # Must be at least 3 chars to avoid matching "SB" generic terms too easily
        if len(text) > 2 and text in sb_name_lower: 
            return sb['id'], sb['name']

        # B. Reverse check: Is the scope box name inside the user prompt?
        # (e.g. User said "Use SB_Plan_Overall please")
        if sb_name_lower in text:
            return sb['id'], sb['name']

    return None, None