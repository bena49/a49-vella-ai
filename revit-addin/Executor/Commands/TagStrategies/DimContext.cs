// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/DimContext.cs
// ============================================================================
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

        public bool IsTotalOnly { get; set; } = false;
        public bool IsGridOnly { get; set; } = false;
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
    //  TaggedRef
    // =========================================================================

    public class TaggedRef
    {
        public Reference Ref { get; set; }
        public double U { get; set; }
        public string Kind { get; set; }
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

            return (startTR, endTR);
        }

        // ── Grid references - WORKING VERSION (preserved from your working code) ──

        public static List<TaggedRef> GetGridRefs(Wall wall, List<Grid> allGrids)
        {
            var result = new List<TaggedRef>();

            var wallCurve = GetWallCenterLine(wall);
            if (wallCurve == null) return result;

            XYZ wallOrigin = wallCurve.GetEndPoint(0);
            XYZ wallEnd = wallCurve.GetEndPoint(1);
            XYZ wallDirection = (wallEnd - wallOrigin).Normalize();
            double wallLength = wallCurve.Length;

            var geoOptions = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine,
            };

            foreach (Grid grid in allGrids)
            {
                try
                {
                    Curve gridCurve = grid.Curve;
                    if (gridCurve == null) continue;

                    XYZ gridStart = gridCurve.GetEndPoint(0);
                    XYZ gridEnd = gridCurve.GetEndPoint(1);

                    // REVISION: Vector-based parallel check (The "Non-Parallel" Killer)
                    XYZ gridDirection = (gridEnd - gridStart).Normalize();
                    double parallelCheck = Math.Abs(wallDirection.DotProduct(gridDirection));

                    // If the grid isn't almost perfectly perpendicular (Dot ~ 0), skip it.
                    // We use an even tighter tolerance here.
                    if (parallelCheck > 0.00005) continue;

                    // Intersection calculation...
                    double denominator = (wallDirection.X * gridDirection.Y - wallDirection.Y * gridDirection.X);
                    if (Math.Abs(denominator) < 0.0001) continue;

                    XYZ delta = gridStart - wallOrigin;
                    double u = (delta.X * gridDirection.Y - delta.Y * gridDirection.X) / denominator;

                    // Boundary + Snap Logic...
                    if (u < -2.0 || u > wallLength + 2.0) continue;
                    if (Math.Abs(u) < 0.25) u = 0;
                    if (Math.Abs(u - wallLength) < 0.25) u = wallLength;

                    // CRITICAL: Ensure the reference is obtained directly from the grid object
                    Reference gridRef = new Reference(grid);

                    result.Add(new TaggedRef
                    {
                        Ref = gridRef,
                        U = u,
                        Kind = "grid",
                        SourceId = grid.Id
                    });
                }
                catch { }
            }

            // Remove duplicates
            result = result
                .GroupBy(r => Math.Round(r.U, 2))
                .Select(g => g.First())
                .OrderBy(r => r.U)
                .ToList();

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

        // ── Merge: prioritize grids over wall ends at same position ────────────────────────

        public static List<TaggedRef> MergeEndCapsWithGrids(
        TaggedRef startCap, TaggedRef endCap,
        List<TaggedRef> gridRefs,
        double searchDistance = 10.0)
            {
            var result = new List<TaggedRef>();

            // 1. Safety check: if no grids, just return the wall endcaps
            if (gridRefs == null || gridRefs.Count == 0)
            {
                result.Add(startCap);
                result.Add(endCap);
                return result;
            }

            // 2. REVISION: Tight tolerance for conditional termination
            // This solves the Pink Dot (Grid at face) and Brown Dot (Wall corner) logic.
            const double snapTol = 0.1; // approx 30mm

            // Start Terminus Logic
            var startGrid = gridRefs.FirstOrDefault(g => Math.Abs(g.U - startCap.U) < snapTol);
            if (startGrid != null)
            {
                // Grid is effectively AT the wall face -> Snap to Grid (Pink Dot solved)
                result.Add(startGrid);
            }
            else
            {
                // No grid at the face -> Snap to Wall Exterior Corner (Brown Dot solved)
                result.Add(startCap);
            }

            // 3. Add all interior grids (strictly between the wall ends)
            double minU = startCap.U + snapTol;
            double maxU = endCap.U - snapTol;
            var interiorGrids = gridRefs.Where(g => g.U > minU && g.U < maxU);
            result.AddRange(interiorGrids);

            // End Terminus Logic
            var endGrid = gridRefs.FirstOrDefault(g => Math.Abs(g.U - endCap.U) < snapTol);
            if (endGrid != null)
            {
                // Grid is at the face -> Snap to Grid
                result.Add(endGrid);
            }
            else
            {
                // No grid -> Snap to Wall Exterior Corner
                result.Add(endCap);
            }

            // 4. Final sorting and deduplication by position
            return result
                .GroupBy(r => Math.Round(r.U, 3))
                .Select(g => g.First())
                .OrderBy(r => r.U)
                .ToList();
        }

        // ── Build ordered ReferenceArray with small segment filtering ───────────────────

        public static ReferenceArray BuildOrderedRefArray(List<TaggedRef> taggedRefs, double posTol = 0.25) // Increased tolerance
        {
            var sorted = taggedRefs.OrderBy(t => t.U).ToList();
            var deduped = new List<TaggedRef>();

            foreach (var item in sorted)
            {
                // Find existing ref within tolerance
                var existing = deduped.FirstOrDefault(e => Math.Abs(e.U - item.U) < posTol);

                if (existing == null)
                {
                    deduped.Add(item);
                }
                else
                {
                    // CRITICAL: If we have a Grid and a Wall endcap at the same spot, 
                    // ALWAYS keep only the Grid. This prevents the '0' dimension.
                    if (item.Kind == "grid" && existing.Kind != "grid")
                    {
                        deduped.Remove(existing);
                        deduped.Add(item);
                    }
                }
            }

            var ra = new ReferenceArray();
            foreach (var item in deduped)
            {
                if (item.Ref != null) ra.Append(item.Ref);
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

            const double pad = 0.33;

            XYZ dimStart = origin + wallDir * (minU - pad) + offXYZ;
            XYZ dimEnd = origin + wallDir * (maxU + pad) + offXYZ;

            double z = cl.GetEndPoint(0).Z;
            dimStart = new XYZ(dimStart.X, dimStart.Y, z);
            dimEnd = new XYZ(dimEnd.X, dimEnd.Y, z);

            if (dimStart.DistanceTo(dimEnd) < 0.01) return null;
            return Line.CreateBound(dimStart, dimEnd);
        }

        // ── Building Envelope Calculator ─────────────────────────────────────────────
        public static BoundingBoxXYZ GetProjectEnvelope(List<Wall> walls)
        {
            if (walls.Count == 0) return null;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var wall in walls)
            {
                var bb = wall.get_BoundingBox(null);
                if (bb == null) continue;

                if (bb.Min.X < minX) minX = bb.Min.X;
                if (bb.Min.Y < minY) minY = bb.Min.Y;
                if (bb.Max.X > maxX) maxX = bb.Max.X;
                if (bb.Max.Y > maxY) maxY = bb.Max.Y;
            }

            var result = new BoundingBoxXYZ();
            result.Min = new XYZ(minX, minY, 0);
            result.Max = new XYZ(maxX, maxY, 0);
            return result;
        }
    }
}