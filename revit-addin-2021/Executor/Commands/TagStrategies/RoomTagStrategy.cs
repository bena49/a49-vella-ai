// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/RoomTagStrategy.cs
// ============================================================================
// Tags rooms in Plan (Floor + Ceiling), Elevation, and Section views.
//
// PLAN (FloorPlan / CeilingPlan / AreaPlan / EngineeringPlan):
//   - Uses RoomTag.Create() — Revit's native room tag API
//   - Auto-places at the room's calculation point (Revit default)
//   - No leader
//
// ELEVATION / SECTION:
//   - Tag at room's XY center, Z = (room level elevation) + 2000mm
//   - Uses IndependentTag.Create()
//   - SKIPS rooms whose 2000mm-AFF point falls outside the view's crop box
//     (Option A — match Revit's visibility behavior)
//   - No leader
//
// Revit 2021 adaptations vs canonical (revit-addin/):
//   - IndependentTag.GetTaggedLocalElementIds() → TaggedLocalElementId (single)
//   - Category.BuiltInCategory → (BuiltInCategory)Category.Id.IntegerValue
//   - ElementId.Value → ElementId.IntegerValue
//   - Linked pass is dead code (LinkedTagHelpers stubbed) — kept for parity.
//     Note: the linked-room plan path uses NewRoomTag(LinkElementId, …) which
//     IS available in Revit 2021's API, but the loop never enters because
//     EnumerateLinks yields nothing. Future enhancement: un-stub
//     EnumerateLinks specifically for room plan tagging if needed.
// ============================================================================

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace A49AIRevitAssistant.Executor.Commands.TagStrategies
{
    public class RoomTagStrategy : ITagStrategy
    {
        public string CategoryKey => "room";
        public BuiltInCategory TargetCategory => BuiltInCategory.OST_Rooms;
        public BuiltInCategory TagCategory => BuiltInCategory.OST_RoomTags;

        // Height above floor for elev/section tag placement (mm → feet)
        private const double TAG_HEIGHT_AFF_FEET = 2000.0 / 304.8;

        public bool SupportsViewType(ViewType viewType)
        {
            return viewType == ViewType.FloorPlan
                || viewType == ViewType.CeilingPlan
                || viewType == ViewType.AreaPlan
                || viewType == ViewType.EngineeringPlan
                || viewType == ViewType.Elevation
                || viewType == ViewType.Section;
        }

        public TagResult TagElementsInView(
            Document doc,
            View view,
            FamilySymbol tagSymbol,
            bool skipTagged)
        {
            var result = new TagResult();

            var roomCollector = new FilteredElementCollector(doc, view.Id)
                .OfCategory(TargetCategory)
                .WhereElementIsNotElementType();

            // Pre-collect already-tagged room IDs in this view.
            // Room tags are stored as RoomTag (not IndependentTag), so we collect both.
            var alreadyTaggedIds = new HashSet<long>();
            if (skipTagged)
            {
                // Native RoomTag collector
                var roomTagCollector = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(SpatialElementTag));
                foreach (SpatialElementTag existingTag in roomTagCollector)
                {
                    try
                    {
                        if (existingTag is RoomTag rt && rt.Room != null)
                            alreadyTaggedIds.Add(rt.Room.Id.IntegerValue);
                    }
                    catch { }
                }

                // Also check IndependentTag (for elev/section tags we placed previously)
                var indepTagCollector = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag));
                foreach (IndependentTag existingTag in indepTagCollector)
                {
                    try
                    {
                        ElementId taggedId = existingTag.TaggedLocalElementId;
                        if (taggedId != null && taggedId != ElementId.InvalidElementId)
                        {
                            Element el = doc.GetElement(taggedId);
                            if (el != null && el.Category != null &&
                                (BuiltInCategory)el.Category.Id.IntegerValue == TargetCategory)
                            {
                                alreadyTaggedIds.Add(taggedId.IntegerValue);
                            }
                        }
                    }
                    catch { }
                }
            }

            bool isPlan = view.ViewType == ViewType.FloorPlan
                       || view.ViewType == ViewType.CeilingPlan
                       || view.ViewType == ViewType.AreaPlan
                       || view.ViewType == ViewType.EngineeringPlan;

            // For elev/section: precompute the view's crop box (if enabled)
            BoundingBoxXYZ cropBox = null;
            if (!isPlan && view.CropBoxActive)
                cropBox = view.CropBox;

            foreach (Room room in roomCollector)
            {
                try
                {
                    if (room.Area <= 0 || room.Location == null) continue;  // unplaced rooms

                    if (skipTagged && alreadyTaggedIds.Contains(room.Id.IntegerValue))
                    {
                        result.Skipped++;
                        continue;
                    }

                    if (isPlan)
                    {
                        // Plan view: use native RoomTag.Create at calculation point
                        if (TagRoomInPlan(doc, view, room, tagSymbol))
                            result.Tagged++;
                    }
                    else
                    {
                        // Elev/Section: IndependentTag at room XY + 1200mm AFF
                        if (TagRoomInElevSection(doc, view, room, tagSymbol, cropBox))
                            result.Tagged++;
                        else
                            result.Skipped++;  // outside crop box
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Room {room.Id.IntegerValue}: {ex.Message}");
                }
            }

            // ====================================================================
            // LINKED PASS — REVIT 2021: stubbed via LinkedTagHelpers (yields nothing).
            // Loop body never executes. Kept for structural parity with revit-addin/.
            // ====================================================================
            foreach (var link in LinkedTagHelpers.EnumerateLinks(doc, view))
            {
                HashSet<long> linkedAlreadyTagged = new HashSet<long>();
                if (skipTagged)
                {
                    try
                    {
                        var roomTagCollector = new FilteredElementCollector(doc, view.Id)
                            .OfClass(typeof(SpatialElementTag));
                        foreach (SpatialElementTag existingTag in roomTagCollector)
                        {
                            try
                            {
                                if (!(existingTag is RoomTag rt)) continue;
                                LinkElementId leid = rt.TaggedRoomId;
                                if (leid == null) continue;
                                if (leid.LinkInstanceId == link.Instance.Id &&
                                    leid.LinkedElementId != ElementId.InvalidElementId)
                                {
                                    linkedAlreadyTagged.Add(leid.LinkedElementId.IntegerValue);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }

                    foreach (long id in LinkedTagHelpers.CollectAlreadyTaggedLinkedIds(
                                 doc, view, link.Instance.Id, TargetCategory))
                    {
                        linkedAlreadyTagged.Add(id);
                    }
                }

                FilteredElementCollector linkedRooms;
                try
                {
                    linkedRooms = new FilteredElementCollector(link.Document)
                        .OfCategory(TargetCategory)
                        .WhereElementIsNotElementType();
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Linked doc '{link.Document.Title}': {ex.Message}");
                    continue;
                }

                foreach (Room room in linkedRooms)
                {
                    try
                    {
                        if (room.Area <= 0 || room.Location == null) continue;

                        if (skipTagged && linkedAlreadyTagged.Contains(room.Id.IntegerValue))
                        {
                            result.Skipped++;
                            continue;
                        }

                        if (isPlan)
                        {
                            if (TagLinkedRoomInPlan(doc, view, room, tagSymbol, link))
                                result.Tagged++;
                        }
                        else
                        {
                            if (TagLinkedRoomInElevSection(doc, view, room, tagSymbol, cropBox, link))
                                result.Tagged++;
                            else
                                result.Skipped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Linked room {room.Id.IntegerValue}: {ex.Message}");
                    }
                }
            }

            return result;
        }

        // ============================================================================
        // LINKED ROOM — PLAN VIEW (dead code in 2021)
        // ============================================================================
        private bool TagLinkedRoomInPlan(Document hostDoc, View view, Room room,
                                        FamilySymbol tagSymbol, LinkContext link)
        {
            var locPt = room.Location as LocationPoint;
            if (locPt == null) return false;

            XYZ pointHost = link.Transform.OfPoint(locPt.Point);
            var linkId = new LinkElementId(link.Instance.Id, room.Id);
            var uv = new UV(pointHost.X, pointHost.Y);

            RoomTag newTag = hostDoc.Create.NewRoomTag(linkId, uv, view.Id);
            if (newTag == null) return false;

            try
            {
                if (newTag.GetTypeId() != tagSymbol.Id)
                    newTag.ChangeTypeId(tagSymbol.Id);
            }
            catch { }
            try { newTag.HasLeader = false; } catch { }

            return true;
        }

        // ============================================================================
        // LINKED ROOM — ELEVATION / SECTION (dead code in 2021)
        // ============================================================================
        private bool TagLinkedRoomInElevSection(Document hostDoc, View view, Room room,
                                                FamilySymbol tagSymbol, BoundingBoxXYZ cropBox,
                                                LinkContext link)
        {
            var locPt = room.Location as LocationPoint;
            if (locPt == null) return false;

            XYZ pointHost = link.Transform.OfPoint(locPt.Point);

            double levelZLink = (room.Level != null)
                ? room.Level.Elevation + TAG_HEIGHT_AFF_FEET
                : locPt.Point.Z + TAG_HEIGHT_AFF_FEET;
            XYZ tagPointLink = new XYZ(locPt.Point.X, locPt.Point.Y, levelZLink);
            XYZ tagPointHost = link.Transform.OfPoint(tagPointLink);

            if (cropBox != null && !IsPointInCropBox(tagPointHost, view, cropBox))
                return false;

            Reference linkedRef = LinkedTagHelpers.BuildLinkedReference(room, link.Instance);
            if (linkedRef == null) return false;

            var newTag = IndependentTag.Create(
                hostDoc, tagSymbol.Id, view.Id,
                linkedRef, false, TagOrientation.Horizontal, tagPointHost);

            return newTag != null;
        }

        // ============================================================================
        // PLAN VIEW TAGGING
        // ============================================================================
        // Uses RoomTag.Create — Revit places automatically at room's calculation point.
        // ============================================================================
        private bool TagRoomInPlan(Document doc, View view, Room room, FamilySymbol tagSymbol)
        {
            var locPt = room.Location as LocationPoint;
            if (locPt == null) return false;

            // LinkElementId - for rooms in the current model, use InvalidElementId
            var linkId = new LinkElementId(room.Id);

            // Use a UV at the room's location point (Revit expects a UV in world XY)
            var uv = new UV(locPt.Point.X, locPt.Point.Y);

            RoomTag newTag = doc.Create.NewRoomTag(linkId, uv, view.Id);
            if (newTag == null) return false;

            // Apply the selected tag family+type (NewRoomTag uses the active type by default)
            try
            {
                if (newTag.GetTypeId() != tagSymbol.Id)
                    newTag.ChangeTypeId(tagSymbol.Id);
            }
            catch { /* best effort — leave default type if change fails */ }

            // Remove leader (defensive — RoomTag usually has none by default)
            try { newTag.HasLeader = false; } catch { }

            return true;
        }

        // ============================================================================
        // ELEVATION / SECTION TAGGING
        // ============================================================================
        // Tag at room's XY center, Z = room level elevation + 1200mm.
        // Skips rooms whose placement point falls outside the view's crop box.
        // ============================================================================
        private bool TagRoomInElevSection(Document doc, View view, Room room,
                                          FamilySymbol tagSymbol, BoundingBoxXYZ cropBox)
        {
            var locPt = room.Location as LocationPoint;
            if (locPt == null) return false;

            // Room's XY from its location point
            double x = locPt.Point.X;
            double y = locPt.Point.Y;

            // Z = room level elevation + 1200mm
            double z = 0.0;
            if (room.Level != null)
                z = room.Level.Elevation + TAG_HEIGHT_AFF_FEET;
            else
                z = locPt.Point.Z + TAG_HEIGHT_AFF_FEET;

            XYZ tagPoint = new XYZ(x, y, z);

            // Crop box visibility check (Option A: skip if outside)
            if (cropBox != null && !IsPointInCropBox(tagPoint, view, cropBox))
                return false;

            var roomRef = new Reference(room);
            var newTag = IndependentTag.Create(
                doc,
                tagSymbol.Id,
                view.Id,
                roomRef,
                false,                      // no leader
                TagOrientation.Horizontal,
                tagPoint
            );

            return newTag != null;
        }

        // ============================================================================
        // CROP BOX VISIBILITY CHECK
        // ============================================================================
        // Transforms the world-space point into the view's local coordinates and
        // checks if it's inside the crop box rectangle (XY only — Z/depth is
        // less reliable across view types).
        // ============================================================================
        private bool IsPointInCropBox(XYZ worldPoint, View view, BoundingBoxXYZ cropBox)
        {
            try
            {
                // CropBox.Transform converts from view-local coords to world coords.
                // To check world point against the view-local crop rectangle, invert it.
                Transform inv = cropBox.Transform.Inverse;
                XYZ localPoint = inv.OfPoint(worldPoint);

                return localPoint.X >= cropBox.Min.X && localPoint.X <= cropBox.Max.X
                    && localPoint.Y >= cropBox.Min.Y && localPoint.Y <= cropBox.Max.Y;
            }
            catch
            {
                // If transform fails, default to including the point (safer than hiding)
                return true;
            }
        }
    }
}
