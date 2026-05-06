// ============================================================================
// InteractiveRoomPackageCommand.cs — Vella AI (Revit 2021)
// Executes a 2-part interactive workflow: Pick Room -> Create Callout ->
// Switch View -> Pick Marker -> Generate Elevations -> Create & Populate Sheet.
//
// Revit 2021 adaptations vs canonical (revit-addin/):
//   - Linked-room support DOES work in 2021: Reference.LinkedElementId,
//     RevitLinkInstance.GetLinkDocument(), and link.GetTotalTransform() are
//     all 2014-era APIs. We don't need Reference.CreateLinkReference (2022+)
//     because this workflow uses host-doc creation APIs (CreateCallout,
//     CreateElevationMarker) — they just need transformed XYZ coords, not
//     link references.
//   - LabelOffset / LabelLineLength on Viewport are NOT in 2021 API → omitted.
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

        // Carried from the first transaction (where we resolve the room's
        // bbox in host coords) into the deferred ContinueAfterMarkerPick
        // step. Lets us tightly crop each generated elevation to the room
        // — eliminates the "default crop is the entire model" problem,
        // especially for linked rooms where Revit's auto-crop heuristic
        // can't introspect the linked geometry the same way.
        private BoundingBoxXYZ _roomBboxFullHost;

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
                // Filter accepts both host-doc rooms and linked-doc rooms via
                // the RevitLinkInstance branch + AllowReference resolution.
                Reference roomRef = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new RoomSelectionFilter(doc),
                    "Vella: Click a Room (or Room Tag) to create an Enlarged Plan.");

                // Resolve picked element into a Room. Handles four cases:
                //   1. Host Room   2. Host RoomTag → tag.Room
                //   3. Linked Room 4. Linked RoomTag → tag.Room (in link doc)
                //
                // roomToHost transform converts coords from the room's own doc
                // into host coords. Identity for host rooms; the link's
                // GetTotalTransform for linked rooms.
                var (targetRoom, roomToHost) = ResolveRoomFromReference(doc, roomRef);
                if (targetRoom == null)
                {
                    return "{\"status\":\"error\", \"message\":\"❌ Invalid selection. Command cancelled.\"}";
                }

                View calloutView = null;

                // --- 4. TRANSACTION 1: CREATE CALLOUT ---
                using (Transaction t1 = new Transaction(doc, "Vella: Create Enlarged Callout"))
                {
                    t1.Start();

                    // For a host room we can pass activePlan to get the bbox
                    // clipped to the view's depth. For a LINKED room,
                    // get_BoundingBox(view) returns null because the view
                    // belongs to a different doc, so we fall back to the
                    // intrinsic bbox and transform corners into host coords.
                    BoundingBoxXYZ bb = targetRoom.get_BoundingBox(activePlan)
                                       ?? targetRoom.get_BoundingBox(null);
                    if (bb == null)
                    {
                        throw new Exception("Could not determine the selected room bounding box.");
                    }

                    // Transform the room bbox corners into host coords. For
                    // host rooms roomToHost is identity → values unchanged.
                    XYZ bbMinHost = roomToHost.OfPoint(bb.Min);
                    XYZ bbMaxHost = roomToHost.OfPoint(bb.Max);
                    // Re-normalize after transform — link rotation about Z can
                    // swap X/Y min↔max so we have to take per-axis min/max.
                    double minX = Math.Min(bbMinHost.X, bbMaxHost.X);
                    double minY = Math.Min(bbMinHost.Y, bbMaxHost.Y);
                    double minZ = Math.Min(bbMinHost.Z, bbMaxHost.Z);
                    double maxX = Math.Max(bbMinHost.X, bbMaxHost.X);
                    double maxY = Math.Max(bbMinHost.Y, bbMaxHost.Y);
                    double maxZ = Math.Max(bbMinHost.Z, bbMaxHost.Z);

                    XYZ minPt = new XYZ(minX - offsetFt, minY - offsetFt, minZ);
                    XYZ maxPt = new XYZ(maxX + offsetFt, maxY + offsetFt, maxZ);

                    // Stash the room's FULL 3D bbox in host coords for the
                    // elevation-crop step (later transaction). The plan-clipped
                    // bbox above was good enough for the callout (a 2D crop)
                    // but elevations need the room's true vertical extent —
                    // get_BoundingBox(activePlan) may have clipped it to the
                    // plan view's depth.
                    BoundingBoxXYZ bbFull = targetRoom.get_BoundingBox(null) ?? bb;
                    XYZ fmin = roomToHost.OfPoint(bbFull.Min);
                    XYZ fmax = roomToHost.OfPoint(bbFull.Max);
                    _roomBboxFullHost = new BoundingBoxXYZ
                    {
                        Min = new XYZ(Math.Min(fmin.X, fmax.X),
                                      Math.Min(fmin.Y, fmax.Y),
                                      Math.Min(fmin.Z, fmax.Z)),
                        Max = new XYZ(Math.Max(fmin.X, fmax.X),
                                      Math.Max(fmin.Y, fmax.Y),
                                      Math.Max(fmin.Z, fmax.Z)),
                    };

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

                    // Crop the elevation to the room's actual bounds. Revit's
                    // default crop after CreateElevation extends to whatever
                    // geometry the marker can "see" in 3D, which for linked
                    // rooms is usually the entire model — hence the staff
                    // report of "elevation height crop and view limits are
                    // off." Setting it explicitly makes the result tight and
                    // identical for host vs linked rooms.
                    SetElevationCropToRoom(elevView, _roomBboxFullHost);

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

                                // LabelOffset / LabelLineLength not available in Revit 2021 API
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
                                    // LabelOffset / LabelLineLength not available in Revit 2021 API
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
        // SET ELEVATION CROP TO ROOM BOUNDS
        // ============================================================================
        // After ElevationMarker.CreateElevation, Revit's default crop is a
        // model-wide guess. For a linked room the heuristic doesn't introspect
        // the linked geometry the same way, so the crop blows out to the
        // entire visible model. We explicitly pin the crop to the room's
        // actual bounds (in host coords), with small XY/Z paddings so wall
        // thickness stays visible.
        //
        // Method: take the 8 corners of the room's host-coord bbox, project
        // them into the elevation view's local frame, and use the per-axis
        // min/max as the new crop extents. This is invariant under arbitrary
        // elevation orientation (north/south/east/west and any in-between).
        // No-op (try/catch swallow) if Revit rejects the new bbox — better to
        // leave the default crop than to crash mid-creation.
        // ============================================================================
        private void SetElevationCropToRoom(ViewSection elevView, BoundingBoxXYZ roomBboxHost)
        {
            if (elevView == null || roomBboxHost == null) return;
            try
            {
                BoundingBoxXYZ crop = elevView.CropBox;
                if (crop == null || crop.Transform == null) return;

                // 8 corners of the room's bbox in host (world) coords.
                XYZ rmin = roomBboxHost.Min;
                XYZ rmax = roomBboxHost.Max;
                XYZ[] corners = new[]
                {
                    new XYZ(rmin.X, rmin.Y, rmin.Z),
                    new XYZ(rmin.X, rmin.Y, rmax.Z),
                    new XYZ(rmin.X, rmax.Y, rmin.Z),
                    new XYZ(rmin.X, rmax.Y, rmax.Z),
                    new XYZ(rmax.X, rmin.Y, rmin.Z),
                    new XYZ(rmax.X, rmin.Y, rmax.Z),
                    new XYZ(rmax.X, rmax.Y, rmin.Z),
                    new XYZ(rmax.X, rmax.Y, rmax.Z),
                };

                // Project into elevation-view-local frame:
                //   X = horizontal across screen
                //   Y = vertical (up = positive)
                //   Z = depth (negative = into model, away from viewer)
                Transform inv = crop.Transform.Inverse;
                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
                foreach (XYZ c in corners)
                {
                    XYZ p = inv.OfPoint(c);
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                    if (p.Z < minZ) minZ = p.Z;
                    if (p.Z > maxZ) maxZ = p.Z;
                }

                // Padding — 200mm horizontal/vertical so wall thickness shows;
                // 300mm depth so far clip extends just past the back wall.
                double padXY = 200.0 / 304.8;
                double padZ = 300.0 / 304.8;

                crop.Min = new XYZ(minX - padXY, minY - padXY, minZ - padZ);
                crop.Max = new XYZ(maxX + padXY, maxY + padXY, maxZ + padZ);
                elevView.CropBox = crop;
                elevView.CropBoxActive = true;
            }
            catch
            {
                // Leave Revit's default crop in place if the API rejects ours.
            }
        }

        // ============================================================================
        // RESOLVE PICKED REFERENCE → (Room, transform-from-room-doc-to-host)
        // ============================================================================
        // Handles all four combinations the RoomSelectionFilter admits:
        //   • Host Room        → (room, Identity)
        //   • Host RoomTag     → (tag.Room, Identity)
        //   • Linked Room      → (linkedRoom, link.GetTotalTransform())
        //   • Linked RoomTag   → (tagInLinkDoc.Room, link.GetTotalTransform())
        //
        // For host paths returns Transform.Identity so downstream OfPoint
        // calls leave coords untouched. For linked paths returns the link's
        // total transform so callers can map the room's bbox / location into
        // host coords before passing to host-doc APIs (CreateCallout, etc.).
        // Returns (null, null) when the reference can't be resolved into a Room.
        //
        // Revit 2021: Reference.LinkedElementId, RevitLinkInstance.GetLinkDocument,
        // and link.GetTotalTransform are all 2014-era APIs — fully usable here.
        // ============================================================================
        private (Room room, Transform roomToHost) ResolveRoomFromReference(Document hostDoc, Reference roomRef)
        {
            if (hostDoc == null || roomRef == null) return (null, null);

            // Host pick: LinkedElementId is the invalid sentinel.
            if (roomRef.LinkedElementId == ElementId.InvalidElementId)
            {
                Element clicked = hostDoc.GetElement(roomRef);
                Room hostRoom = clicked as Room;
                if (clicked is RoomTag tag) hostRoom = tag.Room;
                return (hostRoom, Transform.Identity);
            }

            // Linked pick: walk through the link instance to its document.
            try
            {
                var link = hostDoc.GetElement(roomRef.ElementId) as RevitLinkInstance;
                if (link == null) return (null, null);
                Document linkDoc = link.GetLinkDocument();
                if (linkDoc == null) return (null, null);

                Element linkedElem = linkDoc.GetElement(roomRef.LinkedElementId);
                Room linkedRoom = linkedElem as Room;
                // RoomTag in linked doc — its Room property returns the linked Room.
                if (linkedElem is RoomTag linkedTag) linkedRoom = linkedTag.Room;
                if (linkedRoom == null) return (null, null);

                Transform xform = link.GetTotalTransform() ?? Transform.Identity;
                return (linkedRoom, xform);
            }
            catch
            {
                return (null, null);
            }
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
    // SELECTION FILTER: Forces user to ONLY click on Rooms or Room Tags.
    // Accepts both HOST elements and LINKED elements — for a linked Revit model
    // the picker's hit-test surfaces a RevitLinkInstance, so we say "yes" to
    // the link instance in AllowElement and then narrow to Room/RoomTag in
    // AllowReference by resolving the linked element through the link's doc.
    // ============================================================================
    public class RoomSelectionFilter : ISelectionFilter
    {
        private readonly Document _hostDoc;

        public RoomSelectionFilter(Document hostDoc)
        {
            _hostDoc = hostDoc;
        }

        public bool AllowElement(Element elem)
        {
            // Host-doc Rooms / RoomTags pass directly. RevitLinkInstance also
            // passes so the picker doesn't reject the click outright — the
            // linked-element check happens in AllowReference below.
            return elem is Room
                || elem is RoomTag
                || elem is RevitLinkInstance;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            if (reference == null || _hostDoc == null) return false;

            // Host references: nothing to narrow — AllowElement already gated
            // to Room/RoomTag for non-link picks.
            if (reference.LinkedElementId == ElementId.InvalidElementId)
                return true;

            // Link references: walk to the linked element and confirm it's a
            // Room or RoomTag. Reject everything else (walls, doors, etc. in
            // the linked file would otherwise pass AllowElement via the
            // RevitLinkInstance branch).
            try
            {
                var link = _hostDoc.GetElement(reference.ElementId) as RevitLinkInstance;
                if (link == null) return false;
                Document linkDoc = link.GetLinkDocument();
                if (linkDoc == null) return false;     // unloaded
                Element linkedElem = linkDoc.GetElement(reference.LinkedElementId);
                return linkedElem is Room || linkedElem is RoomTag;
            }
            catch
            {
                return false;
            }
        }
    }
}
