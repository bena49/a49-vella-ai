// ============================================================================
// A49AIRevitAssistant/Executor/Commands/GetProjectInfoCommand.cs
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture; // 💥 NEW: Required for Room elements
using Newtonsoft.Json;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class GetProjectInfoCommand
    {
        private Document _doc;

        public GetProjectInfoCommand(Document doc)
        {
            _doc = doc;
        }

        public string Execute()
        {
            try
            {
                // 1. Get Levels (Sorted by Elevation)
                var levels = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .Select(l => l.Name)
                    .ToList();

                // 2. Get View Templates (Sorted by Name)
                var templates = new FilteredElementCollector(_doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => v.IsTemplate)
                    .Select(v => v.Name)
                    .OrderBy(n => n)
                    .ToList();

                // 3. Get Scope Boxes (Sorted by Name)
                var scopeBoxes = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                    .WhereElementIsNotElementType()
                    .Select(e => e.Name)
                    .OrderBy(n => n)
                    .ToList();

                // 4. Get Titleblocks (Sorted by Family: Type)
                var titleblocks = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsElementType()
                    .Cast<FamilySymbol>()
                    .Select(tb => tb.FamilyName + ": " + tb.Name)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();

                // 5. Get Existing Sheets (Sorted by Number)
                var sheets = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Select(s => $"{s.SheetNumber} - {s.Name}") // Format: "A1.01 - Plan"
                    .OrderBy(s => s)
                    .ToList();

                // 💥 6. NEW: Get Placed Rooms (Sorted by Level, then Number)
                var rooms = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => r.Area > 0 && r.Location != null) // SAFETY: Must be placed in the model
                    .Select(r => new
                    {
                        unique_id = r.UniqueId,
                        number = r.Number,
                        name = r.Name,
                        level = r.Level != null ? r.Level.Name : "Unknown"
                    })
                    .OrderBy(r => r.level).ThenBy(r => r.number)
                    .ToList();

                // 7. Construct the Payload object
                var payload = new
                {
                    project_info = new
                    {
                        levels = levels,
                        templates = templates,
                        scope_boxes = scopeBoxes,
                        titleblocks = titleblocks,
                        sheets = sheets,
                        rooms = rooms // 💥 Added here so Vue can read it!
                    }
                };

                // 8. Serialize to JSON
                return JsonConvert.SerializeObject(payload);
            }
            catch (Exception ex)
            {
                var errorPayload = new { error = $"Failed to fetch project info: {ex.Message}" };
                return JsonConvert.SerializeObject(errorPayload);
            }
        }
    }
}