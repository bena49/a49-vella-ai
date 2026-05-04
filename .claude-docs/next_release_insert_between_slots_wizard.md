---
name: Next-release wishlist — Insert-Between-Slots Sheet Wizard
description: User wants a wizard UI to weave a new sheet into an existing sequence (e.g. between 2010 and 2020 → 2011). Deferred during the 2026-05-04 numbering refactor.
type: project
originSessionId: f7663b11-c02d-4028-87ee-bee5ede39cd8
---
User explicitly deferred this during the sheet numbering refactor (2026-05-04). The core engine ALWAYS picks `max + 10` for sequence-based categories — never gap-fills automatically. Inserting between two existing slots is a deliberate user action.

**The gap**: Currently, if a user has sheets 2010, 2020, 2030 and wants to insert a new A2 sheet between 2010 and 2020, they have to:
1. Create the new sheet (gets 2040 by default)
2. Manually renumber it to 2011 in Revit

The wishlist: a wizard step that lets the user pick "insert after X" and the engine generates X+1 (e.g. 2011, 2012 …).

**Design notes from the original deferral**:
- No NLP needed — wizard-only feature (user's call)
- Backend already has the slot-finder primitive (`get_next_sheet_number`); just needs an extra `insert_after` parameter
- Frontend SheetWizard would need:
  * "Insert mode" toggle (or radio: Append / Insert after…)
  * A sheet picker showing existing sheets in the chosen category
  * Click-to-pick-anchor UX
- Backend needs to:
  * Accept `insert_after_slot` param
  * Try `slot+1, slot+2, …, slot+9` until free (10s reserved for next "row")
  * Reject if the +1..+9 band is exhausted ("no slot available — please renumber existing sheets first")

**Why deferred**: Core refactor was already a big surface change. Pilot users should validate the format before adding more wizard surface. May find users rarely insert (just renumber after the fact), making this work unnecessary.

**When to revisit**:
- After the team has used the new format for a few weeks
- If users complain about "I had to renumber manually" enough times
- If the renumber range command (which is already in Revit) doesn't cover the use case ergonomically

**Effort estimate**: ~1 day if user confirms scope. Mostly frontend (sheet picker + toggle); backend is a small extension.
