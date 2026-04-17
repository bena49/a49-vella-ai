// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/WallTagStrategy.cs
// ============================================================================
// Tags walls in Floor Plan views only.
//
// SMART SEGMENTATION:
//   A single wall can span multiple rooms. This strategy:
//   1. Samples points along the wall at regular intervals
//   2. Uses GetRoomAtPoint() to detect which room each sample is in
//   3. Groups consecutive samples by room → wall "segments"
//   4. Places ONE tag per segment (= one tag per room the wall passes through)
//   5. Within each segment, shifts the tag away from any doors/windows
//
// TAG PLACEMENT:
//   - Leader ON (addLeader = true)
//   - 700mm offset perpendicular from wall center
//   - Tag orientation: Horizontal
//   - All wall segments get tagged, including those outside any room
//
// Note: Wall tags are NOT supported in Elevations/Sections per firm standard.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace A49AIRevitAssistant.Executor.Commands.TagStrategies
{
    public class WallTagStrategy : ITagStrategy
    {
        public string CategoryKey => "wall";
        public BuiltInCategory TargetCategory => BuiltInCategory.OST_Walls;
        public BuiltInCategory TagCategory => BuiltInCategory.OST_WallTags;

        // Offset from wall center for the tag head (mm → feet)
        private const double TAG_OFFSET_FEET = 700.0 / 304.8;

        // Sampling interval along the wall for room detection (mm → feet)
        private const double SAMPLE_INTERVAL_FEET = 300.0 / 304.8;

        // Minimum clearance from an opening center (mm → feet)
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
            // NOTE: A wall can have MULTIPLE tags (one per segment/room).
            // For "skip tagged," we track how many tags each wall already has.
            // We only fully skip a wall if skipTagged is on AND we don't do
            // segment-level skip logic. For simplicity: if the wall has ANY
            // existing tag, we skip it entirely when skipTagged is true.
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

            // Collect all door/window opening positions per wall
            var wallOpenings = new Dictionary<long, List<double>>();
            CollectOpeningsOnWalls(doc, view, BuiltInCategory.OST_Doors, wallOpenings);
            CollectOpeningsOnWalls(doc, view, BuiltInCategory.OST_Windows, wallOpenings);

            // Get the view's phase for room lookups
            Phase viewPhase = null;
            var phaseParam = view.get_Parameter(BuiltInParameter.VIEW_PHASE);
            if (phaseParam != null)
                viewPhase = doc.GetElement(phaseParam.AsElementId()) as Phase;

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
                    if (wallLength < 0.01) continue;

                    // Get perpendicular direction for tag offset (same for all segments)
                    XYZ tangent = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
                    XYZ perpDir = tangent.CrossProduct(XYZ.BasisZ).Normalize();

                    // Get openings on this wall (normalized parameters)
                    List<double> openings = wallOpenings.ContainsKey(wall.Id.Value)
                        ? wallOpenings[wall.Id.Value]
                        : new List<double>();

                    // Segment the wall by room boundaries
                    var segments = SegmentWallByRooms(doc, curve, wallLength, viewPhase);

                    // Tag each segment
                    foreach (var segment in segments)
                    {
                        double tagT = FindBestTagInSegment(
                            segment.StartT, segment.EndT,
                            wallLength, openings);

                        XYZ tagAnchor = curve.Evaluate(tagT, true);
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

                        if (newTag != null)
                        {
                            // Force the tag head to the calculated position
                            // (Revit's leader logic can override the initial placement)
                            try { newTag.TagHeadPosition = tagHeadPoint; } catch { }
                            result.Tagged++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Wall {wall.Id.Value}: {ex.Message}");
                }
            }

            return result;
        }

        // ============================================================================
        // WALL SEGMENT (simple data class)
        // ============================================================================
        private class WallSegment
        {
            public double StartT { get; set; }  // Normalized parameter (0→1)
            public double EndT { get; set; }
            public long RoomId { get; set; }     // -1 = no room (exterior/unenclosed)
        }

        // ============================================================================
        // SEGMENT WALL BY ROOMS
        // ============================================================================
        // Samples points along the wall at regular intervals, checks which room
        // each point is in, and groups consecutive same-room samples into segments.
        //
        // Returns at least one segment (the whole wall if no rooms detected).
        // Segments outside any room get RoomId = -1 and are still tagged.
        // ============================================================================
        private List<WallSegment> SegmentWallByRooms(
            Document doc, Curve curve, double wallLength, Phase phase)
        {
            var segments = new List<WallSegment>();

            // Calculate number of samples (minimum 3 — start, middle, end)
            int sampleCount = Math.Max(3, (int)(wallLength / SAMPLE_INTERVAL_FEET) + 1);
            sampleCount = Math.Min(sampleCount, 100); // Cap to avoid excessive sampling

            // Sample the wall and detect rooms
            var samples = new List<(double t, long roomId)>();
            for (int i = 0; i < sampleCount; i++)
            {
                double t = (double)i / (sampleCount - 1);
                XYZ point = curve.Evaluate(t, true);

                // Offset the test point slightly perpendicular to avoid hitting the wall itself
                // (rooms are on one or both sides of the wall, not inside it)
                long roomId = -1;
                Room room = GetRoomNearWallPoint(doc, curve, point, phase);
                if (room != null) roomId = room.Id.Value;

                samples.Add((t, roomId));
            }

            // Group consecutive samples by room
            if (samples.Count == 0)
            {
                segments.Add(new WallSegment { StartT = 0, EndT = 1, RoomId = -1 });
                return segments;
            }

            double segStart = samples[0].t;
            long currentRoom = samples[0].roomId;

            for (int i = 1; i < samples.Count; i++)
            {
                if (samples[i].roomId != currentRoom)
                {
                    // Room boundary detected — close current segment
                    double boundary = (samples[i - 1].t + samples[i].t) / 2.0;
                    segments.Add(new WallSegment
                    {
                        StartT = segStart,
                        EndT = boundary,
                        RoomId = currentRoom
                    });
                    segStart = boundary;
                    currentRoom = samples[i].roomId;
                }
            }

            // Close the last segment
            segments.Add(new WallSegment
            {
                StartT = segStart,
                EndT = 1.0,
                RoomId = currentRoom
            });

            // Filter out very tiny segments (less than 5% of wall length)
            segments = segments.Where(s => (s.EndT - s.StartT) > 0.05).ToList();

            // If filtering removed everything, return the whole wall as one segment
            if (segments.Count == 0)
                segments.Add(new WallSegment { StartT = 0, EndT = 1, RoomId = -1 });

            return segments;
        }

        // ============================================================================
        // GET ROOM NEAR WALL POINT
        // ============================================================================
        // Checks both sides of the wall at the given point (perpendicular offset)
        // since the room is adjacent to the wall, not at the wall centerline.
        // Returns the first room found on either side.
        // ============================================================================
        private Room GetRoomNearWallPoint(Document doc, Curve wallCurve, XYZ point, Phase phase)
        {
            try
            {
                XYZ tangent = wallCurve.ComputeDerivatives(0.5, true).BasisX.Normalize();
                XYZ perpDir = tangent.CrossProduct(XYZ.BasisZ).Normalize();

                // Test 500mm on each side of the wall center
                double testDist = 500.0 / 304.8;

                XYZ testA = point + perpDir.Multiply(testDist);
                XYZ testB = point - perpDir.Multiply(testDist);

                Room roomA = null;
                Room roomB = null;

                try
                {
                    roomA = phase != null
                        ? doc.GetRoomAtPoint(testA, phase)
                        : doc.GetRoomAtPoint(testA);
                }
                catch { }

                if (roomA != null) return roomA;

                try
                {
                    roomB = phase != null
                        ? doc.GetRoomAtPoint(testB, phase)
                        : doc.GetRoomAtPoint(testB);
                }
                catch { }

                return roomB;
            }
            catch
            {
                return null;
            }
        }

        // ============================================================================
        // FIND BEST TAG IN SEGMENT
        // ============================================================================
        // Within a wall segment [startT, endT], finds the best normalized parameter
        // for tag placement, avoiding any openings that fall within this segment.
        // ============================================================================
        private double FindBestTagInSegment(
            double startT, double endT, double wallLength,
            List<double> allOpenings)
        {
            double segMid = (startT + endT) / 2.0;

            // Filter openings to only those within this segment
            var segOpenings = allOpenings
                .Where(t => t >= startT && t <= endT)
                .OrderBy(t => t)
                .ToList();

            if (segOpenings.Count == 0)
                return segMid; // No openings in this segment — use segment center

            // Clearance zone as fraction of total wall length
            double clearanceFraction = OPENING_CLEARANCE_FEET / wallLength;

            // Check if segment midpoint is clear
            bool midpointClear = segOpenings.All(t => Math.Abs(t - segMid) > clearanceFraction);
            if (midpointClear)
                return segMid;

            // Find the largest gap within the segment
            var boundaries = new List<double> { startT };
            boundaries.AddRange(segOpenings);
            boundaries.Add(endT);

            double bestGapCenter = segMid;
            double bestGapSize = 0.0;

            for (int i = 0; i < boundaries.Count - 1; i++)
            {
                double gapStart = boundaries[i];
                double gapEnd = boundaries[i + 1];

                double safeStart = (i == 0) ? gapStart : gapStart + clearanceFraction;
                double safeEnd = (i == boundaries.Count - 2) ? gapEnd : gapEnd - clearanceFraction;

                double safeSize = safeEnd - safeStart;
                if (safeSize > bestGapSize)
                {
                    bestGapSize = safeSize;
                    bestGapCenter = (safeStart + safeEnd) / 2.0;
                }
            }

            // Clamp within segment bounds (with small margin from edges)
            double margin = 0.02;
            return Math.Max(startT + margin, Math.Min(endT - margin, bestGapCenter));
        }

        // ============================================================================
        // COLLECT OPENINGS ON WALLS
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

                IntersectionResult projection = wallCurve.Project(fiLoc.Point);
                if (projection == null) continue;

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
    }
}
