// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/WallTagStrategy.cs
// ============================================================================
// Tags walls in Floor Plan views only.
//
// FLOOR PLAN:
//   - Tag placed at the midpoint of the wall's visible center curve
//   - Leader ON (addLeader = true)
//   - Leader endpoint offset 700mm perpendicular from wall center
//   - Tag orientation: Horizontal
//
// Note: Wall tags are NOT supported in Elevations/Sections per firm standard.
// ============================================================================

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace A49AIRevitAssistant.Executor.Commands.TagStrategies
{
    public class WallTagStrategy : ITagStrategy
    {
        public string CategoryKey => "wall";
        public BuiltInCategory TargetCategory => BuiltInCategory.OST_Walls;
        public BuiltInCategory TagCategory => BuiltInCategory.OST_WallTags;

        // Offset from wall center for the tag head (mm → feet)
        private const double TAG_OFFSET_FEET = 700.0 / 304.8;

        public bool SupportsViewType(ViewType viewType)
        {
            // Floor Plan only per firm standard
            return viewType == ViewType.FloorPlan;
        }

        public TagResult TagElementsInView(
            Document doc,
            View view,
            FamilySymbol tagSymbol,
            bool skipTagged)
        {
            var result = new TagResult();

            var wallCollector = new FilteredElementCollector(doc, view.Id)
                .OfCategory(TargetCategory)
                .OfClass(typeof(Wall));

            // Pre-collect already-tagged wall IDs
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

            foreach (Wall wall in wallCollector)
            {
                try
                {
                    if (skipTagged && alreadyTaggedIds.Contains(wall.Id.Value))
                    {
                        result.Skipped++;
                        continue;
                    }

                    // Get wall's location curve
                    LocationCurve locCurve = wall.Location as LocationCurve;
                    if (locCurve == null) continue;

                    Curve curve = locCurve.Curve;
                    if (curve == null) continue;

                    // Midpoint of the wall's center curve (reference point for the tag)
                    XYZ wallMidpoint = curve.Evaluate(0.5, true);

                    // Calculate a perpendicular direction for the tag offset
                    // The wall's curve tangent gives us the along-wall direction;
                    // cross with Z to get the perpendicular (in plan XY)
                    XYZ tangent = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
                    XYZ perpDir = tangent.CrossProduct(XYZ.BasisZ).Normalize();

                    // Tag head position = midpoint + 700mm offset perpendicular to wall
                    XYZ tagHeadPoint = wallMidpoint + perpDir.Multiply(TAG_OFFSET_FEET);

                    var wallRef = new Reference(wall);
                    var newTag = IndependentTag.Create(
                        doc,
                        tagSymbol.Id,
                        view.Id,
                        wallRef,
                        true,                       // WITH leader
                        TagOrientation.Horizontal,
                        tagHeadPoint
                    );

                    if (newTag != null) result.Tagged++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Wall {wall.Id.Value}: {ex.Message}");
                }
            }

            return result;
        }
    }
}
