---
name: Next-release plan — Revit Formula Helper
description: Deferred from Monday 2026-05-04 launch. New help tab to demystify Revit family formula syntax for staff with basic knowledge.
type: project
originSessionId: f7663b11-c02d-4028-87ee-bee5ede39cd8
---
Deferred feature for the release after 2026-05-04 (Spot Elevations + Help redesign launch).

**Problem:** Revit family formula syntax is a major pain point for staff with basic knowledge. IF/AND/OR function syntax, unit handling rules, type conversions, radians for trig, etc.

**Approach decided:** Two-phase build inside a new "Revit Formulas" tab in the Help modal.

**Phase 1 (week of 2026-05-05 to 2026-05-09): Formula Cheat Sheet**
- Categorised, copy-paste-ready formulas
- Each entry: plain-English title, formula syntax, 1-line explanation
- Categories: Conditional Logic, Math & Modules, Unit Handling, Yes/No Logic, Trigonometry, Arrays, Common Family Patterns
- Effort: ~3-4 hours, pure content, no backend
- Solves ~80% of staff pain immediately

**Phase 2 (mid-to-late May 2026): NL → Formula Translator**
- GPT-powered conversion: user types plain English ("if width > 1500mm and length > 2400mm divide by 2") → returns Revit formula syntax + explanation + common pitfalls
- New backend handler, system prompt tuning, frontend form
- Reuses existing OpenAI integration + `submitDirect` plumbing
- Effort: ~6-8 hours
- Pilot to one architect before broad rollout

**Skipped:** Visual block-based formula builder (Scratch-style) — overkill for help modal.

**Why:** Adding a new feature on top of the Monday launch was deemed too risky. Math calculator was the right priority for Monday; formulas become the headline of the next release.
