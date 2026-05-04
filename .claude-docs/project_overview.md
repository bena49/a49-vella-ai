---
name: Vella-AI Project Overview
description: Architecture, tech stack, and key structure of the Revit AI assistant monorepo
type: project
originSessionId: f7663b11-c02d-4028-87ee-bee5ede39cd8
---
Vella AI is a three-tier Revit AI assistant monorepo at c:\DEV Projects\Vella-AI.

**Why:** Provides an AI-powered dockable panel inside Autodesk Revit for automating drafting tasks (sheets, views, tags, dimensions, naming).

**How to apply:** When working on any feature, understand which of the three layers is involved and how they communicate.

## Architecture

1. **Backend** (`/backend/`) — Python 3, Django 5.2, DRF 3.16, OpenAI API, Waitress (prod), Azure AD auth
   - `ai_router/ai_core/` — OpenAI/GPT integration, intent routing, session management
   - `ai_router/ai_commands/` — Command handlers (autodim, autotag, sheets, views, rename, preflight)
   - `ai_router/ai_engines/` — Prompt engineering, conversation engine, naming/math/level/scope box engines
   - `ai_router/ai_utils/` — Envelope builder, formatters, validators
   - `ai_router/views.py` — DRF API endpoints
   - `ai_router/auth.py` — Azure SSO

2. **Frontend** (`/frontend/`) — Vue 3, Nuxt 4, TypeScript, Tailwind CSS, Azure MSAL
   - Served as static SPA from XAMPP htdocs (production) or Nuxt dev server (local)
   - Embedded inside Revit via WebView2
   - `composables/useRevitBridge.ts` — WebView2 ↔ Revit communication
   - `composables/useChat.ts` — Chat UI, session, backend comms
   - `composables/useAuth.ts` — Azure MSAL token refresh
   - `components/wizards/` — Multi-step workflows (sheets, views, tags, elevations)

3. **Revit Add-in** (`/revit-addin/`) — C#, net48 (Revit 2024) + net8.0-windows (Revit 2025), WebView2, WPF
   - `Executor/DjangoBridge.cs` — HTTP bridge to Django backend
   - `Executor/CommandEventHandler.cs` — Revit API command dispatcher
   - `Executor/Commands/` — Individual Revit command implementations
   - `UI/` — Dockable pane hosting the Nuxt frontend via WebView2
   - `MainClass.cs` — Revit add-in entry point

## Communication Flow
Frontend (WebView2) ↔ C# Add-in (WebView2 bridge) → Django Backend (HTTP) → OpenAI API

## Build Configurations
- `RR2024`: net48, targets Revit 2024
- `RR2025`: net8.0-windows, targets Revit 2025

## Key Packages
- Backend: OpenAI 2.8.1, Pydantic 2.12.4, PyJWT 2.12.1
- Frontend: Nuxt 4.1.3, MSAL Browser 5.5.0, pnpm 10.23.0
- C#: Newtonsoft.Json 13.0.4, WebView2 1.0.2792.45, A49LicenseManager (custom DLL)
