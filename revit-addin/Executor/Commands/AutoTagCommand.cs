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

            // 500 mm offset downward in screen space (opposite to view.UpDirection)
            double offsetFt = 500.0 / 304.8;
            XYZ downDir = view.UpDirection.Negate();

            foreach (Room room in rooms)
            {
                try
                {
                    var locPt = room.Location as LocationPoint;
                    if (locPt == null) continue;

                    var (faceRef, floorTopZ, facePoint) = GetFloorTopFaceInfo(doc, room, locPt.Point);
                    if (faceRef == null || facePoint == null) continue;

                    if (skipTagged && taggedFloorIds.Contains(faceRef.ElementId.Value)) continue;

                    // origin/refPt must lie ON the face — use pf.Origin (guaranteed on-face).
                    // end/bend use room-centre XY so the label appears below the room centre.
                    XYZ origin = facePoint;
                    XYZ end    = new XYZ(locPt.Point.X + downDir.X * offsetFt,
                                        locPt.Point.Y + downDir.Y * offsetFt,
                                        floorTopZ);
                    XYZ bend   = new XYZ(locPt.Point.X + downDir.X * offsetFt * 0.5,
                                        locPt.Point.Y + downDir.Y * offsetFt * 0.5,
                                        floorTopZ);

                    SpotDimension sd = doc.Create.NewSpotElevation(view, faceRef, origin, bend, end, origin, false);
                    if (sd == null) continue;

                    try { sd.ChangeTypeId(spotType.Id); } catch { }
                    tagged++;
                }
                catch (Exception ex)
                {
                    A49Logger.Log($"⚠️ SpotElev room {room.Id.Value}: {ex.Message}");
                }
            }

            return tagged;
        }

        // ============================================================================
        // SPOT ELEVATION — SECTION
        // ============================================================================
        // Iterates Floor elements visible in the section view.
        // Places one SpotElevation per floor on its top face, 300 mm below the face
        // along the view's screen-down direction.
        // ============================================================================
        private int TagFloorsWithSpotElevationInSection(Document doc, View view, SpotDimensionType spotType, bool skipTagged)
        {
            int tagged = 0;

            var floors = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Floor))
                .Cast<Floor>()
                .ToList();

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

            double offsetFt = 300.0 / 304.8;
            XYZ downDir = view.UpDirection.Negate();

            foreach (Floor floor in floors)
            {
                try
                {
                    if (skipTagged && taggedFloorIds.Contains(floor.Id.Value)) continue;

                    // Use world-space bounding box to get a centre point for reference lookup
                    BoundingBoxXYZ bb = floor.get_BoundingBox(null);
                    if (bb == null) continue;

                    XYZ worldMid = new XYZ(
                        (bb.Min.X + bb.Max.X) / 2.0,
                        (bb.Min.Y + bb.Max.Y) / 2.0,
                        bb.Max.Z);

                    var (faceRef, floorTopZ, facePoint) = GetFloorTopFaceAtPoint(doc, floor, worldMid);
                    if (faceRef == null || facePoint == null) continue;

                    XYZ origin = facePoint;
                    XYZ end    = new XYZ(worldMid.X + downDir.X * offsetFt,
                                        worldMid.Y + downDir.Y * offsetFt,
                                        floorTopZ);
                    XYZ bend   = new XYZ(worldMid.X + downDir.X * offsetFt * 0.5,
                                        worldMid.Y + downDir.Y * offsetFt * 0.5,
                                        floorTopZ);

                    SpotDimension sd = doc.Create.NewSpotElevation(view, faceRef, origin, bend, end, origin, false);
                    if (sd == null) continue;

                    try { sd.ChangeTypeId(spotType.Id); } catch { }
                    tagged++;
                }
                catch (Exception ex)
                {
                    A49Logger.Log($"⚠️ SpotElev floor {floor.Id.Value}: {ex.Message}");
                }
            }

            return tagged;
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
