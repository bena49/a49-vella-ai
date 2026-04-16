// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/DoorTagStrategy.cs
// ============================================================================
// Tags doors in Plan, Elevation, and Section views.
//
// PLAN (FloorPlan / AreaPlan / EngineeringPlan) — from AutoTagDoorsCommand:
//   - Tag on non-swing side of door
//   - 350mm offset if wall is horizontal on page (parallel tag)
//   - 700mm offset if wall is vertical or angled (perpendicular tag)
//   - Offset measured from wall FACE (accounts for wall thickness)
//   - No leader
//
// ELEVATION / SECTION:
//   - Tag at door center point in view (bounding-box center)
//   - No leader
// ============================================================================

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace A49AIRevitAssistant.Executor.Commands.TagStrategies
{
    public class DoorTagStrategy : ITagStrategy
    {
        public string CategoryKey => "door";
        public BuiltInCategory TargetCategory => BuiltInCategory.OST_Doors;
        public BuiltInCategory TagCategory => BuiltInCategory.OST_DoorTags;

        // Offsets (mm → feet) used for Plan-view tagging
        private const double PARALLEL_OFFSET_FEET = 350.0 / 304.8;
        private const double PERPENDICULAR_OFFSET_FEET = 700.0 / 304.8;

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

            // Collect all doors visible in this view
            var doorCollector = new FilteredElementCollector(doc, view.Id)
                .OfCategory(TargetCategory)
                .OfClass(typeof(FamilyInstance));

            // Pre-collect already-tagged door IDs in this view
            HashSet<long> alreadyTaggedIds = new HashSet<long>();
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
                    catch { /* skip unreadable tags */ }
                }
            }

            bool isPlan = view.ViewType == ViewType.FloorPlan
                       || view.ViewType == ViewType.AreaPlan
                       || view.ViewType == ViewType.EngineeringPlan;

            foreach (FamilyInstance door in doorCollector)
            {
                try
                {
                    if (skipTagged && alreadyTaggedIds.Contains(door.Id.Value))
                    {
                        result.Skipped++;
                        continue;
                    }

                    // For non-plan views: apply section-cut and facing-wall filters
                    if (!isPlan)
                    {
                        LocationPoint lp = door.Location as LocationPoint;
                        if (lp != null)
                        {
                            // Section views: only tag doors cut by the section plane
                            if (view.ViewType == ViewType.Section &&
                                !TagHelpers.IsElementCutBySection(view, lp.Point))
                            {
                                result.Skipped++;
                                continue;
                            }

                            // Elevation views: only tag doors on the facing wall
                            if (view.ViewType == ViewType.Elevation &&
                                !TagHelpers.IsElementOnFacingWall(view, door))
                            {
                                result.Skipped++;
                                continue;
                            }
                        }
                    }

                    XYZ tagPoint = isPlan
                        ? CalculatePlanTagPosition(door)
                        : CalculateElevSectionTagPosition(door, view);

                    if (tagPoint == null)
                    {
                        result.Errors.Add($"Could not calculate position for door {door.Id.Value} in '{view.Name}'.");
                        continue;
                    }

                    var doorRef = new Reference(door);
                    var newTag = IndependentTag.Create(
                        doc,
                        tagSymbol.Id,
                        view.Id,
                        doorRef,
                        false,                      // no leader
                        TagOrientation.Horizontal,
                        tagPoint
                    );

                    if (newTag != null) result.Tagged++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Door {door.Id.Value}: {ex.Message}");
                }
            }

            return result;
        }

        // ============================================================================
        // PLAN VIEW PLACEMENT
        // ============================================================================
        // Same logic as AutoTagDoorsCommand (which is already tested and approved).
        // ============================================================================
        private XYZ CalculatePlanTagPosition(FamilyInstance door)
        {
            LocationPoint locPt = door.Location as LocationPoint;
            if (locPt == null) return null;

            XYZ doorPoint = locPt.Point;

            // FacingOrientation is a LIVE vector that points toward the correct
            // TAG side (non-swing side) in this firm's family setup.
            XYZ tagSideDir = door.FacingOrientation;

            Wall hostWall = door.Host as Wall;
            double wallHalfWidth = hostWall != null ? hostWall.Width / 2.0 : 0.0;

            double faceOffset = IsHorizontalWall(hostWall)
                ? PARALLEL_OFFSET_FEET
                : PERPENDICULAR_OFFSET_FEET;

            double totalOffset = wallHalfWidth + faceOffset;
            return doorPoint + tagSideDir.Multiply(totalOffset);
        }

        // ============================================================================
        // ELEVATION / SECTION PLACEMENT
        // ============================================================================
        // Tag at the center of the door's bounding box IN THE VIEW.
        // ============================================================================
        private XYZ CalculateElevSectionTagPosition(FamilyInstance door, View view)
        {
            BoundingBoxXYZ bbox = door.get_BoundingBox(view);
            if (bbox == null) return null;

            return (bbox.Min + bbox.Max) * 0.5;
        }

        // ============================================================================
        // WALL ORIENTATION DETECTION (same as AutoTagDoorsCommand)
        // ============================================================================
        private bool IsHorizontalWall(Wall wall)
        {
            if (wall == null) return false;

            LocationCurve locCurve = wall.Location as LocationCurve;
            if (locCurve == null) return false;

            Line line = locCurve.Curve as Line;
            if (line == null) return false;

            XYZ dir = line.Direction;
            double absX = Math.Abs(dir.X);
            double absY = Math.Abs(dir.Y);

            const double HORIZONTAL_TOLERANCE = 0.9; // cos(~25°)
            return absX >= HORIZONTAL_TOLERANCE && absX > absY;
        }
    }
}
