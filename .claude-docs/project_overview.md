---
name: Vella-AI Project Overview
description: Architecture, tech stack, and key structure of the Revit AI assistant monorepo
type: project
originSessionId: f7663b11-c02d-4028-87ee-bee5ede39cd8
---
Vella AI is a three-tier Revit AI assistant monorepo at `d:\00_DEV Projects\a49-vella-ai`.

**Why:** Provides an AI-powered dockable panel inside Autodesk Revit for automating drafting tasks (sheets, views, tags, dimensions, naming).

**How to apply:** When working on any feature, understand which of the three layers is involved and how they communicate. For sheet-numbering work specifically, see `numbering_schemes.md` — the engine supports two schemes (V1 4-digit small / V2 5-digit large) selected per project.

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

## Sheet Numbering (dual-scheme, ISO19650)
`naming_engine.py` is config-driven via the `SCHEMES` dict. Two schemes ship today (user-facing labels in **bold**, internal keys in `code`):
- **ISO19650 4-digit** (`iso19650_4digit`): A0=0010, A1=1010, X0=X010 — A1 supports 1 site slot, B1-B9
- **ISO19650 5-digit** (`iso19650_5digit`): A0=00100, A1=10100, X0=X0100 — A1 supports 10 site slots, B1-B9 with +10 spacing, +9 sub-slots per level for mezzanine/transfer

Scheme is resolved per-request via `resolve_scheme_for_request(request)`:
1. Auto-detect from cached project sheets (5+ char number → 5-digit scheme)
2. Session override `ai_numbering_scheme = "iso19650_4digit" | "iso19650_5digit"`
3. Default `iso19650_4digit`

Users toggle via chat: `use iso19650 5-digit` / `use iso19650 4-digit` / `what numbering scheme` (plus generic `use 5-digit` / `use 4-digit` and Thai variants). See `numbering_schemes.md` for the full spec.

Sessions stored under the earlier `v1_small` / `v2_large` keys (pre-rename) are auto-migrated by `resolve_scheme_for_request()` — no breakage for existing users.
