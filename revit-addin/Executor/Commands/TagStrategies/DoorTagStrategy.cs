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

            foreach (FamilyInstance door in doorCollector)
            {
                try
                {
                    if (skipTagged && alreadyTaggedIds.Contains(door.Id.Value))
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
                        result.Errors.Add($"Could not calculate position for door {door.Id.Value} in '{view.Name}'.");
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
                    result.Errors.Add($"Door {door.Id.Value}: {ex.Message}");
                }
            }

            // ====================================================================
            // LINKED PASS — tag doors that live in linked Revit models.
            // Additive: the host pass above is unchanged. All coordinate math
            // uses link.Transform so the existing crop-region / facing-wall /
            // section-zone helpers work in host coordinates.
            // ====================================================================
            foreach (var link in LinkedTagHelpers.EnumerateLinks(doc, view))
            {
                HashSet<long> linkedAlreadyTagged = skipTagged
                    ? LinkedTagHelpers.CollectAlreadyTaggedLinkedIds(doc, view, link.Instance.Id, TargetCategory)
                    : new HashSet<long>();

                FilteredElementCollector linkedDoors;
                try
                {
                    linkedDoors = new FilteredElementCollector(link.Document)
                        .OfCategory(TargetCategory)
                        .OfClass(typeof(FamilyInstance));
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Linked doc '{link.Document.Title}': {ex.Message}");
                    continue;
                }

                foreach (FamilyInstance door in linkedDoors)
                {
                    try
                    {
                        if (skipTagged && linkedAlreadyTagged.Contains(door.Id.Value))
                        {
                            result.Skipped++;
                            continue;
                        }

                        LocationPoint lp = door.Location as LocationPoint;
                        XYZ ptHost = lp != null ? link.Transform.OfPoint(lp.Point) : null;

                        if (isPlan)
                        {
                            if (ptHost != null && !TagHelpers.IsElementInCropRegion(view, ptHost))
                            {
                                result.Skipped++;
                                continue;
                            }
                        }
                        else
                        {
                            if (ptHost == null) { result.Skipped++; continue; }

                            if (view.ViewType == ViewType.Section &&
                                !TagHelpers.IsElementVisibleInSection(view, door, ptHost))
                            {
                                // Note: IsElementVisibleInSection's facing-wall fallback
                                // reads door.Host in linkDoc coords — the dot-product
                                // result is invariant under the link transform when the
                                // link has no rotation about Z. For rotated links the
                                // result may misclassify edge cases; acceptable for v1.
                                result.Skipped++;
                                continue;
                            }
                            if (view.ViewType == ViewType.Elevation &&
                                !IsLinkedElementOnFacingWall(view, door, link.Transform))
                            {
                                result.Skipped++;
                                continue;
                            }
                        }

                        XYZ tagPoint = isPlan
                            ? CalculatePlanTagPositionLinked(door, link.Transform)
                            : CalculateElevSectionTagPositionLinked(door, link.Transform);

                        if (tagPoint == null)
                        {
                            result.Errors.Add($"Could not calculate position for linked door {door.Id.Value} in '{view.Name}'.");
                            continue;
                        }

                        if (isPlan)
                            tagPoint = TagHelpers.ClampTagPointToCropRegion(view, tagPoint, CROP_MARGIN_FEET);

                        Reference linkedRef = LinkedTagHelpers.BuildLinkedReference(door, link.Instance);
                        if (linkedRef == null)
                        {
                            result.Errors.Add($"Could not build link reference for door {door.Id.Value}.");
                            continue;
                        }

                        var newTag = IndependentTag.Create(
                            doc, tagSymbol.Id, view.Id,
                            linkedRef, false, TagOrientation.Horizontal, tagPoint);

                        if (newTag != null) result.Tagged++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Linked door {door.Id.Value}: {ex.Message}");
                    }
                }
            }

            return result;
        }

        // ============================================================================
        // LINKED-PASS PLACEMENT — mirror of the host helpers but with the link
        // transform applied to every coordinate / direction we read from the
        // linked document.
        // ============================================================================
        private XYZ CalculatePlanTagPositionLinked(FamilyInstance door, Transform xform)
        {
            LocationPoint locPt = door.Location as LocationPoint;
            if (locPt == null) return null;

            XYZ doorPointHost = xform.OfPoint(locPt.Point);
            XYZ tagSideDirHost = xform.OfVector(door.FacingOrientation);

            Wall hostWall = door.Host as Wall;
            double wallHalfWidth = hostWall != null ? hostWall.Width / 2.0 : 0.0;

            double faceOffset = IsHorizontalWallLinked(hostWall, xform)
                ? PARALLEL_OFFSET_FEET
                : PERPENDICULAR_OFFSET_FEET;

            return doorPointHost + tagSideDirHost.Multiply(wallHalfWidth + faceOffset);
        }

        private XYZ CalculateElevSectionTagPositionLinked(FamilyInstance door, Transform xform)
        {
            // Linked elements: the (view) overload is unsafe (view belongs to
            // host doc, element to link doc). Read the element's intrinsic bbox
            // in link coordinates and transform corners into host coords, then
            // average to a centroid.
            BoundingBoxXYZ bbox = door.get_BoundingBox(null);
            if (bbox == null) return null;
            XYZ centerLink = (bbox.Min + bbox.Max) * 0.5;
            return xform.OfPoint(centerLink);
        }

        private bool IsHorizontalWallLinked(Wall wall, Transform xform)
        {
            if (wall == null) return false;
            LocationCurve locCurve = wall.Location as LocationCurve;
            if (locCurve == null) return false;
            Line line = locCurve.Curve as Line;
            if (line == null) return false;

            // Direction is a vector — rotate it through the link transform so
            // we evaluate horizontality in *host* (= screen) coordinates.
            XYZ dirHost = xform.OfVector(line.Direction);
            double absX = Math.Abs(dirHost.X);
            double absY = Math.Abs(dirHost.Y);

            const double HORIZONTAL_TOLERANCE = 0.9; // cos(~25°)
            return absX >= HORIZONTAL_TOLERANCE && absX > absY;
        }

        // Linked-aware version of TagHelpers.IsElementOnFacingWall — the wall's
        // direction lives in linkDoc coords, so we transform it before comparing
        // against the host elevation's ViewDirection.
        private bool IsLinkedElementOnFacingWall(View elevView, FamilyInstance elem, Transform xform)
        {
            try
            {
                if (elevView.ViewType != ViewType.Elevation) return true;
                Wall hostWall = elem.Host as Wall;
                if (hostWall == null) return true;
                LocationCurve locCurve = hostWall.Location as LocationCurve;
                if (locCurve == null) return true;
                Line wallLine = locCurve.Curve as Line;
                if (wallLine == null) return true;

                XYZ wallDirHost = xform.OfVector(wallLine.Direction);
                XYZ wallNormalHost = new XYZ(-wallDirHost.Y, wallDirHost.X, 0).Normalize();
                XYZ viewDir = elevView.ViewDirection;

                double dot = Math.Abs(wallNormalHost.DotProduct(viewDir));
                return dot > 0.7;
            }
            catch
            {
                return true;
            }
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
