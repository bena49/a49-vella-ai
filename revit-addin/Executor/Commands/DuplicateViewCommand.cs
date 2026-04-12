// ============================================================================
// DuplicateViewCommand.cs — Vella AI
// ----------------------------------------------------------------------------
// Duplicates a view by exact name lookup.
//
// Envelope format:
//
// {
//    "command": "duplicate_view",
//    "target": "<existing view name>",
//    "mode": "<with detailing>"     // optional
// }
//
// Behavior:
//   1. Duplicates the view (Revit API assigns a unique name like 'Original (Copy 1)').
//   2. Renames the new view to a cleaner format like 'Original Name (Copy)'.
//   3. Falls back to the API's unique name if the clean name is already taken.
// ============================================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class DuplicateViewCommand
    {
        private readonly UIApplication _uiapp;

        public DuplicateViewCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        // ============================================================================
        // EXECUTE
        // ============================================================================
        public string Execute(string targetName, string mode)
        {
            if (string.IsNullOrWhiteSpace(targetName))
                return "❌ Target view name is empty.";

            UIDocument uidoc = _uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                View original = FindView(doc, targetName);
                if (original == null)
                    return $"❌ View '{targetName}' not found.";

                // 1. Determine Duplicate Mode
                ViewDuplicateOption dupMode = ViewDuplicateOption.Duplicate;
                if (!string.IsNullOrWhiteSpace(mode) &&
                    mode.Trim().ToLower().Contains("detailing"))
                {
                    // If user requested 'with detailing', use the appropriate mode
                    dupMode = ViewDuplicateOption.WithDetailing;
                }

                // --- TRANSACTION 1: Duplicate and Get Initial Name ---
                ElementId newId;
                using (Transaction tx = new Transaction(doc, "Vella - Duplicate View"))
                {
                    tx.Start();
                    newId = original.Duplicate(dupMode);
                    tx.Commit();
                }

                View newView = doc.GetElement(newId) as View;

                if (newView == null)
                    return "❌ Could not duplicate view.";

                // The API assigns a unique, temporary name (e.g., "Original Name (Copy 1)").
                string initialName = newView.Name;
                string baseName = initialName.Replace(" (Copy 1)", "").Trim();
                string cleanName = $"{baseName} (Copy)";

                string finalName = initialName; // Start with the safe API-generated name

                // --- TRANSACTION 2: Rename to Clean Name (if possible) ---
                using (Transaction txRename = new Transaction(doc, "Vella - Clean Duplicate Name"))
                {
                    txRename.Start();

                    try
                    {
                        // 2. Try to set the clean name (e.g., "L1 Floor Plan (Copy)")
                        newView.Name = cleanName;
                        finalName = cleanName;
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        // 3. If conflict (e.g., "L1 Floor Plan (Copy)" exists), 
                        //    Revit will usually have set the name to "Original (Copy 2)", 
                        //    We just let the transaction commit the name Revit decided.
                        finalName = newView.Name;
                    }

                    txRename.Commit();
                }

                string modeText = dupMode == ViewDuplicateOption.WithDetailing ? " with detailing" : "";

                return $"✔ View '{targetName}' duplicated{modeText}. New view name: '{finalName}'.";
            }
            catch (Exception ex)
            {
                return $"❌ Exception in DuplicateViewCommand: {ex.Message}\n{ex.StackTrace}";
            }
        }


        // ============================================================================
        // FIND VIEW BY EXACT NAME
        // ============================================================================
        private View FindView(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
        }
    }
}