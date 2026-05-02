# ai_router/ai_commands/comment.py
# Handles the "Send a Comment" form from the Help & Command modal.
# Submits via Office 365 SMTP using the EMAIL_* settings in settings.py.

import logging
import socket

from django.conf import settings
from django.core.mail import send_mail
from rest_framework.response import Response

from ..ai_core.session_manager import debug_session

logger = logging.getLogger(__name__)

VALID_CATEGORIES = {'Bug', 'Feature Request', 'Question', 'Other'}
MIN_LEN = 5
MAX_LEN = 2000


def _result(status, message, **extra):
    """Wrap a result so the frontend can handle it from data.send_comment_result."""
    payload = {'status': status, 'message': message}
    payload.update(extra)
    return Response({'send_comment_result': payload})


def handle_send_comment(request):
    """
    Immediate command handler for the 'send_comment' intent.

    Expected request.data keys:
        - category:  str — one of VALID_CATEGORIES
        - body:      str — message text (5-2000 chars)
        - user_name: str — display name from MSAL (optional, for attribution)
        - session_key: str
    """
    category  = (request.data.get('category')  or '').strip()
    body      = (request.data.get('body')      or '').strip()
    user_name = (request.data.get('user_name') or '').strip() or '(unknown user)'

    # ── Validation ─────────────────────────────────────────────────────
    if category not in VALID_CATEGORIES:
        return _result('error', f"Invalid category '{category}'.")

    if len(body) < MIN_LEN:
        return _result('error', 'Message is too short.')
    if len(body) > MAX_LEN:
        return _result('error', f'Message exceeds {MAX_LEN} characters.')

    recipients = getattr(settings, 'VELLA_COMMENT_RECIPIENTS', None) or []
    if not recipients:
        return _result('error', 'No recipients configured. Set COMMENT_RECIPIENTS in the backend .env.')

    if not settings.EMAIL_HOST_USER or not settings.EMAIL_HOST_PASSWORD:
        return _result('error', 'SMTP credentials not configured on the server.')

    # ── Compose ────────────────────────────────────────────────────────
    subject = f"[Vella] {category} from {user_name}"
    text_body = (
        f"From:     {user_name}\n"
        f"Category: {category}\n"
        f"\n"
        f"{body}\n"
        f"\n"
        f"---\n"
        f"Sent via Vella AI · Help & Command · Comment\n"
    )

    debug_session(request, f"📧 Send comment: category={category}, len={len(body)}, to={','.join(recipients)}")

    # ── Send ───────────────────────────────────────────────────────────
    try:
        sent = send_mail(
            subject=subject,
            message=text_body,
            from_email=settings.DEFAULT_FROM_EMAIL,
            recipient_list=recipients,
            fail_silently=False,
        )
        if sent == 0:
            return _result('error', 'SMTP accepted but no email was sent.')
        return _result('success', 'Comment sent. Thank you!')
    except (socket.gaierror, ConnectionError, TimeoutError) as net_err:
        logger.exception('Comment send: network error')
        return _result('error', f'Network error reaching SMTP server: {net_err}')
    except Exception as e:
        logger.exception('Comment send: failed')
        return _result('error', f'Failed to send: {e}')
