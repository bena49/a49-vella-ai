// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/DoorTagStrategy.cs
// ============================================================================
// Tags doors in Plan, Elevation, and Section views.
//
// PLAN (FloorPlan / AreaPlan / EngineeringPlan):
//   - Skips doors whose location is outside the view's crop region
//   - Tag on non-swing side of door (FacingOrientation)
//   - 350mm offset if wall is horizontal on page (parallel tag)
//   - 700mm offset if wall is vertical or angled (perpendicular tag)
//   - Offset measured from wall FACE (wall half-width + offset)
//   - Tag head clamped inside crop region (200mm margin from edge)
//   - No leader
//
// ELEVATION:
//   - Only tags doors on the FACING wall of the elevation
//   - Tag at door bounding-box center in the view
//   - No leader
//
// SECTION:
//   - ZONE 1 (cut plane ±300mm): Tag doors directly cut by the section
//   - ZONE 2 (far-clip zone): Tag doors on the facing back wall only
//   - Skips doors on side walls and doors beyond the far clip
//   - Tag at door bounding-box center in the view
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

        // Margin from crop edge when clamping tag head (mm → feet)
        private const double CROP_MARGIN_FEET = 200.0 / 304.8;

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

            var doorCollector = new FilteredElementCollector(doc, view.Id)
                .OfCategory(TargetCategory)
                .OfClass(typeof(FamilyInstance));

            // Pre-collect already-tagged door IDs in this view
            var alreadyTaggedIds = new HashSet<long>();
            if (skipTagged)
            {
                var tagCollector = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag));

                foreach (IndependentTag existingTag in tagCollector)
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
                       || view.ViewType == ViewType.AreaPlan
                       || view.ViewType == ViewType.EngineeringPlan;

            foreach (FamilyInstance door in doorCollector)
            {
                try
                {
                    if (skipTagged && alreadyTaggedIds.Contains(door.Id.IntegerValue))
                    {
                        result.Skipped++;
                        continue;
                    }

                    LocationPoint lp = door.Location as LocationPoint;

                    if (isPlan)
                    {
                        // Skip doors outside the view's crop region (scope box boundary)
                        if (lp != null && !TagHelpers.IsElementInCropRegion(view, lp.Point))
                        {
                            result.Skipped++;
                            continue;
                        }
                    }
                    else
                    {
                        // SECTION: two-zone visibility check
                        if (view.ViewType == ViewType.Section && lp != null &&
                            !TagHelpers.IsElementVisibleInSection(view, door, lp.Point))
                        {
                            result.Skipped++;
                            continue;
                        }

                        // ELEVATION: only tag doors on the facing wall
                        if (view.ViewType == ViewType.Elevation &&
                            !TagHelpers.IsElementOnFacingWall(view, door))
                        {
                            result.Skipped++;
                            continue;
                        }
                    }

                    XYZ tagPoint = isPlan
                        ? CalculatePlanTagPosition(door)
                        : CalculateElevSectionTagPosition(door, view);

                    if (tagPoint == null)
                    {
                        result.Errors.Add($"Could not calculate position for door {door.Id.IntegerValue} in '{view.Name}'.");
                        continue;
                    }

                    // Clamp plan tag head inside the crop region
                    if (isPlan)
                        tagPoint = TagHelpers.ClampTagPointToCropRegion(view, tagPoint, CROP_MARGIN_FEET);

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
                    result.Errors.Add($"Door {door.Id.IntegerValue}: {ex.Message}");
                }
            }

            return result;
        }

        // ============================================================================
        // PLAN VIEW PLACEMENT
        // ============================================================================
        private XYZ CalculatePlanTagPosition(FamilyInstance door)
        {
            LocationPoint locPt = door.Location as LocationPoint;
            if (locPt == null) return null;

            XYZ doorPoint = locPt.Point;
            XYZ tagSideDir = door.FacingOrientation;

            Wall hostWall = door.Host as Wall;
            double wallHalfWidth = hostWall != null ? hostWall.Width / 2.0 : 0.0;

            double faceOffset = IsHorizontalWall(hostWall)
                ? PARALLEL_OFFSET_FEET
                : PERPENDICULAR_OFFSET_FEET;

            return doorPoint + tagSideDir.Multiply(wallHalfWidth + faceOffset);
        }

        // ============================================================================
        // ELEVATION / SECTION PLACEMENT
        // ============================================================================
        private XYZ CalculateElevSectionTagPosition(FamilyInstance door, View view)
        {
            BoundingBoxXYZ bbox = door.get_BoundingBox(view);
            if (bbox == null) return null;
            return (bbox.Min + bbox.Max) * 0.5;
        }

        // ============================================================================
        // WALL ORIENTATION DETECTION
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
