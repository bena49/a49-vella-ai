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

                // 💥 7. NEW: Get Door Tag Families (for Auto-Tag wizard)
                var doorTags = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_DoorTags)
                    .Cast<FamilySymbol>()
                    .Select(fs => new
                    {
                        family = fs.Family != null ? fs.Family.Name : fs.FamilyName,
                        type = fs.Name
                    })
                    .OrderBy(t => t.family).ThenBy(t => t.type)
                    .ToList();

                // 💥 7b. NEW: Window Tag Families (for Automate Tagging wizard)
                var windowTags = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_WindowTags)
                    .Cast<FamilySymbol>()
                    .Select(fs => new
                    {
                        family = fs.Family != null ? fs.Family.Name : fs.FamilyName,
                        type = fs.Name
                    })
                    .OrderBy(t => t.family).ThenBy(t => t.type)
                    .ToList();

                // 💥 7c. NEW: Wall Tag Families
                var wallTags = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_WallTags)
                    .Cast<FamilySymbol>()
                    .Select(fs => new
                    {
                        family = fs.Family != null ? fs.Family.Name : fs.FamilyName,
                        type = fs.Name
                    })
                    .OrderBy(t => t.family).ThenBy(t => t.type)
                    .ToList();

                // 💥 7d. NEW: Room Tag Families
                var roomTags = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_RoomTags)
                    .Cast<FamilySymbol>()
                    .Select(fs => new
                    {
                        family = fs.Family != null ? fs.Family.Name : fs.FamilyName,
                        type = fs.Name
                    })
                    .OrderBy(t => t.family).ThenBy(t => t.type)
                    .ToList();

                // 💥 7e. NEW: Ceiling Tag Families
                var ceilingTags = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_CeilingTags)
                    .Cast<FamilySymbol>()
                    .Select(fs => new
                    {
                        family = fs.Family != null ? fs.Family.Name : fs.FamilyName,
                        type = fs.Name
                    })
                    .OrderBy(t => t.family).ThenBy(t => t.type)
                    .ToList();

                // 💥 8. NEW: Get Taggable Views (for Automate Tagging wizard)
                // Includes Plans, Elevations, and Sections with full metadata for filtering.
                // Stage/Level/ViewAbbrev are parsed from the A49 view name convention:
                //   {STAGE}_{SHEET_TYPE}_{VIEW_ABBREV}_{LEVEL}  e.g. "CD_A1_FL_01"
                var allowedViewTypes = new HashSet<ViewType>
                {
                    ViewType.FloorPlan,
                    ViewType.CeilingPlan,
                    ViewType.Elevation,
                    ViewType.Section,
                    ViewType.AreaPlan,
                    ViewType.EngineeringPlan
                };

                var taggableViews = new FilteredElementCollector(_doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && allowedViewTypes.Contains(v.ViewType) && v.CanBePrinted)
                    .Select(v =>
                    {
                        // Parse {STAGE}_{SHEET_TYPE}_{VIEW_ABBREV}_{LEVEL} from view name
                        var parts = v.Name.Split('_');
                        string stage = "";
                        string levelCode = "";
                        string viewAbbrev = "";

                        if (parts.Length > 0)
                        {
                            var first = parts[0].ToUpper();
                            if (first == "WV" || first == "PD" || first == "DD" || first == "CD")
                                stage = first;
                        }

                        // Level code is typically the last segment that matches NN / BN / PN / LN / RF / 00
                        if (parts.Length >= 2)
                        {
                            var last = parts[parts.Length - 1].ToUpper();
                            // Strip any trailing text after a space or dash (e.g. "01 - Floor Plan")
                            var levelToken = last.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
                            if (levelToken.Length > 0)
                            {
                                var candidate = levelToken[0];
                                if (System.Text.RegularExpressions.Regex.IsMatch(candidate, @"^(RF|00|[BPL]?\d{1,2})$"))
                                    levelCode = candidate;
                            }
                        }

                        // View abbrev is the segment before the level (if stage present)
                        if (parts.Length >= 3)
                        {
                            viewAbbrev = parts[parts.Length - 2].ToUpper();
                        }

                        // Get scale (1:N format)
                        int scale = v.Scale;

                        return new
                        {
                            id = v.Id.Value,
                            name = v.Name,
                            view_type = v.ViewType.ToString(),  // "FloorPlan", "Elevation", "Section", "CeilingPlan"
                            stage = stage,
                            level = levelCode,
                            view_abbrev = viewAbbrev,
                            scale = scale
                        };
                    })
                    .OrderBy(v => v.stage).ThenBy(v => v.view_type).ThenBy(v => v.name)
                    .ToList();

                // 9. Construct the Payload object
                var payload = new
                {
                    project_info = new
                    {
                        levels = levels,
                        templates = templates,
                        scope_boxes = scopeBoxes,
                        titleblocks = titleblocks,
                        sheets = sheets,
                        rooms = rooms,
                        door_tags = doorTags,
                        window_tags = windowTags,
                        wall_tags = wallTags,
                        room_tags = roomTags,
                        ceiling_tags = ceilingTags,
                        // Full taggable views with metadata (used by AutomateTagWizard)
                        taggable_views = taggableViews
                    }
                };

                // 10. Serialize to JSON
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