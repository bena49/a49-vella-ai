// A49AIRevitAssistant/Executor/Commands/Sheets/RenumberSheetsCommand.cs

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace A49AIRevitAssistant.Executor.Commands.Sheets
{
    public class RenumberSheetsCommand
    {
        private readonly UIApplication _uiapp;

        public RenumberSheetsCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        public string Execute(string startNumber, string sourceRange = null)
        {
            if (string.IsNullOrWhiteSpace(startNumber)) return "❌ Start number is missing.";

            Document doc = _uiapp.ActiveUIDocument.Document;

            // 1. PARSE DESTINATION (e.g. "A9.05")
            // Regex: Group 1 (Prefix e.g. "A9."), Group 2 (Number e.g. "05")
            // We use a Lookahead (?=\d+$) to ensure we split at the very last number block.
            var match = Regex.Match(startNumber, @"^(.*?)(?=\d+$)(\d+)$");

            if (!match.Success) return $"❌ Could not parse format of '{startNumber}'. Expected format like 'A1.05'.";

            string destPrefix = match.Groups[1].Value; // "A9."
            string destStartStr = match.Groups[2].Value; // "05"
            int destCounter = int.Parse(destStartStr);

            try
            {
                using (TransactionGroup tg = new TransactionGroup(doc, "Vella - Renumber Sheets"))
                {
                    tg.Start();

                    // 2. COLLECT SHEETS (SMART FILTER)
                    List<ViewSheet> sheetsToRenumber = new List<ViewSheet>();

                    // Check if sourceRange looks like a range "A9.01 - A9.04"
                    var rangeMatch = Regex.Match(sourceRange ?? "", @"([A-Z0-9\.]+)[\s\-\to]+([A-Z0-9\.]+)", RegexOptions.IgnoreCase);

                    if (rangeMatch.Success)
                    {
                        // 💥 RANGE MODE: Only pick sheets between Start and End
                        string rangeStart = rangeMatch.Groups[1].Value; // A9.01
                        string rangeEnd = rangeMatch.Groups[2].Value;   // A9.04

                        // Heuristic: Filter strictly by string comparison
                        sheetsToRenumber = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewSheet))
                            .Cast<ViewSheet>()
                            .Where(s => string.Compare(s.SheetNumber, rangeStart, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                        string.Compare(s.SheetNumber, rangeEnd, StringComparison.OrdinalIgnoreCase) <= 0)
                            .OrderBy(s => s.SheetNumber)
                            .ToList();
                    }
                    else
                    {
                        // 💥 PREFIX MODE (Fallback): Pick all sheets matching the destination prefix
                        // If user just said "Renumber sheets to A9.05", assume we take existing A9.* sheets
                        sheetsToRenumber = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewSheet))
                            .Cast<ViewSheet>()
                            .Where(s => s.SheetNumber.StartsWith(destPrefix))
                            .OrderBy(s => s.SheetNumber)
                            .ToList();
                    }

                    if (!sheetsToRenumber.Any()) return $"❌ No sheets found to renumber.";

                    // 3. TEMP RENAME (Avoid Collisions)
                    using (Transaction t1 = new Transaction(doc, "Temp Rename"))
                    {
                        t1.Start();
                        foreach (var s in sheetsToRenumber)
                        {
                            s.SheetNumber = $"{s.SheetNumber}_TEMP_{Guid.NewGuid().ToString().Substring(0, 5)}";
                        }
                        t1.Commit();
                    }

                    // 4. FINAL RENAME
                    int processed = 0;
                    using (Transaction t2 = new Transaction(doc, "Final Renumber"))
                    {
                        t2.Start();
                        foreach (var s in sheetsToRenumber)
                        {
                            // Preserve leading zeros based on input (e.g. '05' -> length 2)
                            string newNumStr = destCounter.ToString().PadLeft(destStartStr.Length, '0');
                            s.SheetNumber = $"{destPrefix}{newNumStr}";

                            destCounter++;
                            processed++;
                        }
                        t2.Commit();
                    }

                    tg.Assimilate();
                    return $"✔ Renumbered {processed} sheets starting from {startNumber}.";
                }
            }
            catch (Exception ex)
            {
                return $"❌ Error renumbering sheets: {ex.Message}";
            }
        }
    }
}
