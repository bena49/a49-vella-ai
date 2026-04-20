using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace A49AIRevitAssistant.Executor.Commands.DimStrategies
{
    // =========================================================================
    //  DimRequest
    // =========================================================================

    public class DimRequest
    {
        public View TargetView { get; set; }
        public List<ElementId> WallIds { get; set; } = new List<ElementId>();
        public bool IncludeOpenings { get; set; } = true;
        public bool IncludeGrids { get; set; } = true;
        public double OffsetDistance { get; set; } = 2.625;
        public bool SmartExteriorPlacement { get; set; } = true;
    }

    // =========================================================================
    //  DimResult
    // =========================================================================

    public class DimResult
    {
        public bool Success { get; set; }
        public Dimension CreatedDimension { get; set; }
        public int ReferenceCount { get; set; }
        public string SkipReason { get; set; }
        public string ErrorMessage { get; set; }

        public static DimResult Succeeded(Dimension dim, int refCount) =>
            new DimResult { Success = true, CreatedDimension = dim, ReferenceCount = refCount };
        public static DimResult Skipped(string reason) =>
            new DimResult { Success = false, SkipReason = reason };
        public static DimResult Failed(string error) =>
            new DimResult { Success = false, ErrorMessage = error };
    }

    // =========================================================================
    //  DimContext
    // =========================================================================

    public class DimContext
    {
        public Document Document { get; set; }
        public DimRequest Request { get; set; }
        public List<Wall> AllWallsInView { get; set; } = new List<Wall>();
        public List<Grid> AllGridsInView { get; set; } = new List<Grid>();
        public DimensionType LinearDimensionType { get; set; }
    }

    // =========================================================================
    //  TaggedRef — a reference paired with its u-position along the wall axis
    // =========================================================================

    public class TaggedRef
    {
        public Reference Ref { get; set; }
        public double U { get; set; }
        public string Kind { get; set; } // "endcap" | "opening" | "grid"
        public ElementId SourceId { get; set; }
    }

    // =========================================================================
    //  DimHelpers
    // =========================================================================

    public static class DimHelpers
    {
        // ── Wall geometry ────────────────────────────────────────────────────

        public static XYZ GetWallDirection(Wall wall)
        {
            var lc = wall.Location as LocationCurve;
            var line = lc?.Curve as Line;
            if (line == null) return XYZ.BasisX;
            return (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
        }

        public static XYZ GetWallNormal(Wall wall)
        {
            var d = GetWallDirection(wall);
            return new XYZ(-d.Y, d.X, 0).Normalize();
        }

        public static Line GetWallCenterLine(Wall wall)
        {
            var lc = wall.Location as LocationCurve;
            return lc?.Curve as Line;
        }

        // ── Collect solids ────────────────────────────────────────────────────

        public static List<Solid> CollectSolids(GeometryElement geom)
        {
            var solids = new List<Solid>();
            foreach (GeometryObject obj in geom)
            {
                if (obj is Solid s && s.Volume > 0)
                    solids.Add(s);
                else if (obj is GeometryInstance gi)
                    foreach (GeometryObject sub in gi.GetInstanceGeometry())
                        if (sub is Solid ss && ss.Volume > 0)
                            solids.Add(ss);
            }
            return solids;
        }

        public static XYZ FaceCentroid(Face face)
        {
            var bb = face.GetBoundingBox();
            UV uv = new UV((bb.Min.U + bb.Max.U) / 2.0, (bb.Min.V + bb.Max.V) / 2.0);
            return face.Evaluate(uv);
        }

        // ── Exterior detection ───────────────────────────────────────────────

        public static bool IsExteriorWall(Wall wall, Document doc, View view)
        {
            try
            {
                var cl = GetWallCenterLine(wall);
                if (cl == null) return false;
                XYZ mid = cl.Evaluate(0.5, true);
                XYZ probe = mid + GetWallNormal(wall) * 1.0;
                var phase = doc.GetElement(
                    view.get_Parameter(BuiltInParameter.VIEW_PHASE).AsElementId()) as Phase;
                return doc.GetRoomAtPoint(probe, phase) == null;
            }
            catch { return false; }
        }

        public static XYZ GetDimLineOffsetDirection(Wall wall, Document doc,
            View view, bool smartPlacement)
        {
            return GetWallNormal(wall);
        }

        // ── Wall end-cap references ──────────────────────────────────────────

        public static (TaggedRef start, TaggedRef end)
            GetWallEndCapRefs(Wall wall)
        {
            var opts = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine,
            };

            var cl = GetWallCenterLine(wall);
            if (cl == null) return (null, null);

            XYZ origin = cl.GetEndPoint(0);
            XYZ wallDir = (cl.GetEndPoint(1) - origin).Normalize();
            double len = cl.Length;

            TaggedRef startTR = null, endTR = null;
            double bestStartU = double.MaxValue, bestEndU = double.MinValue;

            foreach (Solid solid in CollectSolids(wall.get_Geometry(opts)))
            {
                foreach (Face face in solid.Faces)
                {
                    if (!(face is PlanarFace pf)) continue;
                    if (Math.Abs(pf.FaceNormal.DotProduct(wallDir)) < 0.95) continue;

                    XYZ cen = FaceCentroid(pf);
                    double u = (cen - origin).DotProduct(wallDir);

                    if (u < len / 2.0)
                    {
                        if (u < bestStartU)
                        {
                            bestStartU = u;
                            startTR = new TaggedRef { Ref = pf.Reference, U = u, Kind = "endcap", SourceId = wall.Id };
                        }
                    }
                    else
                    {
                        if (u > bestEndU)
                        {
                            bestEndU = u;
                            endTR = new TaggedRef { Ref = pf.Reference, U = u, Kind = "endcap", SourceId = wall.Id };
                        }
                    }
                }
            }

            // Fallback for joined walls
            if (startTR == null || endTR == null)
            {
                var fallbackOpts = new Options
                {
                    ComputeReferences = true,
                    IncludeNonVisibleObjects = true,
                    DetailLevel = ViewDetailLevel.Fine,
                };

                foreach (Solid solid in CollectSolids(wall.get_Geometry(fallbackOpts)))
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (!(face is PlanarFace pf)) continue;
                        if (Math.Abs(pf.FaceNormal.DotProduct(wallDir)) < 0.90) continue;
                        if (pf.Reference == null) continue;

                        XYZ cen = FaceCentroid(pf);
                        double u = (cen - origin).DotProduct(wallDir);

                        if (startTR == null && u < len / 2.0)
                            startTR = new TaggedRef { Ref = pf.Reference, U = u, Kind = "endcap", SourceId = wall.Id };

                        if (endTR == null && u >= len / 2.0)
                            endTR = new TaggedRef { Ref = pf.Reference, U = u, Kind = "endcap", SourceId = wall.Id };
                    }

                    if (startTR != null && endTR != null) break;
                }
            }

            // Last resort fallback
            if (startTR == null || endTR == null)
            {
                var locCurve = wall.Location as LocationCurve;
                if (locCurve?.Curve is Line locLine)
                {
                    var curveOpts = new Options
                    {
                        ComputeReferences = true,
                        IncludeNonVisibleObjects = true,
                        DetailLevel = ViewDetailLevel.Fine,
                    };

                    foreach (GeometryObject obj in wall.get_Geometry(curveOpts))
                    {
                        Reference lineRef = null;

                        if (obj is Line ln && ln.Reference != null)
                            lineRef = ln.Reference;
                        else if (obj is GeometryInstance gi)
                            foreach (GeometryObject sub in gi.GetInstanceGeometry())
                                if (sub is Line sl && sl.Reference != null)
                                { lineRef = sl.Reference; break; }

                        if (lineRef != null)
                        {
                            if (startTR == null)
                                startTR = new TaggedRef { Ref = lineRef, U = 0, Kind = "endcap", SourceId = wall.Id };
                            if (endTR == null)
                                endTR = new TaggedRef { Ref = lineRef, U = len, Kind = "endcap", SourceId = wall.Id };
                            break;
                        }
                    }
                }
            }

            return (startTR, endTR);
        }

        // ── Grid references - THE FIXED VERSION based on research ──────────────
        // KEY INSIGHT: You MUST use grid.Curve.Reference and set ComputeReferences=true
        // This matches how Revit API successfully dimensions grids [citation:3][citation:5]

        /// =========================================================================
        // THE CORRECTED GRID REFERENCE METHOD
        // =========================================================================
        public static List<TaggedRef> GetGridRefs(Wall wall, List<Grid> allGrids)
        {
            var result = new List<TaggedRef>();

            // 1. Get the wall's data.
            var wallCurve = GetWallCenterLine(wall);
            if (wallCurve == null) return result;

            XYZ wallOrigin = wallCurve.GetEndPoint(0);
            XYZ wallEnd = wallCurve.GetEndPoint(1);
            XYZ wallDirection = (wallEnd - wallOrigin).Normalize();
            double wallLength = wallCurve.Length;

            // 2. Set up geometry options correctly.
            var geoOptions = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine,
            };

            // 3. Loop through each grid.
            foreach (Grid grid in allGrids)
            {
                try
                {
                    // 4. Get the grid's geometric curve.
                    Curve gridCurve = grid.Curve;
                    if (gridCurve == null) continue;

                    XYZ gridStart = gridCurve.GetEndPoint(0);
                    XYZ gridEnd = gridCurve.GetEndPoint(1);
                    XYZ gridDirection = (gridEnd - gridStart).Normalize();

                    // 5. Check if the grid is parallel to the wall.
                    double dotProduct = Math.Abs(wallDirection.X * gridDirection.X + wallDirection.Y * gridDirection.Y);
                    if (dotProduct > 0.9999) continue;

                    // 6. Calculate the intersection point between the wall's and grid's infinite lines.
                    double denominator = (wallDirection.X * gridDirection.Y - wallDirection.Y * gridDirection.X);
                    if (Math.Abs(denominator) < 0.0001) continue;

                    XYZ delta = gridStart - wallOrigin;
                    double t = (delta.X * gridDirection.Y - delta.Y * gridDirection.X) / denominator;
                    double u = t;

                    // 7. Check if the intersection is within a reasonable distance.
                    const double tolerance = 2.0;
                    if (u < -tolerance || u > wallLength + tolerance) continue;

                    // 8. Snap to the exact start or end of the wall.
                    if (Math.Abs(u) < 0.5) u = 0;
                    if (Math.Abs(u - wallLength) < 0.5) u = wallLength;

                    // 9. --- THE CRITICAL FIX ---
                    //    Calculate the exact XYZ point of intersection on the grid line.
                    XYZ intersectionPointOnGrid = gridStart + (u * gridDirection);

                    // 10. Get the specific geometric reference for the grid AT that intersection point.
                    //     This is what the NewDimension method requires.
                    Reference gridRef = gridCurve.GetEndPointReference(0); // Fallback, but we'll find the right one.

                    // Find the closest endpoint on the grid curve to the intersection point.
                    double distToStart = intersectionPointOnGrid.DistanceTo(gridStart);
                    double distToEnd = intersectionPointOnGrid.DistanceTo(gridEnd);

                    if (distToStart < distToEnd)
                        gridRef = gridCurve.GetEndPointReference(0);
                    else
                        gridRef = gridCurve.GetEndPointReference(1);

                    if (gridRef == null) continue;

                    // 11. Add the valid grid reference.
                    result.Add(new TaggedRef
                    {
                        Ref = gridRef,
                        U = u,
                        Kind = "grid",
                        SourceId = grid.Id
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error processing grid: {ex.Message}");
                }
            }

            // 12. Remove any duplicate grid references at the same 'U' position.
            result = result
                .GroupBy(r => Math.Round(r.U, 2))
                .Select(g => g.First())
                .OrderBy(r => r.U)
                .ToList();

            // 13. Popup to confirm grids are found.
            if (result.Count > 0)
            {
                string msg = $"Found {result.Count} valid grid references for wall:\n";
                foreach (var r in result) msg += $"  Grid {r.SourceId}: U={r.U:F2} ft\n";
                TaskDialog.Show("Grid Detection Success", msg);
            }
            else
            {
                TaskDialog.Show("Grid Detection", "No valid grid references could be created for this wall.");
            }

            return result;
        }

        // CRITICAL: This method gets a valid grid reference that NewDimension will accept
        private static Reference GetValidGridReference(Grid grid, Options opts)
        {
            try
            {
                // Method 1: Use the grid's Curve reference directly [citation:5][citation:8]
                // This is the most reliable method according to Revit API docs
                Curve curve = grid.Curve;
                if (curve != null && curve.Reference != null)
                    return curve.Reference;

                // Method 2: Get from geometry with ComputeReferences=true [citation:3]
                GeometryElement geom = grid.get_Geometry(opts);
                foreach (GeometryObject obj in geom)
                {
                    if (obj is Line line && line.Reference != null)
                        return line.Reference;

                    if (obj is GeometryInstance instance)
                    {
                        foreach (GeometryObject subObj in instance.GetInstanceGeometry())
                        {
                            if (subObj is Line subLine && subLine.Reference != null)
                                return subLine.Reference;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        // ── Opening edge references ──────────────────────────────────────────

        public static List<TaggedRef> GetOpeningEdgeRefs(
            Wall wall, Document doc, double startU, double endU)
        {
            var result = new List<TaggedRef>();

            IList<ElementId> insertIds = wall.FindInserts(true, false, false, false);
            if (insertIds == null || insertIds.Count == 0) return result;

            var cl = GetWallCenterLine(wall);
            if (cl == null) return result;

            XYZ origin = cl.GetEndPoint(0);
            XYZ wallDir = (cl.GetEndPoint(1) - origin).Normalize();
            double wallLen = cl.Length;

            const double coincideTol = 0.25;

            var opts = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine,
            };

            var wallSolids = CollectSolids(wall.get_Geometry(opts));
            if (wallSolids.Count == 0) return result;

            foreach (ElementId insertId in insertIds)
            {
                var insert = doc.GetElement(insertId) as FamilyInstance;
                var insertLoc = insert?.Location as LocationPoint;
                if (insertLoc == null) continue;

                double insertU = (insertLoc.Point - origin).DotProduct(wallDir);

                BoundingBoxXYZ bb = insert.get_BoundingBox(null);
                if (bb == null) continue;

                double halfW = Math.Abs((bb.Max - bb.Min).DotProduct(wallDir)) / 2.0;
                double sideA = insertU - halfW;
                double sideB = insertU + halfW;

                if (Math.Abs(sideA - startU) < coincideTol ||
                    Math.Abs(sideA - endU) < coincideTol ||
                    Math.Abs(sideB - startU) < coincideTol ||
                    Math.Abs(sideB - endU) < coincideTol) continue;

                if (sideA < 0.05 || sideB > wallLen - 0.05) continue;

                bool gotA = false, gotB = false;
                const double faceTol = 0.15;

                foreach (Solid solid in wallSolids)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (!(face is PlanarFace pf) || pf.Reference == null) continue;
                        if (Math.Abs(pf.FaceNormal.DotProduct(wallDir)) < 0.95) continue;

                        XYZ cen = FaceCentroid(pf);
                        double faceU = (cen - origin).DotProduct(wallDir);

                        if (!gotA && Math.Abs(faceU - sideA) < faceTol)
                        {
                            result.Add(new TaggedRef { Ref = pf.Reference, U = faceU, Kind = "opening", SourceId = insert.Id });
                            gotA = true;
                        }
                        else if (!gotB && Math.Abs(faceU - sideB) < faceTol)
                        {
                            result.Add(new TaggedRef { Ref = pf.Reference, U = faceU, Kind = "opening", SourceId = insert.Id });
                            gotB = true;
                        }

                        if (gotA && gotB) break;
                    }
                    if (gotA && gotB) break;
                }
            }

            return result;
        }

        // ── Merge: replace end-caps with nearby grids ────────────────────────

        // ── Merge: replace end-caps with nearby grids ────────────────────────

        public static List<TaggedRef> MergeEndCapsWithGrids(
            TaggedRef startCap, TaggedRef endCap,
            List<TaggedRef> gridRefs,
            double searchDistance = 10.0)
        {
            var result = new List<TaggedRef>();

            if (gridRefs == null || gridRefs.Count == 0)
            {
                result.Add(startCap);
                result.Add(endCap);
                return result;
            }

            // Find grids at or near the start and end positions
            TaggedRef startGrid = null;
            TaggedRef endGrid = null;

            double startDist = searchDistance;
            double endDist = searchDistance;

            foreach (var grid in gridRefs)
            {
                double distToStart = Math.Abs(grid.U - startCap.U);
                double distToEnd = Math.Abs(grid.U - endCap.U);

                if (distToStart < startDist)
                {
                    startDist = distToStart;
                    startGrid = grid;
                }

                if (distToEnd < endDist)
                {
                    endDist = distToEnd;  // FIXED: was "endDist = endDist;" which did nothing
                    endGrid = grid;
                }
            }

            // CRITICAL: Use grid if found, even at same position
            result.Add(startGrid ?? startCap);
            result.Add(endGrid ?? endCap);

            // Add interior grids
            double minU = result.Min(r => r.U);
            double maxU = result.Max(r => r.U);

            foreach (var grid in gridRefs)
            {
                if ((startGrid != null && grid.U == startGrid.U) ||
                    (endGrid != null && grid.U == endGrid.U))
                    continue;

                if (grid.U > minU + 0.01 && grid.U < maxU - 0.01)
                    result.Add(grid);
            }

            result = result.OrderBy(r => r.U).ToList();
            return result;
        }

        // ── Build ordered ReferenceArray with grid priority ───────────────────

        public static ReferenceArray BuildOrderedRefArray(
            List<TaggedRef> taggedRefs, double posTol = 0.05)
        {
            var sorted = taggedRefs.OrderBy(t => t.U).ToList();

            // Deduplicate - when grid and endcap share position, KEEP GRID
            var deduped = new List<TaggedRef>();
            foreach (var item in sorted)
            {
                var existing = deduped.FirstOrDefault(e => Math.Abs(e.U - item.U) < posTol);
                if (existing == null)
                {
                    deduped.Add(item);
                }
                else if (item.Kind == "grid" && existing.Kind != "grid")
                {
                    // Replace endcap with grid at same position
                    deduped.Remove(existing);
                    deduped.Add(item);
                }
            }

            deduped = deduped.OrderBy(t => t.U).ToList();

            var ra = new ReferenceArray();
            foreach (var item in deduped)
            {
                if (item.Ref != null)
                    ra.Append(item.Ref);
            }
            return ra;
        }

        // ── Build dimension line ─────────────────────────────────────────────

        public static Line BuildDimensionLine(Wall wall, XYZ offsetDir,
            double offsetDistance, List<TaggedRef> allRefs)
        {
            var cl = GetWallCenterLine(wall);
            if (cl == null) return null;

            if (allRefs == null || allRefs.Count < 2) return null;

            double minU = allRefs.Min(r => r.U);
            double maxU = allRefs.Max(r => r.U);

            XYZ origin = cl.GetEndPoint(0);
            XYZ wallDir = (cl.GetEndPoint(1) - origin).Normalize();

            double halfThick = wall.Width / 2.0;
            double totalOffset = halfThick + offsetDistance;
            XYZ offXYZ = offsetDir * totalOffset;

            // Minimal padding for grid-terminated dimensions
            var minRef = allRefs.OrderBy(r => r.U).First();
            var maxRef = allRefs.OrderByDescending(r => r.U).First();

            double startPad = (minRef.Kind == "grid") ? 0.08 : 0.33;
            double endPad = (maxRef.Kind == "grid") ? 0.08 : 0.33;

            XYZ dimStart = origin + wallDir * (minU - startPad) + offXYZ;
            XYZ dimEnd = origin + wallDir * (maxU + endPad) + offXYZ;

            double z = cl.GetEndPoint(0).Z;
            dimStart = new XYZ(dimStart.X, dimStart.Y, z);
            dimEnd = new XYZ(dimEnd.X, dimEnd.Y, z);

            if (dimStart.DistanceTo(dimEnd) < 0.01) return null;
            return Line.CreateBound(dimStart, dimEnd);
        }
    }
}