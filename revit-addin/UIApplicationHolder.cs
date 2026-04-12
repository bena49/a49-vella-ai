using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace A49AIRevitAssistant.Executor
{
    public static class UIApplicationHolder
    {
        public static UIApplication UIApp { get; set; }

        public static Document GetDocument()
        {
            return UIApp?.ActiveUIDocument?.Document;
        }

        public static string GetStatus()
        {
            if (UIApp == null)
                return "UIApplication not set - Click the AI Assistant ribbon button";
            if (UIApp.ActiveUIDocument == null)
                return "No active UI document - Please open a project";
            if (UIApp.ActiveUIDocument.Document == null)
                return "No active document";
            if (UIApp.ActiveUIDocument.Document.IsFamilyDocument)
                return "Family document (not supported) - Please open a project file";

            return $"Ready - Project: {UIApp.ActiveUIDocument.Document.Title}";
        }

        public static string GetDetailedStatus()
        {
            if (UIApp == null)
                return "❌ UIApplication: Not set\n💡 Solution: Click the AI Assistant ribbon button";

            string status = $"✅ UIApplication: Set\n";
            status += $"🏷️ Revit Version: {UIApp.Application.VersionName}\n";

            if (UIApp.ActiveUIDocument == null)
            {
                status += "❌ ActiveUIDocument: None\n💡 Solution: Open a project file";
            }
            else
            {
                status += $"✅ ActiveUIDocument: Available\n";

                if (UIApp.ActiveUIDocument.Document == null)
                {
                    status += "❌ Document: None";
                }
                else
                {
                    var doc = UIApp.ActiveUIDocument.Document;
                    status += $"✅ Document: {doc.Title}\n";
                    status += $"📊 Is Family: {doc.IsFamilyDocument}\n";
                    status += $"🔢 Project Number: {doc.ProjectInformation?.Number ?? "N/A"}";
                }
            }

            return status;
        }

        public static bool IsReady()
        {
            return UIApp != null &&
                   UIApp.ActiveUIDocument != null &&
                   UIApp.ActiveUIDocument.Document != null &&
                   !UIApp.ActiveUIDocument.Document.IsFamilyDocument;
        }
    }
}