@echo off
title Build and Deploy Vella Frontend
echo ===================================================
echo 1. Building Nuxt Frontend (pnpm run generate)...
echo ===================================================
echo.

:: Navigate to your project folder
cd /d "D:\xampp\webapps\irisaiassistant"

:: Run the build command (using 'call' so the script doesn't exit)
call pnpm run generate

:: Check if the build actually succeeded before trying to copy
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ❌ ERROR: Build failed! Deployment aborted.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ===================================================
echo 2. Deploying to XAMPP...
echo ===================================================
echo.

:: Run Robocopy with the Mirror flag
robocopy "D:\xampp\webapps\irisaiassistant\.output\public" "D:\xampp\htdocs\irisaiassistant" /MIR /MT:8 /R:2 /W:2

:: Robocopy exit codes: anything under 8 is generally a success.
if %ERRORLEVEL% GEQ 8 (
    echo.
    echo ❌ ERROR: Deployment failed. Please check the logs above.
) else (
    echo.
    echo ✔️ SUCCESS: Build and Deployment completed perfectly!
)

echo.
pause