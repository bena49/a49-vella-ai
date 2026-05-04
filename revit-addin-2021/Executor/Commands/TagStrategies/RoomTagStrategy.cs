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

            return result;
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
