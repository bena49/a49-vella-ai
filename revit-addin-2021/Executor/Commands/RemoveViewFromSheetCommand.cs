// ============================================================================
// RemoveViewFromSheetCommand.cs — Vella AI
// ----------------------------------------------------------------------------
// Removes a view from a sheet by deleting the viewport that hosts the view.
//
// Envelope:
//
// {
//   "command": "remove_view_from_sheet",
//   "view": "<view name>",
//   "sheet": "<sheet number>"
// }
//
// Behavior:
//   • Find the view by name
//   • Find the sheet by number
//   • Find the specific viewport containing that view
//   • Delete the viewport only (safe)
// ============================================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class RemoveViewFromSheetCommand
    {
        private readonly UIApplication _uiapp;

        public RemoveViewFromSheetCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        // ============================================================================
        // EXECUTE
        // ============================================================================
        public string Execute(string viewName, string sheetNumber)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                return "❌ View name is empty.";

            if (string.IsNullOrWhiteSpace(sheetNumber))
                return "❌ Sheet number is empty.";

            UIDocument uidoc = _uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                View view = FindView(doc, viewName);
                if (view == null)
                    return $"❌ View '{viewName}' not found.";

                ViewSheet sheet = FindSheet(doc, sheetNumber);
                if (sheet == null)
                    return $"❌ Sheet '{sheetNumber}' not found.";

                // Find the viewport that hosts this view ON THIS SPECIFIC SHEET
                Viewport viewport = FindViewport(doc, sheet, view);
                if (viewport == null)
                    return $"⚠️ View '{viewName}' is not placed on sheet '{sheetNumber}'.";

                using (Transaction tx = new Transaction(doc, "Vella - Remove View From Sheet"))
                {
                    tx.Start();
                    doc.Delete(viewport.Id);
                    tx.Commit();
                }

                return $"✔ Removed view '{viewName}' from sheet '{sheetNumber}'.";
            }
            catch (Exception ex)
            {
                return $"❌ Exception in RemoveViewFromSheetCommand: {ex.Message}\n{ex.StackTrace}";
            }
        }


        // ============================================================================
        // FIND VIEW (EXACT NAME)
        // ============================================================================
        private View FindView(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
        }

        // ============================================================================
        // FIND SHEET (EXACT SHEET NUMBER)
        // ============================================================================
        private ViewSheet FindSheet(Document doc, string number)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => s.SheetNumber.Equals(number, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
        }

        // ============================================================================
        // FIND VIEWPORT THAT HOSTS VIEW ON SPECIFIC SHEET
        // ============================================================================
        private Viewport FindViewport(Document doc, ViewSheet sheet, View view)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .Where(vp =>
                    vp.SheetId == sheet.Id &&
                    vp.ViewId == view.Id
                )
                .FirstOrDefault();
        }
    }
}
