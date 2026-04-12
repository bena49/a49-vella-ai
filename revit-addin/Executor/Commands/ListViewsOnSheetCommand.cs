// Executor/Commands/ListViewsOnSheetCommand.cs

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System; // Ensure StringComparison is available
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class ListViewsOnSheetCommand
    {
        private readonly UIApplication _uiapp;

        public ListViewsOnSheetCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        public string Execute(string sheetNumber)
        {
            UIDocument uidoc = _uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            ViewSheet sheet = null;

            // 1. CASE A: User provided a Sheet Number
            if (!string.IsNullOrWhiteSpace(sheetNumber))
            {
                sheet = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .FirstOrDefault(s => s.SheetNumber.Equals(sheetNumber, StringComparison.OrdinalIgnoreCase));

                if (sheet == null)
                {
                    // Send empty result to clear UI
                    _ = DjangoBridge.SendAsync(new { list_views_on_sheet_result = new object[0] });
                    return $"❌ Sheet '{sheetNumber}' not found.";
                }
            }
            // 2. CASE B: No input -> Use Active Sheet
            else
            {
                if (doc.ActiveView is ViewSheet activeSheet)
                {
                    sheet = activeSheet;
                }
                else
                {
                    return "❌ Please open a Sheet or specify a sheet number (e.g. 'List views on A1.01').";
                }
            }

            // 3. Collect Views
            var viewIds = sheet.GetAllPlacedViews();
            var views = viewIds
                .Select(id => doc.GetElement(id) as View)
                .Where(v => v != null)
                .OrderBy(v => v.Name)
                .Select(v => new { name = v.Name })
                .ToList();

            // 4. Send Data to Python/Vue
            _ = DjangoBridge.SendAsync(new
            {
                list_views_on_sheet_result = views
            });

            if (views.Count == 0) return $"ℹ Sheet {sheet.SheetNumber} is empty.";

            // 5. Build Chat Response
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"✔ Found {views.Count} views on {sheet.SheetNumber}:");
            foreach (var v in views)
                sb.AppendLine($"• {v.name}");

            return sb.ToString();
        }
    }
}