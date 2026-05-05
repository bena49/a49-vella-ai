// ============================================================================
// InteractiveRoomPackageCommand.cs — Vella AI
// Executes a 2-part interactive workflow: Pick Room -> Create Callout ->
// Switch View -> Pick Marker -> Generate Elevations -> Create & Populate Sheet. 
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

            // 💥 FIX 1: Move this list declaration out of the Transaction block 
            // so it can be accessed at the end of the method for the summary.
            List<View> generatedElevations = new List<View>();

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
                        // A6 series — emits the format matching the project's
                        // active numbering scheme. Frontend passes the scheme
                        // hint (`payload.scheme`) so we honor session-override
                        // even when no A6 sheets exist yet to auto-detect from.
                        // Falls through to local auto-detect if hint is missing.
                        string schemeHint = _rawPayload["scheme"]?.ToString();
                        string safeSheetNum = GetSafeSheetNumber(doc, 6000, "A6", schemeHint);

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

                // === START OF CORRECTED HANDSHAKE ===
                // 💥 THE SUMMARY PAYLOAD
                // Now that generatedElevations is in scope, we can count the views properly.
                Newtonsoft.Json.Linq.JObject summary = new Newtonsoft.Json.Linq.JObject
                {
                    ["type"] = "ROOM_PACKAGE_COMPLETE",
                    ["room_name"] = targetRoom.Name,
                    ["sheet_number"] = newSheet.SheetNumber,
                    ["sheet_name"] = newSheet.Name,
                    ["view_count"] = generatedElevations.Count + 1 // +1 for the Enlarged Plan
                };

                // Capture session_key for the frontend bridge
                string sKey = _rawPayload["session_key"]?.ToString();

                if (A49AIRevitAssistant.UI.DockablePaneViewer.Instance != null)
                {
                    A49AIRevitAssistant.UI.DockablePaneViewer.Instance.PostResultToVue(
                        summary.ToString(),
                        sKey
                    );
                }
                // === END OF CORRECTED HANDSHAKE ===
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
        // HELPER 2: ROBUST SHEET NUMBERING — scheme-aware
        //
        // Mirrors backend naming_engine.get_next_sheet_number for the three
        // schemes Vella supports. Picks which one by, in priority order:
        //
        //   1. `schemeHint` argument (frontend RoomElevationWizard passes the
        //      detected/active scheme via payload — handles the case where a
        //      session override is set but no A6 sheets exist yet to anchor
        //      auto-detect on).
        //   2. Auto-detect from existing sheets (same heuristic as the frontend
        //      RenameWizard.detectedScheme):
        //         · any "A1.NN" / "X0.NN"  → a49_dotted
        //         · any 5+ digit numeric    → iso19650_5digit
        //         · else                    → iso19650_4digit
        //
        // a49_dotted gap-fills (lowest free slot wins). The iso19650 schemes
        // advance from max+increment with collision bump-up, no gap-fill.
        //
        // seriesBase4: 4-digit thousand-base for this series (6000 for A6).
        //              Multiplied ×10 for 5-digit (→ 60000-band).
        // seriesPrefix: the dotted category code, e.g. "A6". Used only when
        //               the project is detected/declared as a49_dotted.
        // schemeHint:  optional explicit scheme name ("a49_dotted",
        //              "iso19650_5digit", "iso19650_4digit"). null/empty
        //              triggers auto-detect.
        // ============================================================================
        private string GetSafeSheetNumber(Document doc, int seriesBase4,
                                          string seriesPrefix, string schemeHint = null)
        {
            var allSheetNumbers = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Select(s => s.SheetNumber ?? "")
                .ToList();

            // Resolve scheme from hint first; fall back to auto-detect.
            bool hasDotted, has5Digit;
            if (!string.IsNullOrWhiteSpace(schemeHint))
            {
                string h = schemeHint.Trim().ToLowerInvariant();
                hasDotted  = h == "a49_dotted";
                has5Digit  = h == "iso19650_5digit";
                // Anything else (including "iso19650_4digit") falls through
                // to the 4-digit branch below.
            }
            else
            {
                hasDotted = allSheetNumbers.Any(n =>
                    Regex.IsMatch(n, @"^[AX]\d\.\d{1,3}(\.\d+)?$"));
                has5Digit = !hasDotted && allSheetNumbers.Any(n =>
                    Regex.IsMatch(n, @"^\d{5,}$"));
            }

            if (hasDotted)
                return NextDottedSlot(allSheetNumbers, seriesPrefix);
            if (has5Digit)
                return NextNumericSlot(allSheetNumbers, seriesBase4 * 10, increment: 100, width: 5);
            return NextNumericSlot(allSheetNumbers, seriesBase4, increment: 10, width: 4);
        }

        // Lowest free positive sub-slot under a category (gap-fill semantics).
        // Sub-parts (A6.05.1) are ignored — they share their parent's primary
        // slot for allocation purposes, just like the Python side does.
        private string NextDottedSlot(List<string> allSheetNumbers, string seriesPrefix)
        {
            var dottedRe = new Regex($@"^{Regex.Escape(seriesPrefix)}\.(\d{{1,3}})(?:\.\d+)?$",
                                     RegexOptions.IgnoreCase);
            var used = new HashSet<int>();
            foreach (string n in allSheetNumbers)
            {
                var m = dottedRe.Match(n);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int slot))
                    used.Add(slot);
            }
            // a49_dotted A6 starts at .01 (no .00 slot per spec). Walk upward
            // from 1 until we find a free slot — covers gap-fill after a delete.
            int next = 1;
            while (used.Contains(next) && next < 100) next++;
            return $"{seriesPrefix}.{next:D2}";
        }

        // Existing behavior, parameterised over digit-width / increment so the
        // same routine handles iso19650_4digit (6010 increments of 10) and
        // iso19650_5digit (60100 increments of 100).
        private string NextNumericSlot(List<string> allSheetNumbers,
                                       int seriesBase, int increment, int width)
        {
            int bandMax = seriesBase + (int)Math.Pow(10, width - 1);
            int maxVal = 0;
            foreach (string num in allSheetNumbers)
            {
                if (int.TryParse(num, out int val) && val >= seriesBase && val < bandMax)
                {
                    if (val > maxVal) maxVal = val;
                }
            }
            int nextVal = (maxVal == 0)
                ? (seriesBase + increment)
                : (((maxVal / increment) + 1) * increment);

            string fmt = "D" + width;
            string nextNum = nextVal.ToString(fmt);
            while (allSheetNumbers.Contains(nextNum))
            {
                nextVal += increment;
                nextNum = nextVal.ToString(fmt);
                if (nextVal >= bandMax) break;  // Safety: don't overflow band
            }
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