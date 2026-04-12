// ============================================================================
// InteractiveRoomPackageCommand.cs — Vella AI
// Executes a 2-part interactive workflow: Pick Room -> Create Callout ->
// Switch View -> Pick Marker -> Generate Elevations -> Create & Populate Sheet.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Newtonsoft.Json.Linq;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class InteractiveRoomPackageCommand
    {
        private readonly UIApplication _uiapp;
        private readonly JObject _rawPayload;

        public InteractiveRoomPackageCommand(UIApplication uiapp, JObject rawPayload)
        {
            _uiapp = uiapp;
            _rawPayload = rawPayload;
        }

        public string Execute()
        {
            try
            {
                if (_uiapp == null)
                    throw new Exception("CRASH LOG: _uiapp is completely null.");

                if (_uiapp.ActiveUIDocument == null)
                    throw new Exception("CRASH LOG: ActiveUIDocument is null! Revit lost focus to the web browser.");

                if (_rawPayload == null)
                    throw new Exception("CRASH LOG: _rawPayload is null! The JSON data didn't make it to this file.");

                UIDocument uidoc = _uiapp.ActiveUIDocument;
                Document doc = uidoc.Document;

                // --- 1. PARSE PAYLOAD ---
                string planTemplateName = _rawPayload["plan_template"]?.ToString();
                string elevTemplateName = _rawPayload["elev_template"]?.ToString();
                string titleblockName = _rawPayload["titleblock"]?.ToString();
                bool createSheets = _rawPayload["create_sheets"]?.ToObject<bool>() ?? false;
                bool placePlan = _rawPayload["place_plan"]?.ToObject<bool>() ?? false;

                JArray placeElevsArray = _rawPayload["place_elevations"] as JArray;
                List<string> elevsToPlace = placeElevsArray?
                    .Select(e => e.ToString())
                    .ToList() ?? new List<string>();

                double rawOffset = 150.0;
                if (_rawPayload["offset"] != null)
                {
                    rawOffset = Convert.ToDouble(_rawPayload["offset"].ToString());
                }
                double offsetFt = rawOffset / 304.8;

                // --- 2. VALIDATE INITIAL CONTEXT ---
                if (!(doc.ActiveView is ViewPlan activePlan) || activePlan.ViewType != ViewType.FloorPlan)
                {
                    return "{\"status\":\"error\", \"message\":\"❌ Please open a standard Floor Plan to begin.\"}";
                }

                // --- 3. INTERACTIVE PICK 1: THE ROOM ---
                Reference roomRef = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new RoomSelectionFilter(),
                    "Vella: Click a Room (or Room Tag) to create an Enlarged Plan.");

                Element clickedElem = doc.GetElement(roomRef);
                Room targetRoom = clickedElem as Room;

                if (clickedElem is RoomTag tag)
                {
                    targetRoom = tag.Room;
                }

                if (targetRoom == null)
                {
                    return "{\"status\":\"error\", \"message\":\"❌ Invalid selection. Command cancelled.\"}";
                }

                View calloutView = null;

                // --- 4. TRANSACTION 1: CREATE CALLOUT ---
                using (Transaction t1 = new Transaction(doc, "Vella: Create Enlarged Callout"))
                {
                    t1.Start();

                    BoundingBoxXYZ bb = targetRoom.get_BoundingBox(activePlan);
                    if (bb == null)
                    {
                        throw new Exception("Could not determine the selected room bounding box.");
                    }

                    XYZ minPt = new XYZ(bb.Min.X - offsetFt, bb.Min.Y - offsetFt, bb.Min.Z);
                    XYZ maxPt = new XYZ(bb.Max.X + offsetFt, bb.Max.Y + offsetFt, bb.Max.Z);

                    ViewFamilyType calloutType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);

                    if (calloutType == null)
                    {
                        throw new Exception("No Floor Plan ViewFamilyType found for callout creation.");
                    }

                    calloutView = ViewSection.CreateCallout(doc, activePlan.Id, calloutType.Id, minPt, maxPt);

                    // 💥 Use the Smart Namer for the Callout
                    SetUniqueViewName(doc, calloutView, $"EP_{targetRoom.Name}");

                    View planTemplate = FindTemplate(doc, planTemplateName);
                    if (planTemplate != null)
                    {
                        calloutView.ViewTemplateId = planTemplate.Id;
                    }

                    t1.Commit();
                }

                // --- 5. THE VIEW JUMP ---
                uidoc.RequestViewChange(calloutView);

                // --- 6. INTERACTIVE PICK 2: THE MARKER ---
                int idlePass = 0;
                EventHandler<Autodesk.Revit.UI.Events.IdlingEventArgs> pickHandler = null;

                pickHandler = (sender, args) =>
                {
                    idlePass++;

                    if (idlePass < 2)
                        return;

                    _uiapp.Idling -= pickHandler;

                    try
                    {
                        UIDocument liveUidoc = _uiapp.ActiveUIDocument;
                        if (liveUidoc == null) return;

                        Document liveDoc = liveUidoc.Document;

                        XYZ markerPt = liveUidoc.Selection.PickPoint(
                            "Vella: Click inside the room to place the Elevation Marker.");

                        ContinueAfterMarkerPick(
                            liveUidoc,
                            liveDoc,
                            targetRoom,
                            calloutView,
                            markerPt,
                            elevTemplateName,
                            titleblockName,
                            createSheets,
                            placePlan,
                            elevsToPlace);
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        // User pressed ESC during marker pick
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show(
                            "Vella Error",
                            "Failed during marker selection / continuation:\n\n" + ex.Message);
                    }
                };

                _uiapp.Idling += pickHandler;

                return "{\"status\":\"pending\", \"message\":\"Interactive room package is waiting for marker placement.\"}";

            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return "{\"status\":\"silent\"}";
            }
            catch (Exception ex)
            {
                TaskDialog.Show(
                    "Vella Fatal Crash",
                    "Error: " + ex.Message + "\n\nStack Trace:\n" + ex.StackTrace);

                string cleanEx = ex.Message
                    .Replace("\"", "'")
                    .Replace("\\", "/")
                    .Replace("\r", "")
                    .Replace("\n", " ");

                return "{\"status\":\"error\", \"message\":\"❌ C# Error: " + cleanEx + "\"}";
            }
        }

        // ============================================================================
        // CONTINUE WORKFLOW AFTER MARKER PICK
        // ============================================================================
        private void ContinueAfterMarkerPick(
            UIDocument uidoc,
            Document doc,
            Room targetRoom,
            View calloutView,
            XYZ markerPt,
            string elevTemplateName,
            string titleblockName,
            bool createSheets,
            bool placePlan,
            List<string> elevsToPlace)
        {
            ViewSheet newSheet = null;

            using (Transaction t2 = new Transaction(doc, "Vella: Create Elevations & Sheet"))
            {
                t2.Start();

                // A. CREATE ELEVATIONS
                ViewFamilyType elevType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.Elevation);

                if (elevType == null)
                {
                    throw new Exception("No Elevation ViewFamilyType found.");
                }

                ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, elevType.Id, markerPt, 50);

                string[] wallFaces = { "WEST", "SOUTH", "EAST", "NORTH" };
                View elevTemplate = FindTemplate(doc, elevTemplateName);

                List<View> generatedElevations = new List<View>();

                for (int i = 0; i < 4; i++)
                {
                    ViewSection elevView = marker.CreateElevation(doc, calloutView.Id, i);

                    // 💥 Use the Smart Namer for the Elevations
                    SetUniqueViewName(doc, elevView, $"{targetRoom.Name}_{wallFaces[i]}");

                    if (elevTemplate != null)
                    {
                        elevView.ViewTemplateId = elevTemplate.Id;
                    }

                    generatedElevations.Add(elevView);
                }

                // B. CREATE SHEET & PLACE VIEWS
                if (createSheets)
                {
                    // 1. EXACT TITLEBLOCK MATCHING
                    string tbFam = titleblockName;
                    if (!string.IsNullOrEmpty(titleblockName) && titleblockName.Contains(":"))
                    {
                        tbFam = titleblockName.Split(':')[0].Trim();
                    }

                    FamilySymbol titleblock = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .Cast<FamilySymbol>()
                        .FirstOrDefault(f => f.FamilyName.Equals(tbFam, StringComparison.OrdinalIgnoreCase));

                    if (titleblock == null)
                    {
                        titleblock = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilySymbol))
                            .OfCategory(BuiltInCategory.OST_TitleBlocks)
                            .Cast<FamilySymbol>()
                            .FirstOrDefault();
                    }

                    if (titleblock != null)
                    {
                        // 💥 1. Calculate the safe number BEFORE creating the sheet!
                        string safeSheetNum = GetSafeSheetNumber(doc, "A6.");

                        // 2. Create the sheet (Revit will auto-assign a temporary number here)
                        newSheet = ViewSheet.Create(doc, titleblock.Id);

                        // 3. Immediately overwrite it with our calculated, safe number
                        newSheet.SheetNumber = safeSheetNum;
                        newSheet.Name = $"ENLARGED PLAN - {targetRoom.Name}";

                        // 4. APPLY A49 STANDARDS
                        string stageStr = _rawPayload["stage"]?.ToString() ?? "CD";

                        string projectPhase = stageStr == "WV" ? "00 - WORKING VIEW" :
                                              stageStr == "PD" ? "01 - PRE-DESIGN" :
                                              stageStr == "DD" ? "02 - DESIGN DEVELOPMENT" :
                                              stageStr == "CD" ? "03 - CONSTRUCTION DOCUMENTS" : "";

                        SetSheetParam(newSheet, "SHEET SET", "A6_ENLARGED PLANS AND INTERIOR ELEVATIONS");
                        SetSheetParam(newSheet, "DISCIPLINE", "ARCHITECTURE");
                        if (!string.IsNullOrEmpty(projectPhase))
                        {
                            SetSheetParam(newSheet, "PROJECT PHASE", projectPhase);
                        }

                        // 4. PLACE THE ENLARGED PLAN (Top Left)
                        XYZ planLoc = new XYZ(0.6, 1.4, 0);
                        if (Viewport.CanAddViewToSheet(doc, newSheet.Id, calloutView.Id))
                        {
                            Viewport planVp = Viewport.Create(doc, newSheet.Id, calloutView.Id, planLoc);
                            doc.Regenerate();

                            try
                            {
                                Outline outline = planVp.GetBoxOutline();
                                double lineLen = Math.Max(0.1, (outline.MaximumPoint.X - outline.MinimumPoint.X) - 0.2);

                                planVp.LabelOffset = new XYZ(0.1, -0.05, 0);
                                planVp.LabelLineLength = lineLen;
                            }
                            catch { }
                        }

                        // 5. PLACE THE ELEVATIONS (Bottom Row Aligned)
                        double currentX = 0.45;
                        double shelfY = 0.45;

                        IEnumerable<View> elevationsToPlace = generatedElevations;

                        if (elevsToPlace != null && elevsToPlace.Count > 0)
                        {
                            elevationsToPlace = generatedElevations.Where(v =>
                                elevsToPlace.Any(key =>
                                    v.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0));
                        }

                        foreach (View targetElev in elevationsToPlace)
                        {
                            if (Viewport.CanAddViewToSheet(doc, newSheet.Id, targetElev.Id))
                            {
                                Viewport elevVp = Viewport.Create(doc, newSheet.Id, targetElev.Id, new XYZ(currentX, shelfY, 0));
                                doc.Regenerate();

                                Outline outline = elevVp.GetBoxOutline();
                                double vpWidth = outline.MaximumPoint.X - outline.MinimumPoint.X;

                                double shiftY = shelfY - outline.MinimumPoint.Y;
                                XYZ center = elevVp.GetBoxCenter();
                                elevVp.SetBoxCenter(new XYZ(center.X, center.Y + shiftY, 0));

                                try
                                {
                                    elevVp.LabelOffset = new XYZ(0.1, -0.05, 0);
                                    elevVp.LabelLineLength = Math.Max(0.1, vpWidth - 0.2);
                                }
                                catch { }

                                currentX += vpWidth + 0.15;
                            }
                        }
                    }
                }

                t2.Commit();
            }

            // 8. OPEN THE SHEET
            if (newSheet != null)
            {
                uidoc.RequestViewChange(newSheet);
            }
        }

        private void SetSheetParam(ViewSheet sheet, string paramName, string value)
        {
            Parameter p = sheet.LookupParameter(paramName);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
            {
                p.Set(value);
            }
        }

        private View FindTemplate(Document doc, string templateName)
        {
            if (string.IsNullOrEmpty(templateName)) return null;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => v.IsTemplate && v.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));
        }

        // ============================================================================
        // HELPER 1: SMART VIEW NAMING (" Copy X")
        // ============================================================================
        private void SetUniqueViewName(Document doc, View view, string desiredName)
        {
            string finalName = desiredName;
            int suffix = 1;

            while (true)
            {
                try
                {
                    view.Name = finalName;
                    break; // Success! Name was unique.
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // Name is taken, append " Copy X" exactly like native Revit
                    finalName = $"{desiredName} Copy {suffix}";
                    suffix++;
                }
            }
        }

        // ============================================================================
        // HELPER 2: ROBUST SHEET NUMBERING
        // ============================================================================
        private string GetSafeSheetNumber(Document doc, string prefix)
        {
            // 1. Get ALL sheet numbers currently in the Revit Project
            var allSheetNumbers = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Select(s => s.SheetNumber)
                .ToList();

            int maxVal = 0;

            // 2. Safely parse the numbers (ignores letters/suffixes like "A6.05a")
            foreach (string num in allSheetNumbers)
            {
                if (num.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string suffixStr = num.Substring(prefix.Length);
                    string numericPart = new string(suffixStr.TakeWhile(char.IsDigit).ToArray());

                    if (int.TryParse(numericPart, out int val))
                    {
                        if (val > maxVal) maxVal = val;
                    }
                }
            }

            // 3. Generate the next number and guarantee it is 100% unique
            string nextNum;
            do
            {
                maxVal++;
                nextNum = $"{prefix}{maxVal:D2}";
            }
            while (allSheetNumbers.Contains(nextNum));

            return nextNum;
        }
    }

    // ============================================================================
    // SELECTION FILTER: Forces user to ONLY click on Rooms or Room Tags
    // ============================================================================
    public class RoomSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Room || elem is RoomTag;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}