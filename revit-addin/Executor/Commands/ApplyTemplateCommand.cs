// ============================================================================
// ApplyTemplateCommand.cs — Vella AI
// ----------------------------------------------------------------------------
// Applies a view template to an existing Revit view.
//
// Envelope format:
//
// {
//   "command": "apply_template",
//   "target": "<view name>",
//   "template": "<template name>"
// }
//
// The Python backend ensures the template is a VALID A49 template string.
//
// This command safely:
//   • Finds the target view by exact name
//   • Finds the template view by exact name (must be IsTemplate == true)
//   • Applies template via view.ViewTemplateId
// ============================================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class ApplyTemplateCommand
    {
        private readonly UIApplication _uiapp;

        public ApplyTemplateCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        // ============================================================================
        // EXECUTE
        // ============================================================================
        public string Execute(string targetViewName, string templateName)
        {
            if (string.IsNullOrWhiteSpace(targetViewName))
                return "❌ View name is empty.";

            if (string.IsNullOrWhiteSpace(templateName))
                return "❌ Template name is empty.";

            UIDocument uidoc = _uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                View targetView = FindView(doc, targetViewName);
                if (targetView == null)
                    return $"❌ View '{targetViewName}' not found.";

                View templateView = FindTemplate(doc, templateName);
                if (templateView == null)
                    return $"❌ Template '{templateName}' not found. Make sure it is a View Template.";

                using (Transaction tx = new Transaction(doc, "Vella - Apply View Template"))
                {
                    tx.Start();

                    targetView.ViewTemplateId = templateView.Id;

                    tx.Commit();
                }

                return $"✔ Template '{templateName}' applied to view '{targetViewName}'.";
            }
            catch (Exception ex)
            {
                return $"❌ Exception in ApplyTemplateCommand: {ex.Message}\n{ex.StackTrace}";
            }
        }


        // ============================================================================
        // FIND TARGET VIEW (NON-TEMPLATE)
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
        // FIND TEMPLATE VIEW (IsTemplate = true)
        // ============================================================================
        private View FindTemplate(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
        }
    }
}
