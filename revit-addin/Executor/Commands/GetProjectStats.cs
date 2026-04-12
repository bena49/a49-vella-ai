using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace A49AIRevitAssistant.Executor.Commands
{
    public static class GetProjectStats
    {
        public static string Run(JObject cmd)
        {
            UIApplication uiApp = UIApplicationHolder.UIApp;
            Document doc = uiApp.ActiveUIDocument.Document;

            try
            {
                // Get various project statistics
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .Count();

                var walls = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Walls)
                    .WhereElementIsNotElementType()
                    .Count();

                var doors = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Doors)
                    .WhereElementIsNotElementType()
                    .Count();

                var windows = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Windows)
                    .WhereElementIsNotElementType()
                    .Count();

                string result = $"Project: {doc.Title}\n";
                result += $"Levels: {levels}\n";
                result += $"Walls: {walls}\n";
                result += $"Doors: {doors}\n";
                result += $"Windows: {windows}\n";
                result += $"Units: {doc.DisplayUnitSystem}";

                return result;
            }
            catch (Exception ex)
            {
                return $"❌ Error getting project stats: {ex.Message}";
            }
        }
    }
}