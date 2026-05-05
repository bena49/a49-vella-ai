---
name: User prefers phased methodical work in mature features
description: Late-stage / mature areas → propose phased rollout, get explicit scope confirmation, run tests between each phase
type: feedback
---

For features touching mature/already-shipped areas (especially naming, numbering, sheet creation, duplicate detection — anywhere a regression would be highly visible), propose a phase-by-phase plan, get scope confirmation BEFORE implementing, and run/verify tests between phases. Don't combine phases just to save time.

**Why:** During the duplicate-handling rollout (2026-05-06) the user said: *"Lets do this very methodically so as to not break anything that is already working. We have come so far and very close to the finish line."* This came after the Phase 2 wizard pipeline had already shipped extensive features and any regression would be highly visible to staff using the tool day-to-day. Earlier in the same session the user also asked for analysis-before-implementation when they said *"Please advise"* about the duplicate-handling design — they wanted to confirm scope/edge-cases first rather than getting a fait-accompli implementation.

**How to apply:** Before any change spanning 4+ files OR touching naming/numbering/sheet-creation/duplicate-handling logic:
1. Write a numbered phase plan listing files touched + risk per phase + test checkpoint per phase. Use the TodoWrite tool to track.
2. Ask the user to confirm scope explicitly, especially edge-case decisions (e.g. *"basement has no sub-slot room — fall back to skip with notice, or fail the whole batch?"*).
3. Run the relevant test suite **between** phases — never batch verifications. The mid-stream test runs are the safety net that catches regressions before the next layer is built on top.
4. Each phase should be small enough that a failing test can be diagnosed without unwinding multiple layers.

**Style preferences observed:**
- User reports test outcomes precisely — copies the actual chat output verbatim with `-->` annotations on what's wrong. Match this precision when describing what changed.
- User confirms with short replies ("first option please", "Please proceed with all three") once scope is clear. Don't keep asking when they've already answered.
- "Please advise" / "Please analyze" → wants analysis + recommendation + tradeoffs, NOT immediate code changes.
