// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/WindowTagStrategy.cs
// ============================================================================
// Tags windows in Plan, Elevation, and Section views.
//
// PLAN (FloorPlan / AreaPlan / EngineeringPlan):
//   - Cast 850mm test points on each side along FacingOrientation
//   - GetRoomAtPoint() determines exterior vs interior wall
//   - Exterior wall: tag on the side WITHOUT a room
//   - Interior wall: tag on the OPERABLE side (FacingOrientation)
//   - 700mm offset from wall face (wall half-width + offset)
//   - No leader
//
// ELEVATION:
//   - Tag at window bounding-box center in the view
//   - Only tags windows on the FACING wall of the elevation
//   - No leader
//
// SECTION:
//   - ZONE 1 (cut plane ±300mm): Tag windows directly cut by the section
//   - ZONE 2 (far-clip zone): Tag windows on the facing back wall
//   - Skips windows on side walls and windows beyond the far clip
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

                    // For non-plan views: apply visibility filters
                    if (!isPlan)
                    {
                        LocationPoint lp = window.Location as LocationPoint;
                        if (lp != null)
                        {
                            // SECTION: two-zone visibility check
                            if (view.ViewType == ViewType.Section &&
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
