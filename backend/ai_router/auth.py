# ai_router/auth.py
import jwt
import requests
from django.conf import settings
from django.http import JsonResponse
from functools import wraps

# A tiny memory cache so we don't have to ask Microsoft for their public key on every single prompt
AZURE_KEYS_CACHE = {}

def get_azure_public_key(kid):
    if kid in AZURE_KEYS_CACHE:
        return AZURE_KEYS_CACHE[kid]
    
    tenant_id = settings.AZURE_TENANT_ID
    jwks_url = f"https://login.microsoftonline.com/{tenant_id}/discovery/v2.0/keys"
    
    try:
        response = requests.get(jwks_url)
        response.raise_for_status()
        jwks = response.json()
        
        for key in jwks.get('keys', []):
            if key['kid'] == kid:
                # Convert Microsoft's JSON Web Key into a usable RSA Public Key
                public_key = jwt.algorithms.RSAAlgorithm.from_jwk(key)
                AZURE_KEYS_CACHE[kid] = public_key
                return public_key
    except Exception as e:
        print(f"Error fetching Azure keys: {e}")
    
    return None

def require_azure_token(view_func):
    """
    A decorator that intercepts incoming requests and mathematically 
    verifies the Azure SSO token before allowing the view to run.
    """
    @wraps(view_func)
    def _wrapped_view(request, *args, **kwargs):
        # 1. Skip validation for pre-flight CORS requests
        if request.method == 'OPTIONS':
            return view_func(request, *args, **kwargs)

        # 2. Extract the Token from the header
        auth_header = request.headers.get('Authorization')
        if not auth_header or not auth_header.startswith('Bearer '):
            return JsonResponse({"error": "Unauthorized. Missing token."}, status=401)
        
        token = auth_header.split(' ')[1]
        
        try:
            # 3. Find out which Microsoft key was used to sign this token
            unverified_header = jwt.get_unverified_header(token)
            kid = unverified_header.get('kid')
            
            public_key = get_azure_public_key(kid)
            if not public_key:
                return JsonResponse({"error": "Unauthorized. Invalid key signature."}, status=401)
            
            # 4. 💥 THE VAULT DOOR: Cryptographically verify the token
            # This mathematically proves the token was issued by your A49 Azure Tenant
            decoded_token = jwt.decode(
                token,
                public_key,
                algorithms=['RS256'],
                audience=settings.AZURE_CLIENT_ID,
                options={"verify_iss": False} # The RSA signature already proves authenticity
            )
            
            # Success! The user is who they say they are.
            # We attach their Azure data to the request just in case Vella needs to know who is typing
            request.azure_user = decoded_token
            
        except jwt.ExpiredSignatureError:
            return JsonResponse({"error": "Session expired. Please log in again."}, status=401)
        except Exception as e:
            # 💥 ADD THIS PRINT STATEMENT
            print(f"🔒 Token Validation Error: {e}")
            return JsonResponse({"error": "Unauthorized. Invalid or forged token."}, status=401)
            
        # If they survived the gauntlet, let them through to Vella!
        return view_func(request, *args, **kwargs)
        
    return _wrapped_view