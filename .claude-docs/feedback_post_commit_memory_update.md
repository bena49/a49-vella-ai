---
name: After every final commit cluster, update .claude-docs
description: Standard practice — when the user finishes a feature and commits to GitHub, write a `shipped_YYYY_MM_DD_<theme>.md` doc capturing what landed so the next session (often from a different machine) is aligned
type: feedback
---

When the user signals a session is done — typically with a confirmation message ("perfect", "you did it", "thank you") followed by a final `git commit` round — proactively offer to update `.claude-docs/` with a `shipped_YYYY_MM_DD_<theme>.md` doc summarising what was just shipped. The user works across multiple machines and pulls from GitHub; the docs are how the next session starts already aligned.

**Why:** User said *"Please make this a standard practice every time after I do a final comit to github. So next time from another place we can be both aligned."* on 2026-05-06 after the linked-model session. The existing `shipped_2026_05_06_phase2c_polish.md` (from the earlier session that day) and `shipped_2026_05_06_phase2b_linked_models.md` (from the linked-model session) demonstrate the pattern they want repeated.

**How to apply:**

1. **Trigger:** session-completion signals from the user (a "thank you" or "you did it" right after committing). Don't wait to be asked — proactively offer to update the docs.

2. **What to write:** one new file at `.claude-docs/shipped_YYYY_MM_DD_<short_theme>.md` with frontmatter:
   ```
   ---
   name: Shipped YYYY-MM-DD — <short theme>
   description: <one-line summary of what landed>
   type: project
   ---
   ```
   Followed by `**Shipped:**` sections grouped by feature, then `**What's still pending:**`, then `**How to apply:**` (usually pointing the next session at `git log --since=…` as the authoritative changelog).

3. **Filename convention:** `shipped_YYYY_MM_DD_<theme>.md`. Use the date of the final commit cluster (per `git log -1 --format=%ai`), not necessarily today's wall clock. If multiple sessions ship on the same day, append a distinguishing theme to keep filenames unique (e.g. `shipped_2026_05_06_phase2c_polish.md` and `shipped_2026_05_06_phase2b_linked_models.md`).

4. **What goes in:** for each shipped feature, name the entry-point files modified (so the next session can `Read` them quickly), capture WHY (the design constraint that drove the choice — not just WHAT), and call out anything that's a regression-risk landmine (e.g. "the dedup set is seeded ONCE — adding mid-loop is the bug we hit").

5. **What NOT to put in:** mechanical change lists already in `git log` ("changed line 42 from X to Y"). The diff is the source of truth for that. Memory is for non-obvious context.

6. **Keep CLAUDE.md current:** when the shipped doc lands, scan CLAUDE.md to see if any pointer or terminology needs updating. The release version line is a common one to bump.

7. **Move next-release plans to "shipped" status:** if a `next_release_<feature>.md` plan was the source for what just shipped, either delete the plan or note in the new shipped doc that it superseded the plan. Don't leave stale plans pointing at landed code.

8. **Pending items get carried forward:** the "What's still pending" section in each shipped doc is how the next session sees what's left in the queue without having to read every `next_release_*.md`.
