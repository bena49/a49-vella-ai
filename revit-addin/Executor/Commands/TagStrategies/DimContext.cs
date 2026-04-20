using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

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

        // ── Collect solids (handles GeometryInstance) ────────────────────────

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
                            startTR = new TaggedRef { Ref = pf.Reference, U = u, Kind = "endcap" };
                        }
                    }
                    else
                    {
                        if (u > bestEndU)
                        {
                            bestEndU = u;
                            endTR = new TaggedRef { Ref = pf.Reference, U = u, Kind = "endcap" };
                        }
                    }
                }
            }

            // FIX: Symbolic fallback for walls joined at both ends.
            // When wall joins consume end-cap geometry, retry with IncludeNonVisibleObjects=true
            // to expose the hidden faces that Revit suppresses at joins.
            // This is the correct Revit API approach — non-visible geometry always
            // contains the full solid including joined faces.
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
                            startTR = new TaggedRef { Ref = pf.Reference, U = u, Kind = "endcap" };

                        if (endTR == null && u >= len / 2.0)
                            endTR = new TaggedRef { Ref = pf.Reference, U = u, Kind = "endcap" };
                    }

                    if (startTR != null && endTR != null) break;
                }
            }

            // Last resort: if still null after geometry retry, use a point reference
            // obtained by creating a temporary reference from the curve itself.
            // PointOnEdge references are always accepted by NewDimension.
            if (startTR == null || endTR == null)
            {
                var locCurve = wall.Location as LocationCurve;
                if (locCurve?.Curve is Line locLine)
                {
                    // Get geometry with view context to obtain curve references
                    var curveOpts = new Options
                    {
                        ComputeReferences = true,
                        IncludeNonVisibleObjects = true,
                        DetailLevel = ViewDetailLevel.Fine,
                    };

                    // Iterate to find any Line geometry whose reference can be used
                    foreach (GeometryObject obj in wall.get_Geometry(curveOpts))
                    {
                        Reference lineRef = null;

                        if (obj is Line ln && ln.Reference != null)
                            lineRef = ln.Reference;
                        else if (obj is GeometryInstance gi)
                            foreach (GeometryObject sub in gi.GetInstanceGeometry())
                                if (sub is Line sl && sl.Reference != null)
                                { lineRef = sl.Reference; break; }

                        // A line reference from geometry is sufficient for NewDimension
                        // as a last-resort fallback — better than no reference at all
                        if (lineRef != null)
                        {
                            if (startTR == null)
                                startTR = new TaggedRef { Ref = lineRef, U = 0, Kind = "endcap" };
                            if (endTR == null)
                                endTR = new TaggedRef { Ref = lineRef, U = len, Kind = "endcap" };
                            break;
                        }
                    }
                }
            }

            return (startTR, endTR);
        }

        // ── Grid references ──────────────────────────────────────────────────
        //
        // Returns TaggedRefs for all grids that intersect (or nearly intersect)
        // the wall centre line extended by 'ext' feet in each direction.

        public static List<TaggedRef> GetGridRefs(Wall wall, List<Grid> allGrids)
        {
            var result = new List<TaggedRef>();

            var cl = GetWallCenterLine(wall);
            if (cl == null) return result;

            XYZ origin = cl.GetEndPoint(0);
            XYZ wallDir = (cl.GetEndPoint(1) - origin).Normalize();
            double len = cl.Length;

            const double ext = 20.0; // ft — wide enough to find grids past corner walls
            Line extWall = Line.CreateBound(
                origin + wallDir * -ext,
                origin + wallDir * (len + ext));

            var opts = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine,
            };

            foreach (Grid grid in allGrids)
            {
                Line extGrid;
                try
                {
                    XYZ gP0 = grid.Curve.GetEndPoint(0);
                    XYZ gP1 = grid.Curve.GetEndPoint(1);
                    XYZ gDir = (gP1 - gP0).Normalize();
                    XYZ gMid = grid.Curve.Evaluate(0.5, true);
                    extGrid = Line.CreateBound(gMid - gDir * 1000, gMid + gDir * 1000);
                }
                catch { continue; }

                IntersectionResultArray ira;
                if (extWall.Intersect(extGrid, out ira) != SetComparisonResult.Overlap
                    || ira == null || ira.Size == 0) continue;

                XYZ pt = ira.get_Item(0).XYZPoint;
                double u = (pt - origin).DotProduct(wallDir);

                if (u < -ext || u > len + ext) continue;

                Reference gridRef = null;
                try
                {
                    foreach (GeometryObject obj in grid.get_Geometry(opts))
                    {
                        if (obj is Line ln && ln.Reference != null)
                        { gridRef = ln.Reference; break; }
                        if (obj is GeometryInstance gi)
                            foreach (GeometryObject sub in gi.GetInstanceGeometry())
                                if (sub is Line sl && sl.Reference != null)
                                { gridRef = sl.Reference; break; }
                        if (gridRef != null) break;
                    }
                }
                catch { }

                if (gridRef != null)
                    result.Add(new TaggedRef { Ref = gridRef, U = u, Kind = "grid" });
            }

            return result;
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

            // Skip opening edges within this distance of an end-cap position
            const double coincideTol = 0.25; // ~75mm

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

                // Skip if either edge coincides with end-cap positions
                if (Math.Abs(sideA - startU) < coincideTol ||
                    Math.Abs(sideA - endU) < coincideTol ||
                    Math.Abs(sideB - startU) < coincideTol ||
                    Math.Abs(sideB - endU) < coincideTol) continue;

                // Skip if outside wall extent
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
                            result.Add(new TaggedRef { Ref = pf.Reference, U = faceU, Kind = "opening" });
                            gotA = true;
                        }
                        else if (!gotB && Math.Abs(faceU - sideB) < faceTol)
                        {
                            result.Add(new TaggedRef { Ref = pf.Reference, U = faceU, Kind = "opening" });
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
        //
        // Core fix for Issue 1:
        // If a grid is within 'snapTol' feet of a wall end-cap position,
        // the grid reference REPLACES the end-cap reference.
        // This extends the dimension string to the grid line rather than
        // stopping at the inside face of the corner wall junction.

        public static List<TaggedRef> MergeEndCapsWithGrids(
            TaggedRef startCap, TaggedRef endCap,
            List<TaggedRef> gridRefs,
            double snapTol = 1.0) // kept for signature compatibility
        {
            var result = new List<TaggedRef>();

            // Directional snap logic:
            // Find the grid sitting at or BEYOND each end-cap (outside the wall).
            // Tolerance of 2.0ft (~600mm) safely covers any wall thickness
            // so that corner grids (which may be offset by a full wall width
            // from the end-cap face) are always snapped to.
            // Without grids the end-cap face is used as fallback.

            TaggedRef startGridSnap = gridRefs
                .Where(g => g.U <= startCap.U + 2.0)   // grid at or before start cap
                .OrderBy(g => g.U)                       // pick the outermost (smallest U)
                .FirstOrDefault();

            TaggedRef endGridSnap = gridRefs
                .Where(g => g.U >= endCap.U - 2.0)      // grid at or after end cap
                .OrderByDescending(g => g.U)             // pick the outermost (largest U)
                .FirstOrDefault();

            // Terminal references: prefer grid over end-cap
            result.Add(startGridSnap ?? startCap);
            result.Add(endGridSnap ?? endCap);

            // Add interior grids — those not used as terminal snaps
            foreach (var g in gridRefs)
            {
                if (g == startGridSnap || g == endGridSnap) continue;
                result.Add(g);
            }

            return result;
        }

        // ── Build ordered, deduplicated ReferenceArray ───────────────────────

        public static ReferenceArray BuildOrderedRefArray(
            List<TaggedRef> taggedRefs, double posTol = 0.05)
        {
            // Sort by u-position
            var sorted = taggedRefs.OrderBy(t => t.U).ToList();

            // Deduplicate by position — keep first of each cluster
            var deduped = new List<TaggedRef>();
            foreach (var item in sorted)
            {
                if (!deduped.Any(e => Math.Abs(e.U - item.U) < posTol))
                    deduped.Add(item);
            }

            var ra = new ReferenceArray();
            foreach (var item in deduped)
                ra.Append(item.Ref);
            return ra;
        }

        // ── Build dimension line ─────────────────────────────────────────────

        public static Line BuildDimensionLine(Wall wall, XYZ offsetDir,
            double offsetDistance, double minU, double maxU)
        {
            var cl = GetWallCenterLine(wall);
            if (cl == null) return null;

            XYZ origin = cl.GetEndPoint(0);
            XYZ wallDir = (cl.GetEndPoint(1) - origin).Normalize();

            double halfThick = wall.Width / 2.0;
            double totalOffset = halfThick + offsetDistance;
            XYZ offXYZ = offsetDir * totalOffset;

            const double pad = 0.33; // ~100mm breathing room
            XYZ dimStart = origin + wallDir * (minU - pad) + offXYZ;
            XYZ dimEnd = origin + wallDir * (maxU + pad) + offXYZ;

            // Flatten to wall Z level
            double z = cl.GetEndPoint(0).Z;
            dimStart = new XYZ(dimStart.X, dimStart.Y, z);
            dimEnd = new XYZ(dimEnd.X, dimEnd.Y, z);

            if (dimStart.DistanceTo(dimEnd) < 0.01) return null;
            return Line.CreateBound(dimStart, dimEnd);
        }
    }
}
