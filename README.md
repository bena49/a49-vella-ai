Vella AI: Revit Assistant
This repository contains the complete, unified source code for Vella AI, A49's custom Revit AI Assistant. It is structured as a monorepo containing the Django Backend, the Nuxt/Vue Frontend, and the C# Revit Add-in.

📂 Repository Structure
/backend - Python/Django REST API and AI routing.

/frontend - Vue/Nuxt user interface.

/revit-addin - C# source code for the Revit integration.

💻 Local Development (Home / Work PC)
Do not make code changes directly on the production server. Always push changes to GitHub first.

1. Backend (Django)
Path: \backend

Virtual Environment: venv\Scripts\activate

Run Server: python manage.py runserver (Defaults to http://127.0.0.1:8000)

Note: Ensure your local .env file is present in the backend folder.

2. Frontend (Nuxt/Vue)
Path: \frontend

Run Dev Server: pnpm run dev (Defaults to http://localhost:3000)

🌐 Production Server (XAMPP)
The live application is hosted on the A49 XAMPP server.

Master Monorepo Path: D:\xampp\webapps\a49-vella-ai

Live Frontend Hosting: D:\xampp\htdocs\irisaiassistant

🚀 How to Deploy Updates
Deployment is automated via batch scripts located in the root folder (D:\xampp\webapps\a49-vella-ai).
Always run them in this order:

Double-click 1_Deploy_Backend.bat

Pulls the latest code from GitHub and instantly restarts the Waitress service.

Double-click 2_Deploy_Frontend.bat

Compiles the latest Nuxt code and securely Robocopies it to the live htdocs folder.

⚙️ Waitress Service Management (Backend)
In production, the backend is kept alive using a Windows Service wrapper (WinSW) running on port 8001 with 50 threads.

To manage the service, use the shortcut scripts located in the master repository root (D:\xampp\webapps\a49-vella-ai):

AI_restart_waitress_service.bat

AI_stop_waitress_service.bat

AI_start_waitress_service.bat

Alternatively, run as Administrator in CMD from the backend folder:
IRISAIBackendWaitress.exe restart | stop | start

🧪 Postman API Testing
To test the live AI parsing endpoint, send a POST request to:
https://a49iris.com/irisai-api/api/ai/parse-command/