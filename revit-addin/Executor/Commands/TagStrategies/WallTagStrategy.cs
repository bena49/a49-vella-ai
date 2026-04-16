// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/WallTagStrategy.cs
// ============================================================================
// Tags walls in Floor Plan views only.
//
// FLOOR PLAN:
//   - Default: tag at wall midpoint, 700mm offset perpendicular, WITH leader
//   - SMART SHIFT: If a door or window is near the midpoint, the tag shifts
//     along the wall to the center of the largest clear gap between openings.
//     This avoids tags overlapping with door/window symbols.
//
// Note: Wall tags are NOT supported in Elevations/Sections per firm standard.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
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

        // Minimum clearance from an opening center to be considered "safe" (mm → feet)
        // A typical door is ~900mm wide, so 600mm from its center covers its extents
        private const double OPENING_CLEARANCE_FEET = 600.0 / 304.8;

        public bool SupportsViewType(ViewType viewType)
        {
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

            // Build a lookup: wall ID → list of opening positions (as parameter t on wall curve)
            // This maps each wall to the normalized positions of its hosted doors/windows
            var wallOpenings = new Dictionary<long, List<double>>();

            CollectOpeningsOnWalls(doc, view, BuiltInCategory.OST_Doors, wallOpenings);
            CollectOpeningsOnWalls(doc, view, BuiltInCategory.OST_Windows, wallOpenings);

            foreach (Wall wall in wallCollector)
            {
                try
                {
                    if (skipTagged && alreadyTaggedIds.Contains(wall.Id.Value))
                    {
                        result.Skipped++;
                        continue;
                    }

                    LocationCurve locCurve = wall.Location as LocationCurve;
                    if (locCurve == null) continue;

                    Curve curve = locCurve.Curve;
                    if (curve == null) continue;

                    double wallLength = curve.Length;
                    if (wallLength < 0.01) continue; // Skip tiny walls

                    // Find the best tag position along the wall (parameter t: 0→1)
                    double bestT = FindBestTagParameter(wall.Id.Value, wallLength, wallOpenings);

                    // Evaluate the point on the wall curve
                    XYZ tagAnchor = curve.Evaluate(bestT, true);

                    // Perpendicular direction for leader offset
                    XYZ tangent = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
                    XYZ perpDir = tangent.CrossProduct(XYZ.BasisZ).Normalize();

                    // Tag head = anchor point + 700mm perpendicular offset
                    XYZ tagHeadPoint = tagAnchor + perpDir.Multiply(TAG_OFFSET_FEET);

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

        // ============================================================================
        // COLLECT OPENINGS ON WALLS
        // ============================================================================
        // For each door/window visible in the view, projects its location onto
        // its host wall's curve and stores the normalized parameter (0→1).
        // ============================================================================
        private void CollectOpeningsOnWalls(
            Document doc, View view,
            BuiltInCategory openingCategory,
            Dictionary<long, List<double>> wallOpenings)
        {
            var collector = new FilteredElementCollector(doc, view.Id)
                .OfCategory(openingCategory)
                .OfClass(typeof(FamilyInstance));

            foreach (FamilyInstance fi in collector)
            {
                Wall hostWall = fi.Host as Wall;
                if (hostWall == null) continue;

                LocationPoint fiLoc = fi.Location as LocationPoint;
                if (fiLoc == null) continue;

                LocationCurve wallLocCurve = hostWall.Location as LocationCurve;
                if (wallLocCurve == null) continue;

                Curve wallCurve = wallLocCurve.Curve;
                if (wallCurve == null) continue;

                // Project the opening's location onto the wall curve
                IntersectionResult projection = wallCurve.Project(fiLoc.Point);
                if (projection == null) continue;

                // Normalize to 0→1 parameter
                double rawParam = projection.Parameter;
                double startParam = wallCurve.GetEndParameter(0);
                double endParam = wallCurve.GetEndParameter(1);
                double paramRange = endParam - startParam;
                double normalizedT = paramRange > 0 ? (rawParam - startParam) / paramRange : 0.5;

                long wallId = hostWall.Id.Value;
                if (!wallOpenings.ContainsKey(wallId))
                    wallOpenings[wallId] = new List<double>();
                wallOpenings[wallId].Add(normalizedT);
            }
        }

        // ============================================================================
        // FIND BEST TAG PARAMETER
        // ============================================================================
        // Given a wall and its opening positions, returns the best normalized
        // parameter (0→1) for the tag placement.
        //
        // Strategy:
        //   1. If no openings → use 0.5 (midpoint)
        //   2. If midpoint is clear of all openings → use 0.5
        //   3. Otherwise → find the largest gap between openings (including
        //      wall endpoints 0 and 1) and place the tag at the gap center.
        //      Clamp to [0.05, 0.95] to keep the tag off the very end of the wall.
        // ============================================================================
        private double FindBestTagParameter(long wallId, double wallLength, Dictionary<long, List<double>> wallOpenings)
        {
            // No openings on this wall → midpoint
            if (!wallOpenings.ContainsKey(wallId) || wallOpenings[wallId].Count == 0)
                return 0.5;

            var openingParams = wallOpenings[wallId].OrderBy(t => t).ToList();

            // Clearance zone expressed as a fraction of the wall length
            double clearanceFraction = OPENING_CLEARANCE_FEET / wallLength;

            // Check if the midpoint (0.5) is clear
            bool midpointClear = openingParams.All(t => Math.Abs(t - 0.5) > clearanceFraction);
            if (midpointClear)
                return 0.5;

            // Find the largest gap between openings (include endpoints 0 and 1)
            var boundaries = new List<double> { 0.0 };
            boundaries.AddRange(openingParams);
            boundaries.Add(1.0);

            double bestGapCenter = 0.5;
            double bestGapSize = 0.0;

            for (int i = 0; i < boundaries.Count - 1; i++)
            {
                double gapStart = boundaries[i];
                double gapEnd = boundaries[i + 1];

                // Shrink the gap by the clearance zone on each side
                double safeStart = (i == 0) ? gapStart : gapStart + clearanceFraction;
                double safeEnd = (i == boundaries.Count - 2) ? gapEnd : gapEnd - clearanceFraction;

                double safeSize = safeEnd - safeStart;
                if (safeSize > bestGapSize)
                {
                    bestGapSize = safeSize;
                    bestGapCenter = (safeStart + safeEnd) / 2.0;
                }
            }

            // Clamp to [0.05, 0.95] to keep tag off wall ends
            return Math.Max(0.05, Math.Min(0.95, bestGapCenter));
        }
    }
}
