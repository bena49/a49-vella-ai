// Executor/Commands/ExecuteBatchUpdateCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using A49AIRevitAssistant.Models; // Ensure this matches your namespace for Envelope

namespace A49AIRevitAssistant.Executor.Commands
{
    public class ExecuteBatchUpdateCommand
    {
        private readonly UIApplication _uiapp;

        public ExecuteBatchUpdateCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        // Sheet-renumber operation extracted from the updates JSON, with
        // before/after numbers and an optional name change.
        private class RenumberOp
        {
            public ViewSheet Sheet;
            public string OldNumber;
            public string NewNumber;
            public JObject Changes;
            public bool NeedsTemp;
        }

        public string Execute(RevitCommandEnvelope env)
        {
            Document doc = _uiapp.ActiveUIDocument.Document;
            StringBuilder report = new StringBuilder();
            int successCount = 0;
            int failCount = 0;

            // 1. EXTRACT UPDATES FROM RAW JSON
            if (env.raw == null || !env.raw.ContainsKey("updates"))
                return "❌ No updates found in payload.";

            JArray updates = env.raw["updates"] as JArray;
            if (updates == null || updates.Count == 0) return "ℹ No changes to apply.";

            // ────────────────────────────────────────────────────────────
            // PRE-PASS: separate sheet-renumbers from name-only/view ops
            // ────────────────────────────────────────────────────────────
            // Sheet-renumbers go through the swap-chain analysis below.
            // Everything else stays on the existing best-effort per-item path.
            var renumberOps = new List<RenumberOp>();
            var otherOps = new List<JObject>();

            foreach (JObject item in updates)
            {
                string uniqueId = item["unique_id"]?.ToString();
                string type = item["element_type"]?.ToString();
                JObject changes = item["changes"] as JObject;

                if (string.IsNullOrEmpty(uniqueId) || changes == null) continue;

                if (type == "SHEET" && changes.ContainsKey("number"))
                {
                    Element elem = doc.GetElement(uniqueId);
                    if (elem is ViewSheet sheet)
                    {
                        renumberOps.Add(new RenumberOp
                        {
                            Sheet = sheet,
                            OldNumber = sheet.SheetNumber,
                            NewNumber = changes["number"].ToString(),
                            Changes = changes
                        });
                        continue;
                    }
                    // Sheet not resolvable — fall through so the existing path reports it.
                }

                otherOps.Add(item);
            }

            // ────────────────────────────────────────────────────────────
            // VALIDATE: no final new_number may collide with a sheet that
            // is NOT part of this batch. We surface this BEFORE starting
            // the transaction so we never strand sheets at TEMP_ names.
            // ────────────────────────────────────────────────────────────
            if (renumberOps.Count > 0)
            {
                var batchSheetIds = new HashSet<ElementId>(renumberOps.Select(r => r.Sheet.Id));
                var existingNumbersOutsideBatch = new HashSet<string>(
                    new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .Where(s => !batchSheetIds.Contains(s.Id))
                        .Select(s => s.SheetNumber));

                foreach (var r in renumberOps)
                {
                    if (r.OldNumber == r.NewNumber) continue;
                    if (existingNumbersOutsideBatch.Contains(r.NewNumber))
                    {
                        return $"❌ Cannot apply: sheet number '{r.NewNumber}' is already in use by a sheet not in this batch.";
                    }
                }
            }

            // ────────────────────────────────────────────────────────────
            // CHAIN DETECTION
            // ────────────────────────────────────────────────────────────
            // A renumber participates in a swap chain (and therefore needs
            // the temp pass) when either:
            //   • my new_number is also someone else's current number, OR
            //   • my old_number is also someone else's target.
            // Both directions are needed to catch linear chains like
            //   1010→1020, 1020→1030, 1030→1040  (the tail end is only
            //   caught by the second condition).
            // ────────────────────────────────────────────────────────────
            var oldNumberSet = new HashSet<string>(renumberOps.Select(r => r.OldNumber));
            var newNumberSet = new HashSet<string>(renumberOps.Select(r => r.NewNumber));
            foreach (var r in renumberOps)
            {
                if (r.OldNumber == r.NewNumber) { r.NeedsTemp = false; continue; }
                r.NeedsTemp = oldNumberSet.Contains(r.NewNumber)
                           || newNumberSet.Contains(r.OldNumber);
            }

            // ────────────────────────────────────────────────────────────
            // APPLY (single transaction so Ctrl+Z rolls back atomically)
            // ────────────────────────────────────────────────────────────
            try
            {
                using (Transaction tx = new Transaction(doc, "Vella - Batch Wizard Update"))
                {
                    tx.Start();

                    // Phase 1: park every chain participant at a guaranteed-
                    // unique TEMP_ number so Phase 2 can assign the final
                    // numbers freely without tripping Revit's uniqueness
                    // constraint mid-batch.
                    try
                    {
                        foreach (var r in renumberOps)
                        {
                            if (!r.NeedsTemp) continue;
                            r.Sheet.SheetNumber = $"TEMP_{r.Sheet.Id.Value}";
                        }
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        return $"❌ Failed during temp-rename phase: {ex.Message}";
                    }

                    // Phase 2: assign final SheetNumber + Name for every
                    // renumber op (chain or not). If any final assignment
                    // fails we roll back the entire transaction so we never
                    // leave sheets stranded at TEMP_ names.
                    foreach (var r in renumberOps)
                    {
                        try
                        {
                            if (r.OldNumber != r.NewNumber)
                                r.Sheet.SheetNumber = r.NewNumber;

                            if (r.Changes.ContainsKey("name"))
                            {
                                string newName = r.Changes["name"].ToString();
                                if (r.Sheet.Name != newName) r.Sheet.Name = newName;
                            }
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            tx.RollBack();
                            return $"❌ Failed renaming sheet '{r.OldNumber}' → '{r.NewNumber}': {ex.Message}\n   Transaction rolled back to prevent stranded TEMP_ numbers.";
                        }
                    }

                    // Phase 3: name-only sheet ops + all view ops
                    // (existing best-effort per-item logic, unchanged)
                    foreach (JObject item in otherOps)
                    {
                        string uniqueId = item["unique_id"]?.ToString();
                        JObject changes = item["changes"] as JObject;

                        if (string.IsNullOrEmpty(uniqueId) || changes == null) continue;

                        Element elem = doc.GetElement(uniqueId);
                        if (elem == null)
                        {
                            report.AppendLine($"• [FAIL] ID {uniqueId} not found.");
                            failCount++;
                            continue;
                        }

                        try
                        {
                            // A. SHEET (name-only — number changes were handled in Phase 2)
                            if (elem is ViewSheet sheet)
                            {
                                if (changes.ContainsKey("name"))
                                {
                                    string newName = changes["name"].ToString();
                                    if (sheet.Name != newName) sheet.Name = newName;
                                }
                            }
                            // B. RENAME VIEW
                            else if (elem is View view)
                            {
                                if (changes.ContainsKey("name"))
                                {
                                    string newName = changes["name"].ToString();
                                    if (view.Name != newName) view.Name = newName;
                                }
                                if (changes.ContainsKey("title_on_sheet"))
                                {
                                    string newTitle = changes["title_on_sheet"].ToString();
                                    Parameter pTitle = view.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION);
                                    if (pTitle == null) pTitle = view.LookupParameter("Title on Sheet");

                                    if (pTitle != null && !pTitle.IsReadOnly)
                                    {
                                        pTitle.Set(newTitle);
                                    }
                                }
                            }

                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            report.AppendLine($"• [FAIL] {elem.Name}: {ex.Message}");
                            failCount++;
                        }
                    }

                    tx.Commit();
                }

                if (failCount == 0)
                    return $"✔ Successfully updated {successCount} items.";
                else
                    return $"⚠ Updated {successCount} items. {failCount} failed:\n{report}";
            }
            catch (Exception ex)
            {
                return $"❌ Critical Error in Batch Update: {ex.Message}";
            }
        }
    }
}
