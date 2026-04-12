## AI ASSISTANT
**BACKEND**
D:\xampp\webapps\irisaiassistant_backend

**FRONTEND**
D:\xampp\webapps\irisaiassistant

**CSHARP**
C:\Users\Bunda.A49local\OneDrive\00_REVIT AI Automation\A49AIRevitAssistant


## WAITRESS
start and Stop WinSW -Waitress with 
Run CMD as Administrator:
cd /d D:\xampp\webapps\irisaiassistant_backend

IRISAIBackendWaitress.exe restart 
IRISAIBackendWaitress.exe stop 
IRISAIBackendWaitress.exe start

or just click the .bat
AI_restart_waitress_service.bat
AI_start_waitress_service.bat
AI_stop_waitress_service.bat

# Postman Testing
POST
https://a49iris.com/irisai-api/api/ai/parse-command/

# Django run: 
python manage.py runserver 0.0.0.0:8001

## FRONTEND

# Frontend UI deploy:
(Build and copy to D:\xampp\htdocs\irisaiassistant)
pnpm run deploy

# Frontend UI build:
(Build to D:\xampp\webapps\irisaiassistant\.output\public)
PS D:\xampp\webapps\irisaiassistant> 
pnpm build 

# Run development server:
http://localhost:3000/irisaiassistant/
pnpm dev