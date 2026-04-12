// ============================================================================
// RenameViewCommand.cs — Vella AI
// ============================================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class RenameViewCommand
    {
        private readonly UIApplication _uiapp;

        public RenameViewCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        // ============================================================================
        // EXECUTE
        // ============================================================================
        public string Execute(string targetName, string newName)
        {
            // ... (initial checks and setup remain the same)
            UIDocument uidoc = _uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                var view = FindView(doc, targetName);
                if (view == null)
                    return $"❌ View '{targetName}' not found.";

                // Start the transaction
                using (Transaction tx = new Transaction(doc, "Vella - Rename View"))
                {
                    tx.Start();

                    try
                    {
                        // 1. Try to set the desired name
                        view.Name = newName;
                        tx.Commit();
                        return $"✔ View '{targetName}' renamed to '{newName}'.";
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        // 2. Conflict detected! Try the fallback name.
                        tx.RollBack(); // Must roll back the failed transaction before starting a new one!

                        using (Transaction txConflict = new Transaction(doc, "Vella - Rename View Conflict"))
                        {
                            txConflict.Start();
                            string fallback = $"{newName}_{Guid.NewGuid().ToString().Substring(0, 6)}";

                            // Re-find the view if the previous transaction rollback invalidated it
                            view = FindView(doc, targetName);
                            if (view == null)
                                return $"❌ Naming conflict prevented rename, and the view disappeared.";

                            view.Name = fallback;
                            txConflict.Commit();
                            return $"⚠️ Naming conflict. View '{targetName}' renamed as '{fallback}'.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return $"❌ Exception in RenameViewCommand: {ex.Message}\n{ex.StackTrace}";
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
