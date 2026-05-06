// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/WindowTagStrategy.cs
// ============================================================================
// Tags windows in Plan, Elevation, and Section views.
//
// PLAN (FloorPlan / AreaPlan / EngineeringPlan):
//   - Skips windows whose location is outside the view's crop region
//   - Cast 850mm test points on each side along FacingOrientation
//   - GetRoomAtPoint() determines exterior vs interior wall
//   - Exterior wall: tag on the side WITHOUT a room
//   - Interior wall: tag on the OPERABLE side (FacingOrientation)
//   - 700mm offset from wall face (wall half-width + offset)
//   - Tag head clamped inside crop region (200mm margin from edge)
//   - No leader
//
// ELEVATION:
//   - Only tags windows on the FACING wall of the elevation
//   - Tag at window bounding-box center in the view
//   - No leader
//
// SECTION:
//   - ZONE 1 (cut plane ±300mm): Tag windows directly cut by the section
//   - ZONE 2 (far-clip zone): Tag windows on the facing back wall only
//   - Skips windows on side walls and windows beyond the far clip
//   - Tag at window bounding-box center in the view
//   - No leader
//
// Revit 2021 adaptations vs canonical (revit-addin/):
//   - IndependentTag.GetTaggedLocalElementIds() → TaggedLocalElementId (single)
//   - Category.BuiltInCategory → (BuiltInCategory)Category.Id.IntegerValue
//   - ElementId.Value → ElementId.IntegerValue
//   - Linked pass is dead code (LinkedTagHelpers stubbed) — kept for parity.
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
        private const double ROOM_TEST_DISTANCE_FEET = 850.0 / 304.8;

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
                    if (skipTagged && alreadyTaggedIds.Contains(window.Id.IntegerValue))
                    {
                        result.Skipped++;
                        continue;
                    }

                    LocationPoint lp = window.Location as LocationPoint;

                    if (isPlan)
                    {
                        // Skip windows outside the view's crop region (scope box boundary)
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
                            !TagHelpers.IsElementVisibleInSection(view, window, lp.Point))
                        {
                            result.Skipped++;
                            continue;
                        }

                        // ELEVATION: only tag windows on the facing wall
                        if (view.ViewType == ViewType.Elevation &&
                            !TagHelpers.IsElementOnFacingWall(view, window))
                        {
                            result.Skipped++;
                            continue;
                        }
                    }

                    XYZ tagPoint = isPlan
                        ? CalculatePlanTagPosition(doc, window, viewPhase)
                        : CalculateElevSectionTagPosition(window, view);

                    if (tagPoint == null)
                    {
                        result.Errors.Add($"Could not calculate position for window {window.Id.IntegerValue} in '{view.Name}'.");
                        continue;
                    }

                    // Clamp plan tag head inside the crop region
                    if (isPlan)
                        tagPoint = TagHelpers.ClampTagPointToCropRegion(view, tagPoint, CROP_MARGIN_FEET);

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
                    result.Errors.Add($"Window {window.Id.IntegerValue}: {ex.Message}");
                }
            }

            // ====================================================================
            // LINKED PASS — REVIT 2021: stubbed via LinkedTagHelpers (yields nothing).
            // Loop body never executes. Kept for structural parity with revit-addin/.
            // ====================================================================
            foreach (var link in LinkedTagHelpers.EnumerateLinks(doc, view))
            {
                HashSet<long> linkedAlreadyTagged = skipTagged
                    ? LinkedTagHelpers.CollectAlreadyTaggedLinkedIds(doc, view, link.Instance.Id, TargetCategory)
                    : new HashSet<long>();

                FilteredElementCollector linkedWindows;
                try
                {
                    linkedWindows = new FilteredElementCollector(link.Document)
                        .OfCategory(TargetCategory)
                        .OfClass(typeof(FamilyInstance));
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Linked doc '{link.Document.Title}': {ex.Message}");
                    continue;
                }

                foreach (FamilyInstance window in linkedWindows)
                {
                    try
                    {
                        if (skipTagged && linkedAlreadyTagged.Contains(window.Id.IntegerValue))
                        {
                            result.Skipped++;
                            continue;
                        }

                        LocationPoint lp = window.Location as LocationPoint;
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
                                !TagHelpers.IsElementVisibleInSection(view, window, ptHost))
                            {
                                result.Skipped++;
                                continue;
                            }
                            if (view.ViewType == ViewType.Elevation &&
                                !IsLinkedElementOnFacingWall(view, window, link.Transform))
                            {
                                result.Skipped++;
                                continue;
                            }
                        }

                        XYZ tagPoint = isPlan
                            ? CalculatePlanTagPositionLinked(link.Document, window, link.Transform)
                            : CalculateElevSectionTagPositionLinked(window, link.Transform);

                        if (tagPoint == null)
                        {
                            result.Errors.Add($"Could not calculate position for linked window {window.Id.IntegerValue} in '{view.Name}'.");
                            continue;
                        }

                        if (isPlan)
                            tagPoint = TagHelpers.ClampTagPointToCropRegion(view, tagPoint, CROP_MARGIN_FEET);

                        Reference linkedRef = LinkedTagHelpers.BuildLinkedReference(window, link.Instance);
                        if (linkedRef == null)
                        {
                            // Revit 2021: BuildLinkedReference always returns null. Silent skip.
                            continue;
                        }

                        var newTag = IndependentTag.Create(
                            doc, tagSymbol.Id, view.Id,
                            linkedRef, false, TagOrientation.Horizontal, tagPoint);

                        if (newTag != null) result.Tagged++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Linked window {window.Id.IntegerValue}: {ex.Message}");
                    }
                }
            }

            return result;
        }

        // ============================================================================
        // LINKED-PASS PLACEMENT HELPERS (dead code in 2021 — see file header)
        // ============================================================================
        private XYZ CalculatePlanTagPositionLinked(Document linkDoc, FamilyInstance window, Transform xform)
        {
            LocationPoint locPt = window.Location as LocationPoint;
            if (locPt == null) return null;

            XYZ windowPointLink = locPt.Point;
            XYZ facingDirLink = window.FacingOrientation;

            XYZ facingTestLink = windowPointLink + facingDirLink.Multiply(ROOM_TEST_DISTANCE_FEET);
            XYZ oppositeTestLink = windowPointLink - facingDirLink.Multiply(ROOM_TEST_DISTANCE_FEET);
            Room facingRoom = GetRoomAtPointSafe(linkDoc, facingTestLink);
            Room oppositeRoom = GetRoomAtPointSafe(linkDoc, oppositeTestLink);

            XYZ tagSideDirLink;
            if (facingRoom != null && oppositeRoom == null)
                tagSideDirLink = facingDirLink.Negate();
            else if (facingRoom == null && oppositeRoom != null)
                tagSideDirLink = facingDirLink;
            else
                tagSideDirLink = facingDirLink;     // both-rooms (interior) OR no-rooms (fallback)

            Wall hostWall = window.Host as Wall;
            double wallHalfWidth = hostWall != null ? hostWall.Width / 2.0 : 0.0;

            XYZ tagPointLink = windowPointLink + tagSideDirLink.Multiply(wallHalfWidth + PLAN_OFFSET_FEET);
            return xform.OfPoint(tagPointLink);
        }

        private XYZ CalculateElevSectionTagPositionLinked(FamilyInstance window, Transform xform)
        {
            BoundingBoxXYZ bbox = window.get_BoundingBox(null);
            if (bbox == null) return null;
            XYZ centerLink = (bbox.Min + bbox.Max) * 0.5;
            return xform.OfPoint(centerLink);
        }

        private Room GetRoomAtPointSafe(Document d, XYZ pointInD)
        {
            try { return d.GetRoomAtPoint(pointInD); } catch { return null; }
        }

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
        private XYZ CalculatePlanTagPosition(Document doc, FamilyInstance window, Phase viewPhase)
        {
            LocationPoint locPt = window.Location as LocationPoint;
            if (locPt == null) return null;

            XYZ windowPoint = locPt.Point;
            XYZ facingDir = window.FacingOrientation;

            XYZ facingSideTestPoint = windowPoint + facingDir.Multiply(ROOM_TEST_DISTANCE_FEET);
            XYZ oppositeSideTestPoint = windowPoint - facingDir.Multiply(ROOM_TEST_DISTANCE_FEET);

            Room facingSideRoom = GetRoomAtPoint(doc, facingSideTestPoint, viewPhase);
            Room oppositeSideRoom = GetRoomAtPoint(doc, oppositeSideTestPoint, viewPhase);

            XYZ tagSideDir;

            if (facingSideRoom != null && oppositeSideRoom == null)
            {
                // Exterior wall: interior on facing side → tag on exterior (opposite)
                tagSideDir = facingDir.Negate();
            }
            else if (facingSideRoom == null && oppositeSideRoom != null)
            {
                // Exterior wall: interior on opposite side → tag on exterior (facing)
                tagSideDir = facingDir;
            }
            else if (facingSideRoom != null && oppositeSideRoom != null)
            {
                // Interior wall: tag on operable side (FacingOrientation)
                tagSideDir = facingDir;
            }
            else
            {
                // No rooms detected: fallback to FacingOrientation
                tagSideDir = facingDir;
            }

            Wall hostWall = window.Host as Wall;
            double wallHalfWidth = hostWall != null ? hostWall.Width / 2.0 : 0.0;

            return windowPoint + tagSideDir.Multiply(wallHalfWidth + PLAN_OFFSET_FEET);
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
