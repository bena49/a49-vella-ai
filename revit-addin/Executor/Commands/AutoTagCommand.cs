// ============================================================================
// A49AIRevitAssistant/Executor/Commands/AutoTagCommand.cs
// ============================================================================
// Unified tagging orchestrator for the "Automate Tagging" wizard.
// Uses the Strategy pattern: each tag category (door, window, wall, room,
// ceiling) has its own ITagStrategy implementation.
//
// Envelope payload (env.raw):
//   {
//     "tag_category": "door",       // door | window | wall | room | ceiling
//     "tag_family":   "A49_Door Tag",
//     "tag_type":     "Mark",
//     "view_ids":     [12345, 67890],
//     "skip_tagged":  true
//   }
//
// Results sent directly to Vue via SendRawMessage.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using A49AIRevitAssistant.Executor.Commands.TagStrategies;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class AutoTagCommand
    {
        private readonly UIApplication _uiapp;

        // Registered strategies. New strategies (Window, Wall, Room, Ceiling)
        // are added to this dictionary in later phases.
        private readonly Dictionary<string, ITagStrategy> _strategies;

        public AutoTagCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;

            _strategies = new Dictionary<string, ITagStrategy>(StringComparer.OrdinalIgnoreCase)
            {
                { "door",    new DoorTagStrategy() },
                { "window",  new WindowTagStrategy() },
                { "room",    new RoomTagStrategy() },
                { "wall",    new WallTagStrategy() },
                { "ceiling", new CeilingTagStrategy() }
            };
        }

        public string Execute(Models.RevitCommandEnvelope env)
        {
            try
            {
                Document doc = _uiapp.ActiveUIDocument.Document;

                // ─────────────────────────────────────────────────────
                // 1. PARSE PAYLOAD
                // ─────────────────────────────────────────────────────
                JObject payload = env.raw;
                if (payload == null)
                    return SendError("No tagging payload received.");

                string categoryKey = (payload.Value<string>("tag_category") ?? "").ToLowerInvariant();
                string tagFamily = payload.Value<string>("tag_family") ?? "";
                string tagType = payload.Value<string>("tag_type") ?? "";
                bool skipTagged = payload.Value<bool?>("skip_tagged") ?? true;

                var viewIdArray = payload["view_ids"] as JArray;
                if (viewIdArray == null || viewIdArray.Count == 0)
                    return SendError("No view IDs provided.");

                var viewIds = new List<ElementId>();
                foreach (var vid in viewIdArray)
                    viewIds.Add(new ElementId(vid.Value<long>()));

                // ─────────────────────────────────────────────────────
                // SPOT ELEVATION: SpotDimensionType, not FamilySymbol — handled inline
                // ─────────────────────────────────────────────────────
                if (categoryKey == "spot_elevation")
                    return ExecuteSpotElevation(doc, payload, viewIds, skipTagged);

                // ─────────────────────────────────────────────────────
                // 2. RESOLVE STRATEGY
                // ─────────────────────────────────────────────────────
                if (!_strategies.TryGetValue(categoryKey, out var strategy))
                    return SendError($"Tag category '{categoryKey}' is not yet supported.");

                A49Logger.Log($"🏷️ AutoTag: category='{categoryKey}', family='{tagFamily}', type='{tagType}', views={viewIds.Count}, skip={skipTagged}");

                // ─────────────────────────────────────────────────────
                // 3. RESOLVE TAG SYMBOL
                // ─────────────────────────────────────────────────────
                FamilySymbol tagSymbol = FindTagSymbol(doc, strategy.TagCategory, tagFamily, tagType);
                if (tagSymbol == null)
                    return SendError($"Tag type not found: '{tagFamily} : {tagType}'. Make sure the tag family is loaded in the project.");

                // ─────────────────────────────────────────────────────
                // 4. PROCESS VIEWS
                // ─────────────────────────────────────────────────────
                int totalTagged = 0;
                int totalSkipped = 0;
                int viewsProcessed = 0;
                var errors = new List<string>();

                using (Transaction tx = new Transaction(doc, $"Vella: Auto-Tag {categoryKey}s"))
                {
                    tx.Start();

                    if (!tagSymbol.IsActive) tagSymbol.Activate();

                    foreach (ElementId viewId in viewIds)
                    {
                        View view = doc.GetElement(viewId) as View;
                        if (view == null)
                        {
                            errors.Add($"View ID {viewId.Value} not found.");
                            continue;
                        }

                        if (!strategy.SupportsViewType(view.ViewType))
                        {
                            errors.Add($"Skipped '{view.Name}' — {view.ViewType} not supported for {categoryKey} tags.");
                            continue;
                        }

                        try
                        {
                            var vResult = strategy.TagElementsInView(doc, view, tagSymbol, skipTagged);
                            totalTagged += vResult.Tagged;
                            totalSkipped += vResult.Skipped;
                            viewsProcessed++;
                            if (vResult.Errors.Count > 0) errors.AddRange(vResult.Errors);

                            A49Logger.Log($"  ✅ View '{view.Name}': tagged={vResult.Tagged}, skipped={vResult.Skipped}");
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Error in view '{view.Name}': {ex.Message}");
                        }
                    }

                    tx.Commit();
                }

                // ─────────────────────────────────────────────────────
                // 5. BUILD RESULT
                // ─────────────────────────────────────────────────────
                var resultObj = new JObject
                {
                    ["auto_tag_result"] = new JObject
                    {
                        ["status"] = errors.Count == 0 ? "success" : "partial",
                        ["category"] = categoryKey,
                        ["tag_family"] = tagFamily,
                        ["tag_type"] = tagType,
                        ["tagged_count"] = totalTagged,
                        ["skipped_count"] = totalSkipped,
                        ["views_processed"] = viewsProcessed,
                        ["message"] = $"Tagged {totalTagged} {categoryKey}(s) across {viewsProcessed} view(s). Skipped {totalSkipped} already-tagged.",
                        ["errors"] = new JArray(errors.ToArray())
                    }
                };

                string jsonResult = JsonConvert.SerializeObject(resultObj);

                A49AIRevitAssistant.UI.DockablePaneViewer.Instance.Dispatcher.Invoke(() =>
                {
                    A49AIRevitAssistant.UI.DockablePaneViewer.Instance.SendRawMessage(jsonResult);
                });

                return "{\"status\":\"silent\"}";
            }
            catch (Exception ex)
            {
                A49Logger.Log($"❌ AutoTag Error: {ex.Message}\n{ex.StackTrace}");
                return SendError($"Auto-tag failed: {ex.Message}");
            }
        }

        // ============================================================================
        // FIND TAG SYMBOL BY CATEGORY + FAMILY + TYPE
        // ============================================================================
        private FamilySymbol FindTagSymbol(Document doc, BuiltInCategory tagCategory, string familyName, string typeName)
        {
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(tagCategory);

            foreach (FamilySymbol fs in collector)
            {
                string fName = fs.Family?.Name ?? "";
                string tName = fs.Name ?? "";

                if (fName.Equals(familyName, StringComparison.OrdinalIgnoreCase) &&
                    tName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                    return fs;
            }

            // Fallback: match by family name only, take first type
            foreach (FamilySymbol fs in collector)
            {
                string fName = fs.Family?.Name ?? "";
                if (fName.Equals(familyName, StringComparison.OrdinalIgnoreCase))
                {
                    A49Logger.Log($"⚠️ Type '{typeName}' not found, using '{fs.Name}'");
                    return fs;
                }
            }

            return null;
        }

        // ============================================================================
        // SPOT ELEVATION — ORCHESTRATOR
        // ============================================================================
        // Handles FloorPlan and Section views independently because they need
        // different SpotDimensionTypes and different element targets.
        // ============================================================================
        private string ExecuteSpotElevation(Document doc, JObject payload, List<ElementId> viewIds, bool skipTagged)
        {
            string planTypeName    = payload.Value<string>("spot_plan_type")    ?? "";
            string sectionTypeName = payload.Value<string>("spot_section_type") ?? "";

            int totalTagged = 0;
            int viewsProcessed = 0;
            var errors = new List<string>();

            A49Logger.Log($"🏷️ SpotElevation: planType='{planTypeName}', sectionType='{sectionTypeName}', views={viewIds.Count}");

            using (Transaction tx = new Transaction(doc, "Vella: Auto Spot Elevation"))
            {
                tx.Start();

                foreach (ElementId viewId in viewIds)
                {
                    View view = doc.GetElement(viewId) as View;
                    if (view == null) { errors.Add($"View {viewId.Value} not found."); continue; }

                    try
                    {
                        int tagged = 0;

                        if (view.ViewType == ViewType.FloorPlan)
                        {
                            SpotDimensionType spotType = FindSpotDimensionType(doc, planTypeName);
                            if (spotType == null)
                            {
                                errors.Add($"Spot type '{planTypeName}' not found in project.");
                                continue;
                            }
                            tagged = TagRoomsWithSpotElevation(doc, view, spotType, skipTagged);
                        }
                        else if (view.ViewType == ViewType.Section)
                        {
                            SpotDimensionType spotType = FindSpotDimensionType(doc, sectionTypeName);
                            if (spotType == null)
                            {
                                errors.Add($"Spot type '{sectionTypeName}' not found in project.");
                                continue;
                            }
                            tagged = TagFloorsWithSpotElevationInSection(doc, view, spotType, skipTagged);
                        }
                        else
                        {
                            errors.Add($"View type '{view.ViewType}' is not supported for spot elevations.");
                            continue;
                        }

                        totalTagged += tagged;
                        viewsProcessed++;
                        A49Logger.Log($"  ✅ View '{view.Name}': spot elevations placed={tagged}");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Error in view '{view.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            var resultObj = new JObject
            {
                ["auto_tag_result"] = new JObject
                {
                    ["status"]          = errors.Count == 0 ? "success" : "partial",
                    ["category"]        = "spot_elevation",
                    ["tag_family"]      = "Spot Elevations",
                    ["tag_type"]        = $"{planTypeName} / {sectionTypeName}",
                    ["tagged_count"]    = totalTagged,
                    ["skipped_count"]   = 0,
                    ["views_processed"] = viewsProcessed,
                    ["message"]         = $"Placed {totalTagged} spot elevation(s) across {viewsProcessed} view(s).",
                    ["errors"]          = new JArray(errors.ToArray())
                }
            };

            string jsonResult = JsonConvert.SerializeObject(resultObj);
            A49AIRevitAssistant.UI.DockablePaneViewer.Instance.Dispatcher.Invoke(() =>
            {
                A49AIRevitAssistant.UI.DockablePaneViewer.Instance.SendRawMessage(jsonResult);
            });

            return "{\"status\":\"silent\"}";
        }

        // ============================================================================
        // SPOT ELEVATION — FLOOR PLAN
        // ============================================================================
        // Iterates rooms visible in the floor plan view.
        // For each room, finds the floor slab beneath it and places a SpotElevation
        // on the slab's top face, offset 500 mm "below" the room centre in screen space.
        // ============================================================================
        private int TagRoomsWithSpotElevation(Document doc, View view, SpotDimensionType spotType, bool skipTagged)
        {
            int tagged = 0;

            var rooms = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0 && r.Location != null)
                .ToList();

            // Pre-collect floor IDs that already have a SpotDimension in this view.
            var taggedFloorIds = new HashSet<long>();
            if (skipTagged)
            {
                try
                {
                    var existingSpots = new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(SpotDimension))
                        .Cast<SpotDimension>();
                    foreach (SpotDimension sd in existingSpots)
                    {
                        try
                        {
                            var dim = sd as Dimension;
                            if (dim?.References != null)
                                foreach (Reference r in dim.References)
                                    if (r?.ElementId != null) taggedFloorIds.Add(r.ElementId.Value);
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // Two-phase placement:
            //   1. Place each spot elevation directly at the room centre (zero-length leader).
            //   2. After the loop, batch-move all newly placed tags 500 mm "down on screen"
            //      using ElementTransformUtils — equivalent to a manual select-all + Move.
            var newSpotIds = new List<ElementId>();

            foreach (Room room in rooms)
            {
                try
                {
                    var locPt = room.Location as LocationPoint;
                    if (locPt == null) continue;

                    var (faceRef, floorTopZ, _) = GetFloorTopFaceInfo(doc, room, locPt.Point);
                    if (faceRef == null) continue;

                    if (skipTagged && taggedFloorIds.Contains(faceRef.ElementId.Value)) continue;

                    // Phase 1: place at room centre. origin = bend = end so the leader
                    // is zero-length. hasLeader=true is required for `end` to position
                    // the text — otherwise the SpotDimensionType's default text-offset
                    // shifts the value 700 mm to the right.
                    XYZ origin = new XYZ(locPt.Point.X, locPt.Point.Y, floorTopZ);
                    SpotDimension sd = doc.Create.NewSpotElevation(view, faceRef, origin, origin, origin, origin, true);
                    if (sd == null) continue;

                    try { sd.ChangeTypeId(spotType.Id); } catch { }
                    newSpotIds.Add(sd.Id);
                    tagged++;
                }
                catch (Exception ex)
                {
                    A49Logger.Log($"⚠️ SpotElev room {room.Id.Value}: {ex.Message}");
                }
            }

            // ────────────────────────────────────────────────────────────
            // LINKED PASS — rooms in linked Revit models. We look for the
            // floor in the SAME link doc (most A49 projects keep architecture
            // rooms+floors together in one link). The face Reference is built
            // local-to-link, then turned into a host-valid link reference via
            // CreateLinkReference. The placement origin is in host coords.
            // Mixed-doc cases (linked room over host floor, or vice-versa) are
            // out of scope for v1 — they fall through and produce no spot.
            // ────────────────────────────────────────────────────────────
            foreach (var link in TagStrategies.LinkedTagHelpers.EnumerateLinks(doc, view))
            {
                IEnumerable<Room> linkedRooms;
                try
                {
                    linkedRooms = new FilteredElementCollector(link.Document)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .Where(r => r.Area > 0 && r.Location != null);
                }
                catch (Exception ex)
                {
                    A49Logger.Log($"⚠️ SpotElev linked doc '{link.Document.Title}': {ex.Message}");
                    continue;
                }

                foreach (Room room in linkedRooms)
                {
                    try
                    {
                        var locPt = room.Location as LocationPoint;
                        if (locPt == null) continue;

                        // Crop check in host coords.
                        XYZ pointHost = link.Transform.OfPoint(locPt.Point);
                        if (view.CropBoxActive && !TagHelpers.IsElementInCropRegion(view, pointHost))
                            continue;

                        var (faceRef, floorTopZLink, _) = GetLinkedFloorTopFaceInfo(link, room, locPt.Point);
                        if (faceRef == null) continue;

                        // Translate the floor-top Z from link coords into host
                        // coords by transforming a point on the floor's top
                        // plane (using room XY for accuracy under link rotation).
                        XYZ originLink = new XYZ(locPt.Point.X, locPt.Point.Y, floorTopZLink);
                        XYZ originHost = link.Transform.OfPoint(originLink);

                        SpotDimension sd = doc.Create.NewSpotElevation(view, faceRef, originHost, originHost, originHost, originHost, true);
                        if (sd == null) continue;

                        try { sd.ChangeTypeId(spotType.Id); } catch { }
                        newSpotIds.Add(sd.Id);
                        tagged++;
                    }
                    catch (Exception ex)
                    {
                        A49Logger.Log($"⚠️ SpotElev linked room {room.Id.Value}: {ex.Message}");
                    }
                }
            }

            // Phase 2: batch-move all newly placed tags 500 mm down on screen.
            // view.UpDirection.Negate() = "down on screen" regardless of view rotation.
            if (newSpotIds.Count > 0)
            {
                try
                {
                    XYZ moveDown = view.UpDirection.Negate() * (1100.0 / 304.8);
                    ElementTransformUtils.MoveElements(doc, newSpotIds, moveDown);
                }
                catch (Exception ex)
                {
                    A49Logger.Log($"⚠️ SpotElev batch-move ({newSpotIds.Count} tags): {ex.Message}");
                }
            }

            return tagged;
        }

        // ============================================================================
        // SPOT ELEVATION — SECTION
        // ============================================================================
        // Iterates rooms cut by / visible in the section view.
        // For each room, finds the floor under it and places one SpotElevation at the
        // room's centre XY on the floor's top face. Label offsets 300 mm down (world -Z).
        // ============================================================================
        private int TagFloorsWithSpotElevationInSection(Document doc, View view, SpotDimensionType spotType, bool skipTagged)
        {
            int tagged = 0;

            // 1. Collect all placed rooms in the document, then filter to those inside
            //    the section's crop volume (rooms cut by or visible in this section).
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0 && r.Location != null)
                .Where(r => IsRoomInSectionView(r, view))
                .ToList();

            A49Logger.Log($"📐 SpotElev section '{view.Name}': {rooms.Count} room(s) in view");

            // 2. Pre-collect rooms that already have a SpotDimension placed near their
            //    plan area in this view (so re-runs don't double-tag).
            var taggedRoomIds = new HashSet<long>();
            if (skipTagged)
            {
                try
                {
                    var existingSpotPts = new List<XYZ>();
                    foreach (SpotDimension sd in new FilteredElementCollector(doc, view.Id)
                        .OfClass(typeof(SpotDimension))
                        .Cast<SpotDimension>())
                    {
                        try { if (sd.Origin != null) existingSpotPts.Add(sd.Origin); }
                        catch { }
                    }

                    foreach (Room r in rooms)
                    {
                        BoundingBoxXYZ rbb = r.get_BoundingBox(null);
                        if (rbb == null) continue;
                        bool hasSpot = existingSpotPts.Any(p =>
                            p.X >= rbb.Min.X && p.X <= rbb.Max.X &&
                            p.Y >= rbb.Min.Y && p.Y <= rbb.Max.Y);
                        if (hasSpot) taggedRoomIds.Add(r.Id.Value);
                    }
                }
                catch { }
            }

            // 3. In a section, view.UpDirection = (0,0,1), so Negate() = (0,0,-1) → label
            //    appears just below the floor cut line. Correct for any section orientation.
            double offsetFt = 300.0 / 304.8;
            XYZ downDir = view.UpDirection.Negate();

            foreach (Room room in rooms)
            {
                try
                {
                    if (skipTagged && taggedRoomIds.Contains(room.Id.Value)) continue;

                    var locPt = room.Location as LocationPoint;
                    if (locPt == null) continue;

                    var (faceRef, floorTopZ, _) = GetFloorTopFaceInfo(doc, room, locPt.Point);
                    if (faceRef == null) continue;

                    XYZ origin = new XYZ(locPt.Point.X, locPt.Point.Y, floorTopZ);
                    XYZ end    = origin + downDir * offsetFt;
                    XYZ bend   = origin + downDir * (offsetFt * 0.5);

                    SpotDimension sd = doc.Create.NewSpotElevation(view, faceRef, origin, bend, end, origin, false);
                    if (sd == null) continue;

                    try { sd.ChangeTypeId(spotType.Id); } catch { }
                    tagged++;
                }
                catch (Exception ex)
                {
                    A49Logger.Log($"⚠️ SpotElev section room {room.Id.Value}: {ex.Message}");
                }
            }

            // ────────────────────────────────────────────────────────────
            // LINKED PASS (section) — same shape as the floor-plan branch
            // above. We collect rooms from each link, filter by section
            // crop volume (in host coords), find the floor in the same link
            // doc, and place the spot using a host-coord origin and a
            // link-aware face reference.
            // ────────────────────────────────────────────────────────────
            foreach (var link in TagStrategies.LinkedTagHelpers.EnumerateLinks(doc, view))
            {
                IEnumerable<Room> linkedRooms;
                try
                {
                    linkedRooms = new FilteredElementCollector(link.Document)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .Where(r => r.Area > 0 && r.Location != null);
                }
                catch (Exception ex)
                {
                    A49Logger.Log($"⚠️ SpotElev section linked doc '{link.Document.Title}': {ex.Message}");
                    continue;
                }

                foreach (Room room in linkedRooms)
                {
                    try
                    {
                        var locPt = room.Location as LocationPoint;
                        if (locPt == null) continue;

                        // Visibility: re-use the section crop volume check on the
                        // transformed (host-coord) location point.
                        XYZ pointHost = link.Transform.OfPoint(locPt.Point);
                        if (!IsPointInSectionView(pointHost, view)) continue;

                        var (faceRef, floorTopZLink, _) = GetLinkedFloorTopFaceInfo(link, room, locPt.Point);
                        if (faceRef == null) continue;

                        XYZ originLink = new XYZ(locPt.Point.X, locPt.Point.Y, floorTopZLink);
                        XYZ originHost = link.Transform.OfPoint(originLink);
                        XYZ end  = originHost + downDir * offsetFt;
                        XYZ bend = originHost + downDir * (offsetFt * 0.5);

                        SpotDimension sd = doc.Create.NewSpotElevation(view, faceRef, originHost, bend, end, originHost, false);
                        if (sd == null) continue;

                        try { sd.ChangeTypeId(spotType.Id); } catch { }
                        tagged++;
                    }
                    catch (Exception ex)
                    {
                        A49Logger.Log($"⚠️ SpotElev section linked room {room.Id.Value}: {ex.Message}");
                    }
                }
            }

            return tagged;
        }

        // ============================================================================
        // LINKED FLOOR LOOKUP — mirror of GetFloorTopFaceInfo for linked rooms.
        // Searches the SAME link doc for the floor under the room. The face
        // Reference is built local-to-link, then converted to a host-valid
        // link reference via Reference.CreateLinkReference. The returned Z
        // is in LINK coords (caller transforms it to host).
        // ============================================================================
        private (Reference faceRef, double topZLink, XYZ facePointLink) GetLinkedFloorTopFaceInfo(
            TagStrategies.LinkContext link, Room room, XYZ roomCenterLink)
        {
            Floor floor = FindFloorUnderRoomInDoc(link.Document, room);
            if (floor == null) return (null, 0.0, null);

            var (localFaceRef, topZ, point) = GetFloorTopFaceAtPoint(link.Document, floor, roomCenterLink);
            if (localFaceRef == null) return (null, 0.0, null);

            try
            {
                Reference linkRef = localFaceRef.CreateLinkReference(link.Instance);
                return (linkRef, topZ, point);
            }
            catch
            {
                return (null, 0.0, null);
            }
        }

        // Same algorithm as FindFloorUnderRoom but takes the doc explicitly so
        // we can search a link's document instead of the host.
        private Floor FindFloorUnderRoomInDoc(Document searchDoc, Room room)
        {
            try
            {
                BoundingBoxXYZ roomBB = room.get_BoundingBox(null);
                if (roomBB == null) return null;

                var expandedMin = new XYZ(roomBB.Min.X, roomBB.Min.Y, roomBB.Min.Z - 2.0);
                var expandedMax = new XYZ(roomBB.Max.X, roomBB.Max.Y, roomBB.Max.Z);
                var outline  = new Outline(expandedMin, expandedMax);
                var bbFilter = new BoundingBoxIntersectsFilter(outline);

                var floors = new FilteredElementCollector(searchDoc)
                    .OfClass(typeof(Floor))
                    .WherePasses(bbFilter)
                    .Cast<Floor>()
                    .ToList();

                if (floors.Count == 0) return null;

                double roomBaseZ = roomBB.Min.Z;
                Floor  best      = null;
                double bestDelta = double.MaxValue;
                foreach (Floor f in floors)
                {
                    BoundingBoxXYZ fbb = f.get_BoundingBox(null);
                    if (fbb == null) continue;
                    double delta = Math.Abs(fbb.Max.Z - roomBaseZ);
                    if (delta < bestDelta) { bestDelta = delta; best = f; }
                }
                return best;
            }
            catch { return null; }
        }

        // Crop-volume containment for a host-coord point in a section view.
        // Mirror of IsRoomInSectionView but takes the point directly so the
        // linked branch can pre-transform.
        private bool IsPointInSectionView(XYZ pointHost, View view)
        {
            BoundingBoxXYZ crop = view.CropBox;
            if (crop == null) return true;
            try
            {
                XYZ localPt = crop.Transform.Inverse.OfPoint(pointHost);
                return localPt.X >= crop.Min.X && localPt.X <= crop.Max.X &&
                       localPt.Y >= crop.Min.Y && localPt.Y <= crop.Max.Y &&
                       localPt.Z >= crop.Min.Z && localPt.Z <= crop.Max.Z;
            }
            catch { return false; }
        }

        // Returns true if the room's location point lies inside the section view's
        // crop volume, transforming the world point into the crop box's local frame.
        private bool IsRoomInSectionView(Room room, View view)
        {
            var locPt = room.Location as LocationPoint;
            if (locPt == null) return false;

            BoundingBoxXYZ crop = view.CropBox;
            if (crop == null) return true;

            try
            {
                XYZ localPt = crop.Transform.Inverse.OfPoint(locPt.Point);
                return localPt.X >= crop.Min.X && localPt.X <= crop.Max.X &&
                       localPt.Y >= crop.Min.Y && localPt.Y <= crop.Max.Y &&
                       localPt.Z >= crop.Min.Z && localPt.Z <= crop.Max.Z;
            }
            catch
            {
                return false;
            }
        }

        // ============================================================================
        // GEOMETRY HELPERS
        // ============================================================================

        private (Reference faceRef, double topZ, XYZ facePoint) GetFloorTopFaceInfo(Document doc, Room room, XYZ roomCenter)
        {
            Floor floor = FindFloorUnderRoom(doc, room);
            if (floor == null) return (null, 0.0, null);
            return GetFloorTopFaceAtPoint(doc, floor, roomCenter);
        }

        // Returns the highest near-horizontal upward PlanarFace reference on the floor solid,
        // plus pf.Origin — a point guaranteed to lie on the face (used as NewSpotElevation origin).
        private (Reference faceRef, double topZ, XYZ facePoint) GetFloorTopFaceAtPoint(Document doc, Floor floor, XYZ nearPoint)
        {
            try
            {
                var opts = new Options
                {
                    ComputeReferences       = true,
                    IncludeNonVisibleObjects = false
                };

                GeometryElement geom = floor.get_Geometry(opts);

                Reference bestRef   = null;
                double    bestZ     = double.MinValue;
                XYZ       bestPoint = null;

                foreach (GeometryObject geomObj in geom)
                {
                    Solid solid = geomObj as Solid;
                    if (solid == null || solid.Faces.Size == 0) continue;

                    foreach (Face face in solid.Faces)
                    {
                        PlanarFace pf = face as PlanarFace;
                        if (pf == null) continue;
                        if (pf.FaceNormal.Z < 0.9) continue;   // only upward-facing horizontal faces

                        double faceZ = pf.Origin.Z;
                        if (faceZ > bestZ)
                        {
                            bestZ     = faceZ;
                            bestRef   = face.Reference;
                            bestPoint = pf.Origin;  // guaranteed on-face point
                        }
                    }
                }

                return (bestRef, bestZ, bestPoint);
            }
            catch
            {
                return (null, 0.0, null);
            }
        }

        // Finds the floor whose top-Z is closest to the room's base elevation.
        private Floor FindFloorUnderRoom(Document doc, Room room)
        {
            try
            {
                BoundingBoxXYZ roomBB = room.get_BoundingBox(null);
                if (roomBB == null) return null;

                // Search 2 ft below the room's lower face to catch the slab
                var expandedMin = new XYZ(roomBB.Min.X, roomBB.Min.Y, roomBB.Min.Z - 2.0);
                var expandedMax = new XYZ(roomBB.Max.X, roomBB.Max.Y, roomBB.Max.Z);

                var outline  = new Outline(expandedMin, expandedMax);
                var bbFilter = new BoundingBoxIntersectsFilter(outline);

                var floors = new FilteredElementCollector(doc)
                    .OfClass(typeof(Floor))
                    .WherePasses(bbFilter)
                    .Cast<Floor>()
                    .ToList();

                if (floors.Count == 0) return null;

                double roomBaseZ = roomBB.Min.Z;
                Floor  best      = null;
                double bestDelta = double.MaxValue;

                foreach (Floor f in floors)
                {
                    BoundingBoxXYZ fbb = f.get_BoundingBox(null);
                    if (fbb == null) continue;
                    double delta = Math.Abs(fbb.Max.Z - roomBaseZ);
                    if (delta < bestDelta) { bestDelta = delta; best = f; }
                }

                return best;
            }
            catch
            {
                return null;
            }
        }

        // ============================================================================
        // SPOT DIMENSION TYPE LOOKUP
        // ============================================================================
        private SpotDimensionType FindSpotDimensionType(Document doc, string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(SpotDimensionType))
                .Cast<SpotDimensionType>()
                .ToList();

            // Exact match first
            SpotDimensionType found = all.FirstOrDefault(
                sdt => sdt.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            // Fallback: first available type if nothing matched
            return found ?? all.FirstOrDefault();
        }

        // ============================================================================
        // ERROR HELPER
        // ============================================================================
        private string SendError(string message)
        {
            A49Logger.Log($"❌ AutoTag: {message}");

            var errorResult = new JObject
            {
                ["auto_tag_result"] = new JObject
                {
                    ["status"] = "error",
                    ["message"] = message,
                    ["tagged_count"] = 0,
                    ["skipped_count"] = 0,
                    ["views_processed"] = 0,
                    ["errors"] = new JArray()
                }
            };

            string jsonResult = JsonConvert.SerializeObject(errorResult);

            try
            {
                A49AIRevitAssistant.UI.DockablePaneViewer.Instance.Dispatcher.Invoke(() =>
                {
                    A49AIRevitAssistant.UI.DockablePaneViewer.Instance.SendRawMessage(jsonResult);
                });
            }
            catch { }

            return "{\"status\":\"silent\"}";
        }
    }
}
