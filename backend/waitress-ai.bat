@echo off
setlocal
cd /d D:\xampp\webapps\irisaiassistant-backend

rem --- DEVELOPMENT: loads .env, no SSL redirect ---
set DJANGO_SETTINGS_MODULE=ai_backend.settings
set PYTHONUNBUFFERED=1

rem --- ADDED: --threads=50 to handle up to 50 simultaneous user requests at the exact same millisecond
venv\Scripts\python.exe -m waitress ^
  --listen=127.0.0.1:8001 ^
  --threads=50 ^
  ai_backend.wsgi:application

endlocal
