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
        public double OffsetDistance { get; set; } = 2.625; // ~800mm in feet
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
    //  DimHelpers
    // =========================================================================

    public static class DimHelpers
    {
        // ------------------------------------------------------------------
        //  1. Wall geometry helpers
        // ------------------------------------------------------------------

        public static XYZ GetWallDirection(Wall wall)
        {
            var lc = wall.Location as LocationCurve;
            var line = lc?.Curve as Line;
            if (line == null) return XYZ.BasisX;
            return (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
        }

        public static XYZ GetWallNormal(Wall wall)
        {
            var dir = GetWallDirection(wall);
            return new XYZ(-dir.Y, dir.X, 0).Normalize();
        }

        public static Line GetWallCenterLine(Wall wall)
        {
            var lc = wall.Location as LocationCurve;
            return lc?.Curve as Line;
        }

        // ------------------------------------------------------------------
        //  2. Collect wall solids (handles GeometryInstance wrapping)
        // ------------------------------------------------------------------

        private static List<Solid> GetWallSolids(Wall wall, Options opts)
        {
            var solids = new List<Solid>();
            foreach (GeometryObject obj in wall.get_Geometry(opts))
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

        // ------------------------------------------------------------------
        //  3. Exterior / interior detection
        // ------------------------------------------------------------------

        public static bool IsExteriorWall(Wall wall, Document doc, View view)
        {
            try
            {
                var cl = GetWallCenterLine(wall);
                if (cl == null) return false;

                XYZ midPoint = cl.Evaluate(0.5, true);
                XYZ normal = GetWallNormal(wall);
                XYZ probe = midPoint + normal * 1.0;

                var phase = doc.GetElement(
                    view.get_Parameter(BuiltInParameter.VIEW_PHASE).AsElementId()) as Phase;

                Room room = doc.GetRoomAtPoint(probe, phase);
                return room == null;
            }
            catch { return false; }
        }

        public static XYZ GetDimLineOffsetDirection(Wall wall, Document doc,
            View view, bool smartPlacement)
        {
            if (!smartPlacement) return GetWallNormal(wall);
            bool exterior = IsExteriorWall(wall, doc, view);
            return exterior ? GetWallNormal(wall) : GetWallNormal(wall);
        }

        // ------------------------------------------------------------------
        //  4. Wall end-cap face references
        // ------------------------------------------------------------------

        public static (Reference startFaceRef, Reference endFaceRef)
            GetWallEndFaceReferences(Wall wall)
        {
            var opts = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            var cl = GetWallCenterLine(wall);
            if (cl == null) return (null, null);

            XYZ startPt = cl.GetEndPoint(0);
            XYZ endPt = cl.GetEndPoint(1);
            XYZ wallDir = (endPt - startPt).Normalize();
            double wallLen = startPt.DistanceTo(endPt);

            Reference startRef = null, endRef = null;

            foreach (Solid solid in GetWallSolids(wall, opts))
            {
                foreach (Face face in solid.Faces)
                {
                    if (!(face is PlanarFace pf)) continue;

                    // End-cap faces: normal parallel to wall direction
                    double dot = Math.Abs(pf.FaceNormal.DotProduct(wallDir));
                    if (dot < 0.95) continue;

                    // Use face UV centroid for reliable position
                    BoundingBoxUV bbuv = pf.GetBoundingBox();
                    UV uvMid = new UV(
                        (bbuv.Min.U + bbuv.Max.U) / 2.0,
                        (bbuv.Min.V + bbuv.Max.V) / 2.0);
                    XYZ centre = pf.Evaluate(uvMid);
                    double u = (centre - startPt).DotProduct(wallDir);

                    if (u < wallLen / 2.0)
                        startRef = pf.Reference;
                    else
                        endRef = pf.Reference;
                }

                if (startRef != null && endRef != null) break;
            }

            return (startRef, endRef);
        }

        // ------------------------------------------------------------------
        //  5. Opening edge references (doors + windows)
        // ------------------------------------------------------------------

        public static List<Reference> GetOpeningEdgeReferences(Wall wall, Document doc)
        {
            var refs = new List<Reference>();

            IList<ElementId> insertIds = wall.FindInserts(true, false, false, false);
            if (insertIds == null || insertIds.Count == 0) return refs;

            XYZ wallDir = GetWallDirection(wall);
            var cl = GetWallCenterLine(wall);
            if (cl == null) return refs;

            XYZ wallStart = cl.GetEndPoint(0);
            double wallLen = wallStart.DistanceTo(cl.GetEndPoint(1));

            // Skip opening edges within this distance of a wall end — produces zero segment
            const double minDistFromEnd = 0.20; // ~60mm in feet

            var opts = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            var wallSolids = GetWallSolids(wall, opts);
            if (wallSolids.Count == 0) return refs;

            foreach (ElementId insertId in insertIds)
            {
                var insert = doc.GetElement(insertId) as FamilyInstance;
                var insertLoc = insert?.Location as LocationPoint;
                if (insertLoc == null) continue;

                XYZ insertPt = insertLoc.Point;
                double insertU = (insertPt - wallStart).DotProduct(wallDir);

                BoundingBoxXYZ bb = insert.get_BoundingBox(null);
                if (bb == null) continue;

                double halfWidth = Math.Abs((bb.Max - bb.Min).DotProduct(wallDir)) / 2.0;
                double sideA = insertU - halfWidth;
                double sideB = insertU + halfWidth;

                // Skip if opening edges are too close to wall ends
                if (sideA < minDistFromEnd || sideB > wallLen - minDistFromEnd)
                    continue;

                bool gotA = false, gotB = false;
                const double faceTol = 0.15; // ~45mm

                foreach (Solid solid in wallSolids)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (!(face is PlanarFace pf) || pf.Reference == null) continue;

                        // Opening edge faces: normal parallel to wall direction
                        double dot = Math.Abs(pf.FaceNormal.DotProduct(wallDir));
                        if (dot < 0.95) continue;

                        BoundingBoxUV bbuv = pf.GetBoundingBox();
                        UV uvMid = new UV(
                            (bbuv.Min.U + bbuv.Max.U) / 2.0,
                            (bbuv.Min.V + bbuv.Max.V) / 2.0);
                        XYZ centre = pf.Evaluate(uvMid);
                        double faceU = (centre - wallStart).DotProduct(wallDir);

                        if (!gotA && Math.Abs(faceU - sideA) < faceTol)
                        {
                            refs.Add(pf.Reference);
                            gotA = true;
                        }
                        else if (!gotB && Math.Abs(faceU - sideB) < faceTol)
                        {
                            refs.Add(pf.Reference);
                            gotB = true;
                        }

                        if (gotA && gotB) break;
                    }
                    if (gotA && gotB) break;
                }
            }

            return refs;
        }

        // ------------------------------------------------------------------
        //  6. Grid references
        // ------------------------------------------------------------------

        public static List<Reference> GetGridReferences(Wall wall, List<Grid> allGrids)
        {
            var refs = new List<Reference>();

            var cl = GetWallCenterLine(wall);
            if (cl == null) return refs;

            XYZ tStart = cl.GetEndPoint(0);
            XYZ tEnd = cl.GetEndPoint(1);
            XYZ wallDir = (tEnd - tStart).Normalize();
            double len = tStart.DistanceTo(tEnd);

            // Extend wall line so grids passing through are caught
            const double extend = 10.0; // 10 ft
            Line extWall = Line.CreateBound(
                tStart - wallDir * extend,
                tEnd + wallDir * extend);

            var opts = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine,
            };

            foreach (Grid grid in allGrids)
            {
                // Extend grid curve to ensure intersection
                Line extGrid = null;
                try
                {
                    XYZ gP0 = grid.Curve.GetEndPoint(0);
                    XYZ gP1 = grid.Curve.GetEndPoint(1);
                    XYZ gDir = (gP1 - gP0).Normalize();
                    XYZ gMid = grid.Curve.Evaluate(0.5, true);
                    extGrid = Line.CreateBound(gMid - gDir * 500, gMid + gDir * 500);
                }
                catch { continue; }

                IntersectionResultArray ira;
                if (extWall.Intersect(extGrid, out ira) != SetComparisonResult.Overlap
                    || ira == null || ira.Size == 0)
                    continue;

                XYZ pt = ira.get_Item(0).XYZPoint;
                double u = (pt - tStart).DotProduct(wallDir);

                // Must be within or very near actual wall extent
                if (u < -extend || u > len + extend) continue;

                // Extract reference from grid geometry
                Reference gridRef = null;
                try
                {
                    foreach (GeometryObject obj in grid.get_Geometry(opts))
                    {
                        if (obj is Line line && line.Reference != null)
                        {
                            gridRef = line.Reference;
                            break;
                        }
                        if (obj is GeometryInstance gi)
                            foreach (GeometryObject sub in gi.GetInstanceGeometry())
                                if (sub is Line sl && sl.Reference != null)
                                {
                                    gridRef = sl.Reference;
                                    break;
                                }
                        if (gridRef != null) break;
                    }
                }
                catch { }

                if (gridRef != null)
                    refs.Add(gridRef);
            }

            return refs;
        }

        // ------------------------------------------------------------------
        //  7. Order references along wall axis using face centroids
        // ------------------------------------------------------------------

        public static ReferenceArray OrderReferencesAlongWall(
            List<Reference> references, Wall wall, Document doc)
        {
            var cl = GetWallCenterLine(wall);
            XYZ wStart = cl?.GetEndPoint(0) ?? XYZ.Zero;
            XYZ wDir = GetWallDirection(wall);

            var opts = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine,
            };

            var sorted = references
                .Select((r, idx) =>
                {
                    double u = idx * 0.0001; // tiny index offset as tiebreaker
                    try
                    {
                        Element el = doc.GetElement(r.ElementId);

                        if (el is Wall w)
                        {
                            // Match reference to actual face using stable representation
                            string rStable = "";
                            try { rStable = r.ConvertToStableRepresentation(doc); } catch { }

                            foreach (Solid solid in GetWallSolids(w, opts))
                            {
                                bool found = false;
                                foreach (Face face in solid.Faces)
                                {
                                    if (face.Reference == null) continue;
                                    try
                                    {
                                        if (!string.IsNullOrEmpty(rStable) &&
                                            face.Reference.ConvertToStableRepresentation(doc) == rStable)
                                        {
                                            BoundingBoxUV bb = face.GetBoundingBox();
                                            UV uv = new UV(
                                                (bb.Min.U + bb.Max.U) / 2.0,
                                                (bb.Min.V + bb.Max.V) / 2.0);
                                            XYZ c = face.Evaluate(uv);
                                            u = (c - wStart).DotProduct(wDir);
                                            found = true;
                                            break;
                                        }
                                    }
                                    catch { }
                                }
                                if (found) break;
                            }
                        }
                        else if (el?.Location is LocationPoint lp)
                        {
                            u = (lp.Point - wStart).DotProduct(wDir);
                        }
                        else if (el?.Location is LocationCurve lc2)
                        {
                            XYZ mid = lc2.Curve.Evaluate(0.5, true);
                            u = (mid - wStart).DotProduct(wDir);
                        }
                    }
                    catch { }
                    return (u, idx, r);
                })
                .OrderBy(t => t.u)
                .ThenBy(t => t.idx)
                .Select(t => t.r)
                .ToList();

            var ra = new ReferenceArray();
            foreach (var r in sorted) ra.Append(r);
            return ra;
        }

        // ------------------------------------------------------------------
        //  8. Dimension line geometry
        // ------------------------------------------------------------------

        public static Line BuildDimensionLine(Wall wall, XYZ offsetDir,
            double offsetDistance)
        {
            var cl = GetWallCenterLine(wall);
            if (cl == null) return null;

            XYZ start = cl.GetEndPoint(0);
            XYZ end = cl.GetEndPoint(1);
            XYZ wallDir = GetWallDirection(wall);

            // Offset from wall centreline: half-thickness + user offset
            double halfThick = wall.Width / 2.0;
            double totalOffset = halfThick + offsetDistance;
            XYZ offset = offsetDir * totalOffset;

            XYZ dimStart = new XYZ(start.X + offset.X, start.Y + offset.Y, start.Z);
            XYZ dimEnd = new XYZ(end.X + offset.X, end.Y + offset.Y, end.Z);

            // Small extension so witness lines don't crowd the end caps
            const double ext = 0.5; // 0.5 ft
            dimStart -= wallDir * ext;
            dimEnd += wallDir * ext;

            if (dimStart.DistanceTo(dimEnd) < 0.01) return null;
            return Line.CreateBound(dimStart, dimEnd);
        }
    }
}
