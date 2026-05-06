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
//
// Revit 2021 adaptations vs canonical (revit-addin/):
//   - new ElementId(long) → new ElementId((int)long)
//   - ElementId.Value → ElementId.IntegerValue
//   - Reference.LinkedElementId is Revit 2022+ — CollectTaggedFloorKeys
//     simplifies to host-only floor keys (linked-floor dedup unavailable).
//   - Reference.CreateLinkReference is Revit 2022+ — FindFloorRefForRoom's
//     linked-floor branch is unreachable in 2021 (LinkedTagHelpers.EnumerateLinks
//     yields nothing) AND defensively `continue`s if it ever does run.
//   - Linked-room spot-elevation paths are dead code in 2021 (EnumerateLinks
//     stub) but kept structurally for parity with revit-addin/.
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
                    viewIds.Add(new ElementId((int)vid.Value<long>()));

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
                            errors.Add($"View ID {viewId.IntegerValue} not found.");
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
                    if (view == null) { errors.Add($"View {viewId.IntegerValue} not found."); continue; }

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
        private int TagRoomsWithSpotElevation(Document doc, View view, SpotDimensionType spotType, bool skipTagged)
        {
            int tagged = 0;

            var rooms = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0 && r.Location != null)
                .ToList();

            // Pre-collect dedup keys for floors already tagged in this view.
            var taggedFloorKeys = skipTagged
                ? CollectTaggedFloorKeys(doc, view)
                : new HashSet<string>();

            // Two-phase placement:
            //   1. Place each spot elevation directly at the room centre (zero-length leader).
            //   2. After the loop, batch-move all newly placed tags 500 mm "down on screen".
            var newSpotIds = new List<ElementId>();

            foreach (Room room in rooms)
            {
                try
                {
                    var locPt = room.Location as LocationPoint;
                    if (locPt == null) continue;

                    var (faceRef, originHost, dedup, foundLabel) =
                        FindFloorRefForRoom(doc, view, null, room, locPt.Point);
                    if (faceRef == null) continue;
                    if (skipTagged && dedup != null && taggedFloorKeys.Contains(dedup)) continue;

                    SpotDimension sd = doc.Create.NewSpotElevation(view, faceRef, originHost, originHost, originHost, originHost, true);
                    if (sd == null)
                    {
                        A49Logger.Log($"⚠️ SpotElev plan: NewSpotElevation returned null for host room {room.Id.IntegerValue} (floor source: {foundLabel})");
                        continue;
                    }

                    try { sd.ChangeTypeId(spotType.Id); } catch { }
                    newSpotIds.Add(sd.Id);
                    tagged++;
                }
                catch (Exception ex)
                {
                    A49Logger.Log($"⚠️ SpotElev room {room.Id.IntegerValue}: {ex.Message}");
                }
            }

            // ────────────────────────────────────────────────────────────
            // LINKED PASS — REVIT 2021: stubbed via LinkedTagHelpers
            // (yields nothing). Loop body never executes. Kept for parity.
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

                        XYZ pointHost = link.Transform.OfPoint(locPt.Point);
                        if (view.CropBoxActive && !TagHelpers.IsElementInCropRegion(view, pointHost))
                            continue;

                        var (faceRef, originHost, dedup, foundLabel) =
                            FindFloorRefForRoom(doc, view, link, room, locPt.Point);
                        if (faceRef == null) continue;
                        if (skipTagged && dedup != null && taggedFloorKeys.Contains(dedup)) continue;

                        SpotDimension sd = doc.Create.NewSpotElevation(view, faceRef, originHost, originHost, originHost, originHost, true);
                        if (sd == null) continue;

                        try { sd.ChangeTypeId(spotType.Id); } catch { }
                        newSpotIds.Add(sd.Id);
                        tagged++;
                    }
                    catch (Exception ex)
                    {
                        A49Logger.Log($"⚠️ SpotElev linked room {room.Id.IntegerValue}: {ex.Message}");
                    }
                }
            }

            // Phase 2: batch-move all newly placed tags 500 mm down on screen.
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
        private int TagFloorsWithSpotElevationInSection(Document doc, View view, SpotDimensionType spotType, bool skipTagged)
        {
            int tagged = 0;

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0 && r.Location != null)
                .Where(r => IsRoomInSectionView(r, view))
                .ToList();

            A49Logger.Log($"📐 SpotElev section '{view.Name}': {rooms.Count} room(s) in view");

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
                        if (hasSpot) taggedRoomIds.Add(r.Id.IntegerValue);
                    }
                }
                catch { }
            }

            double offsetFt = 300.0 / 304.8;
            XYZ downDir = view.UpDirection.Negate();

            var taggedFloorKeysSec = skipTagged
                ? CollectTaggedFloorKeys(doc, view)
                : new HashSet<string>();

            foreach (Room room in rooms)
            {
                try
                {
                    if (skipTagged && taggedRoomIds.Contains(room.Id.IntegerValue)) continue;

                    var locPt = room.Location as LocationPoint;
                    if (locPt == null) continue;

                    var (faceRef, originHost, dedup, foundLabel) =
                        FindFloorRefForRoom(doc, view, null, room, locPt.Point);
                    if (faceRef == null) continue;
                    if (skipTagged && dedup != null && taggedFloorKeysSec.Contains(dedup)) continue;

                    XYZ end  = originHost + downDir * offsetFt;
                    XYZ bend = originHost + downDir * (offsetFt * 0.5);

                    SpotDimension sd = doc.Create.NewSpotElevation(view, faceRef, originHost, bend, end, originHost, false);
                    if (sd == null)
                    {
                        A49Logger.Log($"⚠️ SpotElev section: NewSpotElevation returned null for host room {room.Id.IntegerValue} (floor source: {foundLabel})");
                        continue;
                    }

                    try { sd.ChangeTypeId(spotType.Id); } catch { }
                    tagged++;
                }
                catch (Exception ex)
                {
                    A49Logger.Log($"⚠️ SpotElev section room {room.Id.IntegerValue}: {ex.Message}");
                }
            }

            // ────────────────────────────────────────────────────────────
            // LINKED PASS (section) — REVIT 2021: stubbed (dead code).
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

                        XYZ pointHost = link.Transform.OfPoint(locPt.Point);
                        if (!IsPointInSectionView(pointHost, view)) continue;

                        var (faceRef, originHost, dedup, foundLabel) =
                            FindFloorRefForRoom(doc, view, link, room, locPt.Point);
                        if (faceRef == null) continue;
                        if (skipTagged && dedup != null && taggedFloorKeysSec.Contains(dedup)) continue;

                        XYZ end  = originHost + downDir * offsetFt;
                        XYZ bend = originHost + downDir * (offsetFt * 0.5);

                        SpotDimension sd = doc.Create.NewSpotElevation(view, faceRef, originHost, bend, end, originHost, false);
                        if (sd == null) continue;

                        try { sd.ChangeTypeId(spotType.Id); } catch { }
                        tagged++;
                    }
                    catch (Exception ex)
                    {
                        A49Logger.Log($"⚠️ SpotElev section linked room {room.Id.IntegerValue}: {ex.Message}");
                    }
                }
            }

            return tagged;
        }

        // ============================================================================
        // FLOOR-UNDER-ROOM LOOKUP — Revit 2021 variant
        // ============================================================================
        // Same multi-doc search as canonical, but the linked-floor branch is
        // unreachable in 2021 because:
        //   1. LinkedTagHelpers.EnumerateLinks yields nothing → no linked
        //      entries get added to `searches`.
        //   2. Even if a caller passes roomLink != null somehow,
        //      Reference.CreateLinkReference doesn't exist in 2021's API
        //      so we'd `continue` rather than try to call it.
        // Net: only the host-doc search ever runs in Revit 2021. Linked-floor
        // dedup keys are never produced. Fine for projects without links.
        // ============================================================================
        private (Reference faceRef, XYZ originHost, string dedupKey, string foundLabel)
            FindFloorRefForRoom(Document hostDoc, View view, TagStrategies.LinkContext roomLink, Room room, XYZ roomCenter)
        {
            Transform roomToHost = (roomLink != null) ? roomLink.Transform : Transform.Identity;

            var searches = new List<(Document doc, Transform xform, RevitLinkInstance link, string label)>();

            if (roomLink != null)
            {
                searches.Add((roomLink.Document, roomLink.Transform, roomLink.Instance,
                              $"linked: {roomLink.Document.Title}"));
            }

            searches.Add((hostDoc, Transform.Identity, null, "host"));

            try
            {
                foreach (var other in TagStrategies.LinkedTagHelpers.EnumerateLinks(hostDoc, view))
                {
                    if (roomLink != null && other.Instance.Id == roomLink.Instance.Id) continue;
                    searches.Add((other.Document, other.Transform, other.Instance,
                                  $"linked: {other.Document.Title}"));
                }
            }
            catch { }

            BoundingBoxXYZ roomBB = null;
            try { roomBB = room.get_BoundingBox(null); } catch { }
            if (roomBB == null) return (null, null, null, null);

            foreach (var s in searches)
            {
                try
                {
                    Transform roomToSearch = s.xform.Inverse.Multiply(roomToHost);
                    XYZ pA = roomToSearch.OfPoint(roomBB.Min);
                    XYZ pB = roomToSearch.OfPoint(roomBB.Max);
                    XYZ minN = new XYZ(Math.Min(pA.X, pB.X), Math.Min(pA.Y, pB.Y), Math.Min(pA.Z, pB.Z));
                    XYZ maxN = new XYZ(Math.Max(pA.X, pB.X), Math.Max(pA.Y, pB.Y), Math.Max(pA.Z, pB.Z));

                    Floor floor = FindFloorByBBox(s.doc, minN, maxN);
                    if (floor == null) continue;

                    XYZ centerSearch = roomToSearch.OfPoint(roomCenter);
                    var (localFaceRef, topZ, _) = GetFloorTopFaceAtPoint(s.doc, floor, centerSearch);
                    if (localFaceRef == null) continue;

                    Reference hostFaceRef;
                    string dedup;
                    if (s.link == null)
                    {
                        hostFaceRef = localFaceRef;
                        dedup = $"F{floor.Id.IntegerValue}";
                    }
                    else
                    {
                        // Revit 2021: Reference.CreateLinkReference doesn't exist (Revit 2022+).
                        // We can't make a host-valid link reference here, so skip linked floors.
                        continue;
                    }

                    XYZ originSearch = new XYZ(centerSearch.X, centerSearch.Y, topZ);
                    XYZ originHost = s.xform.OfPoint(originSearch);

                    return (hostFaceRef, originHost, dedup, s.label);
                }
                catch (Exception ex)
                {
                    A49Logger.Log($"⚠️ SpotElev floor search '{s.label}' for room {room.Id.IntegerValue}: {ex.Message}");
                }
            }

            return (null, null, null, null);
        }

        // ============================================================================
        // CollectTaggedFloorKeys — Revit 2021 variant (host floors only)
        // ============================================================================
        // Canonical uses Reference.LinkedElementId (Revit 2022+) to distinguish
        // host-floor refs from linked-floor refs. In 2021 we only have
        // Reference.ElementId, so we produce host-floor keys exclusively.
        // Linked-floor dedup is unavailable — but since the linked-floor branch
        // in FindFloorRefForRoom is dead code in 2021 anyway, we never produce
        // linked-floor spot tags to dedup.
        // ============================================================================
        private HashSet<string> CollectTaggedFloorKeys(Document doc, View view)
        {
            var keys = new HashSet<string>();
            try
            {
                foreach (SpotDimension sd in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(SpotDimension))
                    .Cast<SpotDimension>())
                {
                    try
                    {
                        var refs = (sd as Dimension)?.References;
                        if (refs == null) continue;
                        foreach (Reference r in refs)
                        {
                            if (r == null || r.ElementId == null) continue;
                            keys.Add($"F{r.ElementId.IntegerValue}");
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return keys;
        }

        // BBox-first floor search shared by host and link doc lookups.
        private Floor FindFloorByBBox(Document searchDoc, XYZ minPt, XYZ maxPt)
        {
            try
            {
                var expandedMin = new XYZ(minPt.X, minPt.Y, minPt.Z - 2.0);
                var expandedMax = new XYZ(maxPt.X, maxPt.Y, maxPt.Z);
                var outline = new Outline(expandedMin, expandedMax);
                var bbFilter = new BoundingBoxIntersectsFilter(outline);

                var floors = new FilteredElementCollector(searchDoc)
                    .OfClass(typeof(Floor))
                    .WherePasses(bbFilter)
                    .Cast<Floor>()
                    .ToList();

                if (floors.Count == 0) return null;

                double roomBaseZ = minPt.Z;
                Floor best = null;
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

        // Same algorithm as FindFloorUnderRoom but takes the doc explicitly so
        // we can search a link's document instead of the host. (Dead code in
        // 2021 because EnumerateLinks is stubbed, but kept for parity.)
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
                        if (pf.FaceNormal.Z < 0.9) continue;

                        double faceZ = pf.Origin.Z;
                        if (faceZ > bestZ)
                        {
                            bestZ     = faceZ;
                            bestRef   = face.Reference;
                            bestPoint = pf.Origin;
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

        private Floor FindFloorUnderRoom(Document doc, Room room)
        {
            try
            {
                BoundingBoxXYZ roomBB = room.get_BoundingBox(null);
                if (roomBB == null) return null;

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

            SpotDimensionType found = all.FirstOrDefault(
                sdt => sdt.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

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
