# =====================================================================
# Vella Backend (views.py) — Slim Entry Point with Modular Architecture
# ai_router/views.py
# =====================================================================

import sys, io, importlib
from rest_framework.decorators import api_view
from rest_framework.response import Response
from rest_framework import status
from django.views.decorators.csrf import csrf_exempt
from django.conf import settings
from .auth import require_azure_token

# Force UTF-8 encoding
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8', errors='replace')

# ---------------------------------------------------------------------
# MODULE IMPORTS
# ---------------------------------------------------------------------
from .ai_core.session_manager import (
    initialize_session, debug_session, reset_pending
)
from .ai_core.intent_router import finalize_router, process_intent
from .ai_core.callback_handler import handle_revit_callbacks
from .ai_commands.preflight import handle_preflight_repair_interceptor
from .ai_commands.automate_tag_nlp import handle_nlp_tag_conversation
from .ai_commands.automate_dim import handle_automate_dim
from .ai_engines.math_engine import process_math_and_conversions
from .ai_engines.conversation_engine import process_conversational_intent

engine = importlib.import_module(settings.SESSION_ENGINE)

# =====================================================================
# MAIN API ENTRYPOINT
# =====================================================================

@csrf_exempt
@api_view(['POST'])
@require_azure_token
def ai_router(request):
    passed_session_key = request.data.get("session_key")
    if passed_session_key and request.session.session_key != passed_session_key:
        from django.contrib.sessions.models import Session
        if Session.objects.filter(session_key=passed_session_key).exists():
            request.session = engine.SessionStore(session_key=passed_session_key)
            debug_session(request, f"🔄 Session recovered: {passed_session_key[:8]}")

    initialize_session(request)
    if not request.session.session_key: request.session.create()

    try:
        raw_text_original = request.data.get("message", "").strip()
        raw_text_lower = raw_text_original.lower()
        
        # ==========================================================
        # 0. CONFIRMATION INTERCEPTOR (Safeguard)
        # ==========================================================
        if request.session.get("ai_expecting_renumber_confirmation"):
            if raw_text_lower in ["yes", "y", "confirm", "proceed", "sure", "ok"]:
                debug_session(request, "🛡️ Safeguard: User CONFIRMED renumbering.")
                request.session["ai_renumber_confirmed"] = True
                request.session["ai_expecting_renumber_confirmation"] = False
                request.session.modified = True
                from .ai_commands.modifier import finalize_modification_command
                return finalize_modification_command(request)
            
            elif raw_text_lower in ["no", "n", "cancel", "stop", "don't"]:
                debug_session(request, "🛡️ Safeguard: User CANCELLED renumbering.")
                reset_pending(request)
                request.session["ai_expecting_renumber_confirmation"] = False
                return Response({"message": "❌ Cancelled. The sheets were not renumbered."})
        
        # 💥 CLEAN TEXT (used by interceptors below)
        clean_text = raw_text_lower.strip(".!, ")

        # ==========================================================
        # 0.6) PREFLIGHT REPAIR INTERCEPTOR
        # ==========================================================
        preflight_resp = handle_preflight_repair_interceptor(request, clean_text)
        if preflight_resp:
            return preflight_resp

        # ==========================================================
        # 0.7) NLP TAG CONVERSATION INTERCEPTOR
        # ==========================================================
        nlp_tag_resp = handle_nlp_tag_conversation(request, raw_text_original)
        if nlp_tag_resp:
            return nlp_tag_resp

        # ==========================================================
        # 0.8) TEMPLATE SELECTION INTERCEPTOR
        # When `check_template_requirements` emits the "Multiple
        # template/s found" prompt it also sets
        # ai_expecting_template_selection. Catch the user's reply
        # here so it never reaches GPT (which misclassifies it as a
        # fresh "create floor plan" prompt and loops).
        # ==========================================================
        if request.session.get("ai_expecting_template_selection"):
            options = request.session.get("ai_pending_template_options") or []
            cleaned = raw_text_original.strip()

            # Cancel
            if cleaned.lower() in ["cancel", "stop", "nevermind", "never mind", "quit", "exit"]:
                debug_session(request, "🛡️ Template selection: user cancelled.")
                reset_pending(request)
                request.session["ai_expecting_template_selection"] = False
                request.session["ai_pending_template_options"] = None
                request.session.modified = True
                return Response({"message": "❌ Cancelled. No template selected."})

            # Numeric index (1-based)
            chosen = None
            try:
                idx = int(cleaned) - 1
                if 0 <= idx < len(options):
                    chosen = options[idx]
            except ValueError:
                pass

            # Exact (case-insensitive) string match
            if chosen is None:
                for opt in options:
                    if opt.upper() == cleaned.upper():
                        chosen = opt
                        break

            if chosen:
                debug_session(request, f"🛡️ Template selection: user chose '{chosen}'.")
                request.session["ai_pending_template"] = chosen
                request.session["ai_expecting_template_selection"] = False
                request.session["ai_pending_template_options"] = None
                request.session.modified = True
                # Resume the create flow with the resolved template — no GPT call.
                return finalize_router(request)

            # Escape hatch: user appears to be starting a fresh command rather
            # than replying to the prompt (e.g. "Create L1 Floor Plan…"). Clear
            # the stale flag and let normal routing handle the new intent.
            import re as _re
            if _re.match(r'^\s*(create|make|add|build|generate|new|open|browse|insert|show|run|preflight|tag|dimension|refresh|reload|update|sync)\b',
                         cleaned, _re.IGNORECASE):
                debug_session(request, "🛡️ Template selection: user typed a new command — clearing flag.")
                request.session["ai_expecting_template_selection"] = False
                request.session["ai_pending_template_options"] = None
                request.session.modified = True
                # Fall through (no return) — normal routing handles the new command.
            else:
                # Reply didn't match any option AND isn't a new command — re-prompt.
                option_list = "\n".join([f"{i+1}. {tpl}" for i, tpl in enumerate(options)])
                return Response({
                    "message": (
                        f"I didn't catch that. Please reply with the number "
                        f"(1–{len(options)}) or the exact template name:\n{option_list}\n"
                        f"Or type 'cancel' to abort."
                    )
                })

        # ==========================================================
        # 0.9) TITLEBLOCK SELECTION INTERCEPTOR
        # Same shape as template selection above. When a sheet-creation
        # flow asks "Please choose a titleblock", it sets
        # ai_expecting_titleblock_selection + stores the option list.
        # Catch the user's reply here so it never reaches GPT (which
        # often fails to recognise the long titleblock string as the
        # answer to the previous question and re-prompts in a loop).
        # ==========================================================
        if request.session.get("ai_expecting_titleblock_selection"):
            tb_options = request.session.get("ai_pending_titleblock_options") or []
            cleaned = raw_text_original.strip()

            # Cancel
            if cleaned.lower() in ["cancel", "stop", "nevermind", "never mind", "quit", "exit"]:
                debug_session(request, "🛡️ Titleblock selection: user cancelled.")
                reset_pending(request)
                request.session["ai_expecting_titleblock_selection"] = False
                request.session["ai_pending_titleblock_options"] = None
                request.session.modified = True
                return Response({"message": "❌ Cancelled. No titleblock selected."})

            # Numeric index (1-based)
            chosen = None
            try:
                idx = int(cleaned) - 1
                if 0 <= idx < len(tb_options):
                    chosen = tb_options[idx]
            except ValueError:
                pass

            # Exact (case-insensitive) string match. The titleblock string may
            # contain " : " separators — normalise spacing for tolerant compare.
            if chosen is None:
                def _norm(s):
                    return " ".join(str(s).split()).lower()
                cleaned_norm = _norm(cleaned)
                for opt in tb_options:
                    if _norm(opt) == cleaned_norm:
                        chosen = opt
                        break

            if chosen:
                debug_session(request, f"🛡️ Titleblock selection: user chose '{chosen}'.")
                request.session["ai_pending_titleblock"] = chosen
                # Also pre-parse family/type so downstream code doesn't re-ask
                from .ai_engines.titleblock_engine import parse_titleblock_from_user_text
                fam, t_type = parse_titleblock_from_user_text(chosen)
                if fam and t_type:
                    request.session["titleblock_family"] = fam
                    request.session["titleblock_type"] = t_type
                request.session["ai_expecting_titleblock_selection"] = False
                request.session["ai_pending_titleblock_options"] = None
                request.session.modified = True
                # Resume the create flow with the resolved titleblock — no GPT call.
                return finalize_router(request)

            # Escape hatch: user typed a fresh command rather than answering.
            import re as _re
            if _re.match(r'^\s*(create|make|add|build|generate|new|open|browse|insert|show|run|preflight|tag|dimension|refresh|reload|update|sync)\b',
                         cleaned, _re.IGNORECASE):
                debug_session(request, "🛡️ Titleblock selection: user typed a new command — clearing flag.")
                request.session["ai_expecting_titleblock_selection"] = False
                request.session["ai_pending_titleblock_options"] = None
                request.session.modified = True
                # Fall through to normal routing
            else:
                option_list = "\n".join([f"{i+1}. {opt}" for i, opt in enumerate(tb_options)])
                return Response({
                    "message": (
                        f"I didn't catch that. Please reply with the number "
                        f"(1–{len(tb_options)}) or the exact titleblock name:\n{option_list}\n"
                        f"Or type 'cancel' to abort."
                    )
                })

        # ==========================================================
        # 0.95) DUPLICATE-CHOICE INTERCEPTOR
        # When sheet_creator or batch_processor returns the "⚠ Sheets
        # already exist" prompt, it sets ai_expecting_duplicate_choice.
        # We catch the user's reply here — BEFORE GPT — for the same
        # reason as titleblock/template interceptors above: GPT
        # otherwise misclassifies short keyword replies as fresh
        # commands and the user gets re-prompted in a loop.
        #
        # Recognised replies (case-insensitive):
        #   "cancel" / "abort" / "stop" / "nevermind"        → cancel
        #   "skip" / "skip duplicates" / "skip dup"          → skip
        #   "sub-parts" / "subparts" / "create as sub-parts" → subparts
        # ==========================================================
        if request.session.get("ai_expecting_duplicate_choice"):
            import re as _re
            cleaned = raw_text_original.strip().lower()
            choice = None
            if _re.search(r'\b(cancel|abort|stop|nevermind|never\s*mind)\b', cleaned):
                choice = "cancel"
            elif _re.search(r'\b(sub[\s\-]?parts?)\b', cleaned) or "create as sub" in cleaned:
                choice = "subparts"
            elif _re.search(r'\bskip(\s+dup\w*)?\b', cleaned):
                choice = "skip"

            if choice:
                debug_session(request, f"🛡️ Duplicate-choice interceptor: '{cleaned}' → {choice}")
                request.session["ai_duplicate_choice"] = choice
                request.session["ai_expecting_duplicate_choice"] = False
                request.session.modified = True
                # Resume the create flow with the resolved choice — no GPT call.
                return finalize_router(request)

            # Escape hatch — user typed a fresh command rather than a choice.
            import re as _re2
            if _re2.match(
                r'^\s*(create|make|add|build|generate|new|open|browse|insert|show|run|preflight|tag|dimension|refresh|reload|update|sync)\b',
                cleaned, _re2.IGNORECASE):
                debug_session(request, "🛡️ Duplicate-choice: user typed a new command — clearing expectation.")
                request.session["ai_expecting_duplicate_choice"] = False
                request.session["ai_pending_duplicate_info"] = None
                request.session.modified = True
                # Fall through to normal GPT routing.
            else:
                # Re-prompt with the same options so the user sees what's expected.
                return Response({
                    "message": (
                        "I didn't catch that. Please reply with one of:\n"
                        "  • ** cancel ** — abort, no sheets created\n"
                        "  • ** skip ** — only create sheets for new (non-duplicate) levels\n"
                        "  • ** sub-sheets ** — create duplicates as sub-parts of the existing sheets"
                    )
                })

        # 💥 BULLETPROOF "NO" INTERCEPTOR
        if clean_text in ["no", "none", "skip", "n", "nope", "cancel"]:
            current_intent = request.session.get("ai_pending_intent")
            if current_intent in ["create_view", "create_and_place"]:
                request.session["ai_pending_scope_box_id"] = "SKIP"
                request.session.modified = True
                return finalize_router(request)
            
        # ==========================================================
        # 0.5) CONVERSATIONAL INTERCEPTOR
        # ==========================================================
        conv_response = process_conversational_intent(raw_text_lower, request)
        if conv_response:
            return conv_response
        
        # ==========================================================
        # 1) Handle Revit Callbacks
        # ==========================================================
        callback_resp = handle_revit_callbacks(request, finalize_router)
        if callback_resp:
            return callback_resp

        # ==========================================================
        # 3) NONE Short-Circuit
        # ==========================================================
        if raw_text_original.strip().upper() == "NONE":
            request.session["ai_pending_stage"] = "NONE"
            request.session.modified = True
            return finalize_router(request)
        
        # ==========================================================
        # 3.5) ARCHITECTURAL MATH & CONVERSION SHORT-CIRCUIT
        # ==========================================================
        math_response = process_math_and_conversions(raw_text_lower, request.session.session_key)
        if math_response:
            return math_response

        # ==========================================================
        # 4) Intent Processing (GPT + Immediate Commands + Router)
        # ==========================================================
        return process_intent(request, raw_text_original)

    except Exception as ex:
        print(f"CRITICAL ERROR: {ex}")
        return Response({"error": f"Backend exception: {ex}"}, status=status.HTTP_500_INTERNAL_SERVER_ERROR)