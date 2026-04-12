@echo off
cd /d D:\xampp\webapps\irisaiassistant_backend

echo Restarting IRIS AI Backend Waitress service...
IRISAIBackendWaitress.exe restart

echo.
pause
