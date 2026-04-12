// ============================================================================
// GetLevelsCommand.cs  —  Vella AI
// ----------------------------------------------------------------------------
// Replaces GetLevelsInfo.cs
//
// Purpose:
//   • Returns a clean JSON-like string describing the actual Revit levels.
//   • Used by Python's level_engine to debug mappings.
//   • Called by envelope: { "command": "get_levels" }
//
// Output Format:
//   L1 → “Level 1”, “L2”, “02”, "B1", “Roof”, etc.
//   Returns all levels exactly as Revit sees them.
// ============================================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;
using System.Text;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class GetLevelsCommand
    {
        private readonly UIApplication _uiapp;

        public GetLevelsCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        // ============================================================================
        // EXECUTE
        // ============================================================================
        public string Execute()
        {
            try
            {
                UIDocument uidoc = _uiapp.ActiveUIDocument;
                Document doc = uidoc.Document;

                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();

                if (levels.Count == 0)
                    return "[]";

                // Build JSON-like text for Python
                StringBuilder sb = new StringBuilder();
                sb.Append("[\n");

                foreach (var lvl in levels)
                {
                    double elevFeet = lvl.Elevation;
                    double elevMM = UnitUtils.ConvertFromInternalUnits(elevFeet, UnitTypeId.Millimeters);

                    sb.Append($"  {{ \"name\": \"{lvl.Name}\", \"elevation_mm\": {Math.Round(elevMM, 2)} }},\n");
                }

                sb.Append("]");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"❌ Exception in GetLevelsCommand: {ex.Message}\n{ex.StackTrace}";
            }
        }
    }
}
