import re
from rest_framework.response import Response
from ..ai_core.session_manager import debug_session, reset_pending
from ..ai_utils.envelope_builder import send_envelope, envelope_create_views
from ..ai_engines.naming_engine import apply, get_view_abbrev
from ..ai_engines.template_engine import validate_template, get_templates
from ..ai_engines.level_engine import parse_levels

def message(text): return Response({"message": text})

def finalize_create_views(request):
    debug_session(request, "ENTERING finalize_create_views()")
    
    raw_vt = request.session.get("ai_pending_view_type", "")
    
    # 💥 FIX 1: Force 'None' to be '[]'
    levels = request.session.get("ai_pending_levels_parsed") or []
    
    raw_levels_text = request.session.get("ai_pending_levels_raw") 
    stage = request.session.get("ai_pending_stage")
    tpl = request.session.get("ai_pending_template")
    batch_raw = request.session.get("ai_pending_batch_count")
    
    # We still capture this, but we won't force-override with it anymore
    user_provided_name = request.session.get("ai_pending_user_provided_name")

    try: batch = int(batch_raw) if batch_raw else 1
    except: batch = 1
    
    raw_scope_box_id = request.session.get("ai_pending_scope_box_id")

    # STEP 0: FALLBACK LEVELS
    if not levels:
        raw_msg = request.data.get("message", "") or ""
        levels_raw_slot = request.session.get("ai_pending_levels_raw") or ""
        search_text = (raw_msg + " " + levels_raw_slot).strip()
        extracted = parse_levels(search_text)
        if extracted:
            levels = extracted
            request.session["ai_pending_levels_parsed"] = levels
            request.session.modified = True

    # STEP 0.5: MANUAL REFRESH
    raw_msg = request.data.get("message", "").lower()
    trigger_words = ["refresh", "reset", "update", "reload", "sync"]
    if any(w in raw_msg for w in trigger_words):
        debug_session(request, "🔄 User requested Cache Refresh.")
        request.session["ai_last_known_views"] = []
        request.session.modified = True

    # STEP 0.7: LEVEL CACHE CHECK
    # Chat-typed commands skip the wizard's cache_level_inventory step, so
    # ai_last_known_levels can be empty here. If the user asked for levels
    # that include special tokens (SITE, TOP, RF) — which have no digit for
    # C#'s ResolveLevel digit-fallback to grab onto — they would silently be
    # skipped. Force a one-shot get_levels round-trip to fill the cache;
    # the callback will resolve tokens to project-native names and resume.
    fetch_attempted = request.session.get("ai_levels_fetch_attempted")
    if levels and not request.session.get("ai_last_known_levels") and not fetch_attempted:
        request.session["ai_levels_fetch_attempted"] = True
        request.session["ai_pending_request_data"] = {
            "intent": request.session.get("ai_pending_intent"),
            "view_type": raw_vt,
            "levels": levels,
            "stage": stage,
            "template": tpl,
            "batch": batch_raw,
            "scope_box_id": raw_scope_box_id,
            "user_provided_name": user_provided_name
        }
        request.session.modified = True
        return send_envelope(request, {"command": "get_levels"})

    # 1) CACHE CHECK
    if not request.session.get("ai_last_known_views"):
        request.session["ai_pending_request_data"] = {
            "intent": request.session.get("ai_pending_intent"), 
            "view_type": raw_vt, 
            "levels": levels, 
            "stage": stage, 
            "template": tpl, 
            "batch": batch_raw, 
            "scope_box_id": raw_scope_box_id,
            "user_provided_name": user_provided_name 
        }
        request.session.modified = True 
        return send_envelope(request, {"command": "list_views"}) 

    # 2) PREPARE DATA
    final_scope_box_id = None if raw_scope_box_id == "SKIP" else raw_scope_box_id
    existing_views = request.session.get("ai_last_known_views", [])
    existing_sheets = request.session.get("ai_last_known_sheets", [])

    view_types = []
    if raw_vt:
        parts = re.split(r'\s+(?:and|&|,)\s+', raw_vt, flags=re.IGNORECASE)
        for p in parts:
            p = p.strip()
            if p: view_types.append(p)
    
    final_view_items = []
    try:
        for vt in view_types:
            abbrev = get_view_abbrev(vt)
            
            # --- RESTRICTIONS ---
            if stage == "WV" and abbrev in ["D1", "SC", "AD", "AW"]:
                return message("❌ Detail and Schedule views are not created at this Stage.")
            if abbrev == "AP" and stage not in ["PD", "DD"]:
                return message(f"❌ Area Plans are only created in PD and DD stages (Requested: {stage}).")

            debug_session(request, f"Processing '{vt}' (Abbrev: {abbrev}) | Batch Count: {batch}")

            current_template_to_use = tpl
            if not current_template_to_use and stage:
                tpl_info = get_templates(stage, vt)
                current_template_to_use = tpl_info.get("default_template")
            
            # LOOP LOGIC
            is_batch_type = abbrev in ["D1", "SC", "AD", "AW"]
            iter_items = [] 
            
            if is_batch_type:
                for i in range(batch): 
                    iter_items.append( ("NONE", i + 1) )
            else:
                if not levels: continue 
                for lvl in levels:
                    iter_items.append( (lvl, None) )

            for lvl, idx in iter_items:
                req_data = {
                    "command": "create_view", 
                    "stage": stage, 
                    "view_type": vt, 
                    "levels": [lvl], 
                    "template": current_template_to_use, 
                    "mode": "STANDARD",
                    "batch_index": idx,
                    "sheet_name": user_provided_name # Pass strictly for custom mode fallback
                }
                
                naming = apply(req_data, existing_views, existing_sheets)
                
                if naming and naming.get("views"):
                    generated_views = naming["views"]
                    for view in generated_views:
                        
                        # 💥 REVERT: We now TRUST the naming engine fully.
                        # We do NOT override with user_provided_name here.
                        final_name = view.get("name", "")
                        
                        view_item = {
                            "view_type": view.get("view_type", ""), 
                            "level": view.get("level", ""), 
                            "name": final_name, 
                            "template": naming.get("final_template", "") or view.get("template", ""),
                            "batch_index": idx
                        }
                        if final_scope_box_id: 
                            view_item["scope_box_id"] = final_scope_box_id
                        
                        final_view_items.append(view_item)
                        existing_views.append(final_name)

        if not final_view_items:
            return message("Nothing to create. Please check levels or view types.")

        # 3) CLEANUP
        request.session["ai_last_known_views"] = existing_views 
        
        request.session["ai_pending_request_data"] = None
        request.session["ai_pending_scope_box_id"] = None
        request.session["ai_scope_box_checked"] = False
        request.session.modified = True
        reset_pending(request)
        
        env = envelope_create_views(final_view_items)
        return send_envelope(request, env)

    except Exception as e:
        return Response({"message": f"Error in naming engine: {e}"})