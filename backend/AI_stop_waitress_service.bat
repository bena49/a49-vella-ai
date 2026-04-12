@echo off
cd /d D:\xampp\webapps\irisaiassistant_backend

echo Stopping IRIS AI Backend Waitress service...
IRISAIBackendWaitress.exe stop

echo.
pause
