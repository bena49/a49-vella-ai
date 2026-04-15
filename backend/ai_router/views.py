# =====================================================================
# Vella Backend (views.py) — Slim Entry Point
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