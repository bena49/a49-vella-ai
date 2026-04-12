from openai import OpenAI
import os
import json
from rest_framework.response import Response  # 💥 ADDED THIS IMPORT

client = OpenAI(api_key=os.getenv("OPENAI_API_KEY"))

# This prompt ensures the JSON drafted by GPT gets repaired into valid JSON.
VALIDATOR_PROMPT = """
You will receive raw JSON-like output from a model. 
Your job is to:

1. Fix malformed JSON  
2. Ensure it is valid JSON the client can parse  
3. Do NOT change values unless required to fix the structure  
4. ALWAYS output ONLY valid JSON. 
5. NEVER wrap in code fences. Never add explanations.

Return only the cleaned JSON.
"""


def validate_json_with_ai(raw_text):
    """
    Takes ANY GPT output and ensures it becomes valid JSON.
    """

    try:
        # First: check if already valid JSON
        return json.loads(raw_text)
    except Exception:
        pass  # Not valid — send to GPT for repair

    try:
        completion = client.chat.completions.create(
            model="gpt-4o-mini",
            messages=[
                {"role": "system", "content": VALIDATOR_PROMPT},
                {"role": "user", "content": raw_text},
            ],
            temperature=0,
        )

        cleaned = completion.choices[0].message.content

        # Try parsing again
        return json.loads(cleaned)

    except Exception as e:
        return {"error": f"JSON validation failed: {str(e)}"}


# =====================================================================
# 💥 NEW: VIEW SUPPORT VALIDATOR (Fixes the Import Error)
# =====================================================================
def validate_view_support(request):
    """
    Checks if the pending view type is supported for creation.
    Returns a Response object if an error exists, otherwise None.
    """
    vtype = request.session.get("ai_pending_view_type")
    
    if not vtype:
        return None # No view type to validate yet
        
    # Example Restriction: Block '3D View' if you don't support it yet
    if "3D" in vtype.upper():
        return Response({
            "message": f"Sorry, I cannot create '{vtype}' yet. I only support 2D views (Plans, Elevations, Sections)."
        })

    return None # Validation passed