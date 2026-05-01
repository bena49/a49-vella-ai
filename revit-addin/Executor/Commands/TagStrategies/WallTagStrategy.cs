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
// SCOPE BOX / CROP REGION:
//   - Skips wall segments whose midpoint anchor falls outside the crop region
//   - Clamps tag head inside the crop region (200mm margin from edge)
//
// TAG PLACEMENT:
//   - Leader ON (addLeader = true)
//   - 350mm offset perpendicular from wall center (with explicit TagHeadPosition)
//   - Tag orientation: Horizontal
//   - All wall segments get tagged, including those outside any room
//
// Note: Wall tags are NOT supported in Elevations/Sections per firm standard.
// ============================================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System;
using System.Collections.Generic;
using System.Linq;

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

        // Margin from crop edge when clamping tag head (mm → feet)
        private const double CROP_MARGIN_FEET = 200.0 / 304.8;

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

                    // Perpendicular direction for tag offset (same for all segments)
                    XYZ tangent = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
                    XYZ perpDir = tangent.CrossProduct(XYZ.BasisZ).Normalize();

                    // Openings on this wall (normalized parameters 0→1)
                    List<double> openings = wallOpenings.ContainsKey(wall.Id.Value)
                        ? wallOpenings[wall.Id.Value]
                        : new List<double>();

                    // Segment the wall by room boundaries
                    var segments = SegmentWallByRooms(doc, curve, wallLength, viewPhase);

                    foreach (var segment in segments)
                    {
                        // REVISION: Get a list of all valid tag positions in this segment
                        var tagParameters = FindAllTagsInSegment(segment.StartT, segment.EndT, wallLength, openings);

                        foreach (double tagT in tagParameters)
                        {
                            XYZ tagAnchor = curve.Evaluate(tagT, true);

                            if (!TagHelpers.IsElementInCropRegion(view, tagAnchor)) continue;

                            XYZ tagHeadPoint = tagAnchor + perpDir.Multiply(TAG_OFFSET_FEET);
                            tagHeadPoint = TagHelpers.ClampTagPointToCropRegion(view, tagHeadPoint, CROP_MARGIN_FEET);

                            var newTag = IndependentTag.Create(doc, tagSymbol.Id, view.Id, new Reference(wall), true, TagOrientation.Horizontal, tagHeadPoint);

                            if (newTag != null)
                            {
                                newTag.LeaderEndCondition = LeaderEndCondition.Free;
                                newTag.TagHeadPosition = tagHeadPoint;
                                result.Tagged++;
                            }
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
            public long RoomId { get; set; }  // -1 = no room (exterior/unenclosed)
        }

        // ============================================================================
        // SEGMENT WALL BY ROOMS
        // ============================================================================
        private List<WallSegment> SegmentWallByRooms(
            Document doc, Curve curve, double wallLength, Phase phase)
        {
            var segments = new List<WallSegment>();

            int sampleCount = Math.Max(3, (int)(wallLength / SAMPLE_INTERVAL_FEET) + 1);
            sampleCount = Math.Min(sampleCount, 100);

            var samples = new List<(double t, long roomId)>();
            for (int i = 0; i < sampleCount; i++)
            {
                double t = (double)i / (sampleCount - 1);
                XYZ point = curve.Evaluate(t, true);

                long roomId = -1;
                Room room = GetRoomNearWallPoint(doc, curve, point, phase);
                if (room != null) roomId = room.Id.Value;

                samples.Add((t, roomId));
            }

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

            segments.Add(new WallSegment { StartT = segStart, EndT = 1.0, RoomId = currentRoom });

            // Filter out tiny segments (less than 5% of wall length)
            segments = segments.Where(s => (s.EndT - s.StartT) > 0.05).ToList();

            if (segments.Count == 0)
                segments.Add(new WallSegment { StartT = 0, EndT = 1, RoomId = -1 });

            return segments;
        }

        // ============================================================================
        // GET ROOM NEAR WALL POINT
        // ============================================================================
        private Room GetRoomNearWallPoint(Document doc, Curve wallCurve, XYZ point, Phase phase)
        {
            try
            {
                XYZ tangent = wallCurve.ComputeDerivatives(0.5, true).BasisX.Normalize();
                XYZ perpDir = tangent.CrossProduct(XYZ.BasisZ).Normalize();

                double testDist = 500.0 / 304.8;
                XYZ testA = point + perpDir.Multiply(testDist);
                XYZ testB = point - perpDir.Multiply(testDist);

                Room roomA = null;
                try
                {
                    roomA = phase != null
                        ? doc.GetRoomAtPoint(testA, phase)
                        : doc.GetRoomAtPoint(testA);
                }
                catch { }

                if (roomA != null) return roomA;

                Room roomB = null;
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
        // FIND ALL TAGS IN SEGMENT
        // ============================================================================
        private List<double> FindAllTagsInSegment(
            double startT, double endT, double wallLength,
            List<double> allOpenings)
        {
            var tagPositions = new List<double>();

            // Convert mm buffers to normalized wall fractions
            double cornerBuffer = 600.0 / 304.8; // 600mm from wall ends/corners
            double openingBuffer = 800.0 / 304.8; // 800mm from door/window edges

            double cornerFraction = cornerBuffer / wallLength;
            double openingFraction = openingBuffer / wallLength;

            // 1. Get openings within this room segment
            var segOpenings = allOpenings
                .Where(t => t > startT && t < endT)
                .OrderBy(t => t)
                .ToList();

            // 2. Define boundaries
            var boundaries = new List<double> { startT };
            boundaries.AddRange(segOpenings);
            boundaries.Add(endT);

            for (int i = 0; i < boundaries.Count - 1; i++)
            {
                double gapStart = boundaries[i];
                double gapEnd = boundaries[i + 1];

                // --- SMART BUFFERING ---
                // If at the very start/end of the wall (Corner), use cornerBuffer
                // If next to a door/window opening, use openingBuffer
                double bufferStart = (i == 0) ? cornerFraction : openingFraction;
                double bufferEnd = (i == boundaries.Count - 2) ? cornerFraction : openingFraction;

                double safeStart = gapStart + bufferStart;
                double safeEnd = gapEnd - bufferEnd;

                // 3. Only tag if the "Safe Zone" exists and is large enough
                if (safeEnd > safeStart)
                {
                    // Place tag in the middle of the safe zone, away from the door/corner
                    double tagT = (safeStart + safeEnd) / 2.0;
                    tagPositions.Add(tagT);
                }
            }

            return tagPositions;
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
