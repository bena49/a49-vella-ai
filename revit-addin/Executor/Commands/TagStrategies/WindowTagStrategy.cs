// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/WindowTagStrategy.cs
// ============================================================================
// Tags windows in Plan, Elevation, and Section views.
//
// PLAN (FloorPlan / AreaPlan / EngineeringPlan):
//   - Determine which side of the wall is "outside":
//       Cast a test point 850mm on each side of the window along FacingOrientation
//       and check GetRoomAtPoint() at both points.
//       • One side has a room, the other doesn't → window is in an EXTERIOR wall.
//         Tag goes on the EXTERIOR side (the side without a room).
//       • Both sides have rooms → window is in an INTERIOR wall.
//         Tag goes on the OPERABLE side (= FacingOrientation direction).
//       • Edge case (neither side has a room) → default to FacingOrientation side.
//   - Offset: 700mm from wall face (adds wall half-thickness for wall-centerline math)
//   - No leader
//
// ELEVATION / SECTION:
//   - Tag at window bounding-box center in the view
//   - No leader
// ============================================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System;
using System.Collections.Generic;

namespace A49AIRevitAssistant.Executor.Commands.TagStrategies
{
    public class WindowTagStrategy : ITagStrategy
    {
        public string CategoryKey => "window";
        public BuiltInCategory TargetCategory => BuiltInCategory.OST_Windows;
        public BuiltInCategory TagCategory => BuiltInCategory.OST_WindowTags;

        // Plan-view offset (mm → feet)
        private const double PLAN_OFFSET_FEET = 700.0 / 304.8;

        // Ray-cast distance for room detection (mm → feet)
        // 850mm clears any reasonable wall thickness (walls are typically < 500mm)
        private const double ROOM_TEST_DISTANCE_FEET = 850.0 / 304.8;

        public bool SupportsViewType(ViewType viewType)
        {
            return viewType == ViewType.FloorPlan
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

            var windowCollector = new FilteredElementCollector(doc, view.Id)
                .OfCategory(TargetCategory)
                .OfClass(typeof(FamilyInstance));

            // Pre-collect already-tagged window IDs in this view
            var alreadyTaggedIds = new HashSet<long>();
            if (skipTagged)
            {
                var tagCollector = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag));

                foreach (IndependentTag existingTag in tagCollector)
                {
                    try
                    {
                        foreach (var taggedId in existingTag.GetTaggedLocalElementIds())
                        {
                            Element el = doc.GetElement(taggedId);
                            if (el != null && el.Category != null &&
                                el.Category.BuiltInCategory == TargetCategory)
                            {
                                alreadyTaggedIds.Add(taggedId.Value);
                            }
                        }
                    }
                    catch { }
                }
            }

            bool isPlan = view.ViewType == ViewType.FloorPlan
                       || view.ViewType == ViewType.AreaPlan
                       || view.ViewType == ViewType.EngineeringPlan;

            // Get the view's phase for room lookups (plan views only)
            Phase viewPhase = null;
            if (isPlan)
            {
                var phaseParam = view.get_Parameter(BuiltInParameter.VIEW_PHASE);
                if (phaseParam != null)
                    viewPhase = doc.GetElement(phaseParam.AsElementId()) as Phase;
            }

            foreach (FamilyInstance window in windowCollector)
            {
                try
                {
                    if (skipTagged && alreadyTaggedIds.Contains(window.Id.Value))
                    {
                        result.Skipped++;
                        continue;
                    }

                    XYZ tagPoint = isPlan
                        ? CalculatePlanTagPosition(doc, window, viewPhase)
                        : CalculateElevSectionTagPosition(window, view);

                    if (tagPoint == null)
                    {
                        result.Errors.Add($"Could not calculate position for window {window.Id.Value} in '{view.Name}'.");
                        continue;
                    }

                    var windowRef = new Reference(window);
                    var newTag = IndependentTag.Create(
                        doc,
                        tagSymbol.Id,
                        view.Id,
                        windowRef,
                        false,                      // no leader
                        TagOrientation.Horizontal,
                        tagPoint
                    );

                    if (newTag != null) result.Tagged++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Window {window.Id.Value}: {ex.Message}");
                }
            }

            return result;
        }

        // ============================================================================
        // PLAN VIEW PLACEMENT
        // ============================================================================
        // Uses ray-cast room detection to determine exterior vs operable side.
        // ============================================================================
        private XYZ CalculatePlanTagPosition(Document doc, FamilyInstance window, Phase viewPhase)
        {
            LocationPoint locPt = window.Location as LocationPoint;
            if (locPt == null) return null;

            XYZ windowPoint = locPt.Point;
            XYZ facingDir = window.FacingOrientation;

            // Test points 850mm on each side of the window along FacingOrientation
            XYZ facingSideTestPoint = windowPoint + facingDir.Multiply(ROOM_TEST_DISTANCE_FEET);
            XYZ oppositeSideTestPoint = windowPoint - facingDir.Multiply(ROOM_TEST_DISTANCE_FEET);

            Room facingSideRoom = GetRoomAtPoint(doc, facingSideTestPoint, viewPhase);
            Room oppositeSideRoom = GetRoomAtPoint(doc, oppositeSideTestPoint, viewPhase);

            // Determine the tag-side direction (unit vector from window toward tag location)
            XYZ tagSideDir;

            if (facingSideRoom != null && oppositeSideRoom == null)
            {
                // Exterior wall — facing side has room (interior), opposite side has no room (exterior)
                // Tag goes on the EXTERIOR side = opposite of FacingOrientation
                tagSideDir = facingDir.Negate();
            }
            else if (facingSideRoom == null && oppositeSideRoom != null)
            {
                // Exterior wall — opposite side has room, facing side is exterior
                // Tag goes on the EXTERIOR side = FacingOrientation direction
                tagSideDir = facingDir;
            }
            else if (facingSideRoom != null && oppositeSideRoom != null)
            {
                // Interior wall — rooms on both sides
                // Tag goes on the OPERABLE side = FacingOrientation direction
                tagSideDir = facingDir;
            }
            else
            {
                // Edge case: no room on either side (e.g. unplaced rooms, exterior walls to nowhere)
                // Fallback: FacingOrientation side
                tagSideDir = facingDir;
            }

            // Add wall half-thickness so the offset is measured from the wall FACE
            Wall hostWall = window.Host as Wall;
            double wallHalfWidth = hostWall != null ? hostWall.Width / 2.0 : 0.0;

            double totalOffset = wallHalfWidth + PLAN_OFFSET_FEET;
            return windowPoint + tagSideDir.Multiply(totalOffset);
        }

        // ============================================================================
        // ELEVATION / SECTION PLACEMENT
        // ============================================================================
        private XYZ CalculateElevSectionTagPosition(FamilyInstance window, View view)
        {
            BoundingBoxXYZ bbox = window.get_BoundingBox(view);
            if (bbox == null) return null;

            return (bbox.Min + bbox.Max) * 0.5;
        }

        // ============================================================================
        // ROOM LOOKUP HELPER
        // ============================================================================
        // Wraps Document.GetRoomAtPoint with phase awareness and null safety.
        // ============================================================================
        private Room GetRoomAtPoint(Document doc, XYZ point, Phase phase)
        {
            try
            {
                if (phase != null)
                    return doc.GetRoomAtPoint(point, phase);
                return doc.GetRoomAtPoint(point);
            }
            catch
            {
                return null;
            }
        }
    }
}
