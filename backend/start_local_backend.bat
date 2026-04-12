@echo off
cd /d "C:\DEV Projects\Vella-AI\backend"

echo Activating Virtual Environment...
call venv\Scripts\activate.bat

echo Starting Vella AI Backend (Local Dev Server)...
python manage.py runserver

pause