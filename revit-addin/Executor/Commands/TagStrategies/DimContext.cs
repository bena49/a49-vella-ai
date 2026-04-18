using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace A49AIRevitAssistant.Executor.Commands.DimStrategies
{
    // =========================================================================
    //  DimRequest  —  what the user asked for (built from wizard / NLP payload)
    // =========================================================================

    public class DimRequest
    {
        // --- Scope ---

        /// <summary>The floor-plan view to dimension.</summary>
        public View TargetView { get; set; }

        /// <summary>
        /// Optional explicit wall ElementIds to dimension.
        /// If empty, AutoDimCommand will collect ALL walls visible in TargetView.
        /// </summary>
        public List<ElementId> WallIds { get; set; } = new List<ElementId>();

        // --- What to reference ---

        /// <summary>Include door and window opening edges as references.</summary>
        public bool IncludeOpenings { get; set; } = true;

        /// <summary>Include intersecting wall faces as references.</summary>
        public bool IncludeIntersectingWalls { get; set; } = true;

        /// <summary>Include structural grid intersections as references.</summary>
        public bool IncludeGrids { get; set; } = true;

        // --- Placement ---

        /// <summary>
        /// Distance from the wall face to the dimension line, in feet (Revit internal units).
        /// Default: ~800mm → 2.625 ft
        /// </summary>
        public double OffsetDistance { get; set; } = 2.625;

        /// <summary>
        /// When true, exterior walls dimension outward from the building perimeter.
        /// When false, all dimension lines are placed on the same offset side.
        /// </summary>
        public bool SmartExteriorPlacement { get; set; } = true;
    }


    // =========================================================================
    //  DimResult  —  outcome of dimensioning one element
    // =========================================================================

    public class DimResult
    {
        public bool Success { get; set; }

        /// <summary>The Dimension element created, or null if skipped/failed.</summary>
        public Dimension CreatedDimension { get; set; }

        /// <summary>Number of references included in this dimension string.</summary>
        public int ReferenceCount { get; set; }

        /// <summary>Why this element was skipped (if Success = false and not an error).</summary>
        public string SkipReason { get; set; }

        /// <summary>Exception message if an error occurred.</summary>
        public string ErrorMessage { get; set; }

        // --- Convenience factories ---

        public static DimResult Succeeded(Dimension dim, int refCount) =>
            new DimResult { Success = true, CreatedDimension = dim, ReferenceCount = refCount };

        public static DimResult Skipped(string reason) =>
            new DimResult { Success = false, SkipReason = reason };

        public static DimResult Failed(string error) =>
            new DimResult { Success = false, ErrorMessage = error };
    }


    // =========================================================================
    //  DimContext  —  runtime bag passed to every strategy call
    // =========================================================================

    public class DimContext
    {
        public Document Document { get; set; }
        public DimRequest Request { get; set; }

        // Pre-collected at AutoDimCommand startup to avoid repeated FilteredElementCollector calls
        public List<Wall> AllWallsInView { get; set; } = new List<Wall>();
        public List<Grid> AllGridsInView { get; set; } = new List<Grid>();

        // Dimension type to use — resolved once in AutoDimCommand, passed here
        public DimensionType LinearDimensionType { get; set; }
    }


    // =========================================================================
    //  DimHelpers  —  static geometry and reference utilities
    // =========================================================================

    public static class DimHelpers
    {
        // ------------------------------------------------------------------
        //  1.  Wall orientation helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns the normalised direction vector along the wall (start → end).
        /// </summary>
        public static XYZ GetWallDirection(Wall wall)
        {
            var locCurve = wall.Location as LocationCurve;
            if (locCurve == null) return XYZ.BasisX;
            var line = locCurve.Curve as Line;
            if (line == null) return XYZ.BasisX;
            return (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
        }

        /// <summary>
        /// Returns the wall's outward-facing normal (perpendicular to wall direction, in XY plane).
        /// </summary>
        public static XYZ GetWallNormal(Wall wall)
        {
            var dir = GetWallDirection(wall);
            // Rotate 90° CCW in XY plane → left-hand normal
            return new XYZ(-dir.Y, dir.X, 0).Normalize();
        }

        /// <summary>
        /// Returns the wall centre-line as a Line, or null if not a straight wall.
        /// </summary>
        public static Line GetWallCenterLine(Wall wall)
        {
            var locCurve = wall.Location as LocationCurve;
            return locCurve?.Curve as Line;
        }

        // ------------------------------------------------------------------
        //  2.  Exterior / interior detection (ray-cast approach)
        //      Same logic used in window tagging strategy.
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns true if the wall is an exterior (perimeter) wall.
        /// Uses a short ray cast from the wall normal side: if we hit no room,
        /// that side is exterior.
        /// </summary>
        public static bool IsExteriorWall(Wall wall, Document doc, View view)
        {
            try
            {
                var centerLine = GetWallCenterLine(wall);
                if (centerLine == null) return false;

                // Mid-point of the wall at its base level
                XYZ midPoint = centerLine.Evaluate(0.5, true);

                // Offset slightly outward along wall normal
                XYZ normal = GetWallNormal(wall);
                XYZ probePoint = midPoint + normal * 1.0; // 1 ft outward

                // Check if there is a room at the probe point
                // If no room → that side is exterior
                var phase = doc.GetElement(view.get_Parameter(
                    BuiltInParameter.VIEW_PHASE).AsElementId()) as Phase;

                Room room = doc.GetRoomAtPoint(probePoint, phase);
                return room == null;
            }
            catch
            {
                // Default to interior if detection fails
                return false;
            }
        }

        /// <summary>
        /// Returns the XYZ offset direction for the dimension line:
        /// - Exterior walls → outward (away from building)
        /// - Interior walls → toward the larger room, or default normal
        /// </summary>
        public static XYZ GetDimLineOffsetDirection(Wall wall, Document doc,
            View view, bool smartPlacement)
        {
            if (!smartPlacement) return GetWallNormal(wall);

            bool exterior = IsExteriorWall(wall, doc, view);
            XYZ normal = GetWallNormal(wall);

            if (exterior)
            {
                // Outward = direction where there is no room
                // IsExteriorWall probes in +normal direction; if exterior, that IS outward
                return normal;
            }
            else
            {
                // Interior: place on the +normal side by default
                // A future enhancement could pick the side with the larger room
                return normal;
            }
        }

        // ------------------------------------------------------------------
        //  3.  Reference extraction — wall start / end faces
        // ------------------------------------------------------------------

        /// <summary>
        /// Extracts face References for the two end-cap faces of a straight wall
        /// (the faces perpendicular to the wall direction at start and end points).
        /// Returns null entries if a face cannot be found.
        /// ComputeReferences must be true on the Options passed.
        /// </summary>
        public static (Reference startFaceRef, Reference endFaceRef)
            GetWallEndFaceReferences(Wall wall)
        {
            var opts = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            var centerLine = GetWallCenterLine(wall);
            if (centerLine == null) return (null, null);

            XYZ startPt = centerLine.GetEndPoint(0);
            XYZ endPt = centerLine.GetEndPoint(1);
            XYZ wallDir = (endPt - startPt).Normalize();

            Reference startRef = null, endRef = null;

            var geom = wall.get_Geometry(opts);
            foreach (GeometryObject obj in geom)
            {
                if (!(obj is Solid solid)) continue;
                foreach (Face face in solid.Faces)
                {
                    if (!(face is PlanarFace pf)) continue;

                    // End-cap faces are perpendicular to wall direction
                    // i.e. face normal is parallel to wall direction
                    double dot = Math.Abs(pf.FaceNormal.DotProduct(wallDir));
                    if (dot < 0.99) continue; // not an end-cap

                    // Determine start vs end by comparing face origin to wall midpoint
                    XYZ mid = (startPt + endPt) / 2;
                    double distToStart = pf.Origin.DistanceTo(startPt);
                    double distToEnd = pf.Origin.DistanceTo(endPt);

                    if (distToStart < distToEnd)
                        startRef = pf.Reference;
                    else
                        endRef = pf.Reference;
                }
            }

            return (startRef, endRef);
        }

        // ------------------------------------------------------------------
        //  4.  Reference extraction — opening edges (doors + windows)
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns References for the two vertical opening-edge faces of each
        /// door or window insert in the given wall.
        /// Uses wall.FindInserts() then geometry traversal on the wall solid
        /// to find planar faces whose normals align with the wall direction
        /// (i.e. the cut faces at opening sides).
        /// </summary>
        public static List<Reference> GetOpeningEdgeReferences(Wall wall, Document doc)
        {
            var refs = new List<Reference>();

            // Collect all hosted inserts (doors + windows + openings)
            // Positional args: (addedByWall, includeShadows, includeEmbeddedWalls, includeSharedEmbeddedInserts)
            IList<ElementId> insertIds = wall.FindInserts(true, false, false, false);

            if (insertIds == null || insertIds.Count == 0)
                return refs;

            XYZ wallDir = GetWallDirection(wall);
            var centerLine = GetWallCenterLine(wall);
            if (centerLine == null) return refs;

            XYZ wallStart = centerLine.GetEndPoint(0);
            XYZ wallEnd = centerLine.GetEndPoint(1);

            var opts = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            // Get wall geometry solid for face extraction
            var wallGeom = wall.get_Geometry(opts);
            Solid wallSolid = null;
            foreach (GeometryObject obj in wallGeom)
            {
                if (obj is Solid s && s.Volume > 0) { wallSolid = s; break; }
            }
            if (wallSolid == null) return refs;

            // For each insert, find the two wall-solid faces at the opening sides
            foreach (ElementId insertId in insertIds)
            {
                var insert = doc.GetElement(insertId) as FamilyInstance;
                if (insert == null) continue;

                // Get the insert's location point to find its position along the wall
                var insertLoc = insert.Location as LocationPoint;
                if (insertLoc == null) continue;

                XYZ insertPt = insertLoc.Point;

                // Get insert width from bounding box (approximate opening width)
                BoundingBoxXYZ bb = insert.get_BoundingBox(null);
                if (bb == null) continue;

                // Project insert centre onto wall direction
                double insertU = (insertPt - wallStart).DotProduct(wallDir);

                // Estimate half-width along wall direction from bbox
                double halfWidth = Math.Abs(
                    (bb.Max - bb.Min).DotProduct(wallDir)) / 2.0 + 0.05; // small tolerance

                double sideA = insertU - halfWidth;
                double sideB = insertU + halfWidth;

                // Find wall faces whose normal aligns with wallDir
                // and whose position along the wall matches sideA or sideB
                foreach (Face face in wallSolid.Faces)
                {
                    if (!(face is PlanarFace pf)) continue;

                    double dot = Math.Abs(pf.FaceNormal.DotProduct(wallDir));
                    if (dot < 0.99) continue; // must be parallel to wall direction

                    double faceU = (pf.Origin - wallStart).DotProduct(wallDir);

                    if (Math.Abs(faceU - sideA) < 0.1 || Math.Abs(faceU - sideB) < 0.1)
                    {
                        if (pf.Reference != null)
                            refs.Add(pf.Reference);
                    }
                }
            }

            return refs;
        }

        // ------------------------------------------------------------------
        //  5.  Reference extraction — intersecting wall faces
        // ------------------------------------------------------------------

        /// <summary>
        /// For walls in the view that frame into the target wall,
        /// returns the face Reference of the framing wall at the intersection point.
        /// </summary>
        public static List<Reference> GetIntersectingWallReferences(
            Wall targetWall, List<Wall> allWalls, Document doc)
        {
            var refs = new List<Reference>();

            var targetLine = GetWallCenterLine(targetWall);
            if (targetLine == null) return refs;

            XYZ tStart = targetLine.GetEndPoint(0);
            XYZ tEnd = targetLine.GetEndPoint(1);

            var opts = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            };

            foreach (Wall other in allWalls)
            {
                if (other.Id == targetWall.Id) continue;

                var otherLine = GetWallCenterLine(other);
                if (otherLine == null) continue;

                // Check if other wall's centre line intersects target wall's centre line
                var result = targetLine.Intersect(otherLine, out IntersectionResultArray ira);
                if (result != SetComparisonResult.Overlap || ira == null || ira.Size == 0)
                    continue;

                XYZ intersectionPt = ira.get_Item(0).XYZPoint;

                // Confirm intersection is within the target wall's extent (not just extension)
                double u = (intersectionPt - tStart).DotProduct((tEnd - tStart).Normalize());
                double len = tStart.DistanceTo(tEnd);
                if (u < 0.01 || u > len - 0.01) continue; // skip endpoints

                // Get the face of the intersecting wall that faces the intersection
                XYZ otherDir = GetWallDirection(other);
                XYZ otherNormal = GetWallNormal(other);

                var otherGeom = other.get_Geometry(opts);
                foreach (GeometryObject obj in otherGeom)
                {
                    if (!(obj is Solid solid)) continue;
                    foreach (Face face in solid.Faces)
                    {
                        if (!(face is PlanarFace pf)) continue;

                        // We want end-cap faces of the intersecting wall (perpendicular to other wall dir)
                        double dot = Math.Abs(pf.FaceNormal.DotProduct(otherDir));
                        if (dot < 0.99) continue;

                        // Face must be close to the intersection point
                        if (pf.Origin.DistanceTo(intersectionPt) < 1.5) // within 1.5ft
                        {
                            if (pf.Reference != null)
                            {
                                refs.Add(pf.Reference);
                                break; // one face per intersecting wall is enough
                            }
                        }
                    }
                    if (refs.Count > 0) break;
                }
            }

            return refs;
        }

        // ------------------------------------------------------------------
        //  6.  Reference extraction — grids
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns References for all grids whose line intersects the target wall
        /// within the wall's extent.
        ///
        /// The Revit API does not expose a direct GetReference() on Grid.
        /// Instead we extract a Reference via geometry traversal with
        /// ComputeReferences = true — same pattern used for wall faces.
        /// </summary>
        public static List<Reference> GetGridReferences(
            Wall targetWall, List<Grid> allGrids)
        {
            var refs = new List<Reference>();

            var targetLine = GetWallCenterLine(targetWall);
            if (targetLine == null) return refs;

            XYZ tStart = targetLine.GetEndPoint(0);
            XYZ tEnd = targetLine.GetEndPoint(1);
            double len = tStart.DistanceTo(tEnd);

            var opts = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            foreach (Grid grid in allGrids)
            {
                Curve gridCurve = grid.Curve;

                var result = targetLine.Intersect(gridCurve, out IntersectionResultArray ira);
                if (result != SetComparisonResult.Overlap || ira == null || ira.Size == 0)
                    continue;

                XYZ intersectionPt = ira.get_Item(0).XYZPoint;

                // Confirm intersection is within the wall extent (not just line extension)
                double u = (intersectionPt - tStart)
                    .DotProduct((tEnd - tStart).Normalize());

                if (u < -0.01 || u > len + 0.01) continue;

                // Extract reference via geometry traversal
                Reference gridRef = null;
                try
                {
                    var geom = grid.get_Geometry(opts);
                    foreach (GeometryObject obj in geom)
                    {
                        // Grid geometry is typically a Line/Curve object
                        if (obj is Line line && line.Reference != null)
                        {
                            gridRef = line.Reference;
                            break;
                        }
                        // Sometimes wrapped in a GeometryInstance
                        if (obj is GeometryInstance gi)
                        {
                            foreach (GeometryObject subObj in gi.GetInstanceGeometry())
                            {
                                if (subObj is Line subLine && subLine.Reference != null)
                                {
                                    gridRef = subLine.Reference;
                                    break;
                                }
                            }
                        }
                        if (gridRef != null) break;
                    }
                }
                catch { /* skip this grid if geometry fails */ }

                if (gridRef != null)
                    refs.Add(gridRef);
            }

            return refs;
        }

        // ------------------------------------------------------------------
        //  7.  Reference ordering — sort references along wall axis
        // ------------------------------------------------------------------

        /// <summary>
        /// Sorts a list of references by their projected position along the wall direction.
        /// NewDimension() doesn't require sorted refs, but ordered refs produce
        /// clean dimension strings that read left-to-right.
        /// Uses a lightweight probe via the Reference's element geometry.
        /// Falls back to original order if projection cannot be computed.
        /// </summary>
        public static ReferenceArray OrderReferencesAlongWall(
            List<Reference> references, Wall wall, Document doc)
        {
            var centerLine = GetWallCenterLine(wall);
            XYZ wallStart = centerLine?.GetEndPoint(0) ?? XYZ.Zero;
            XYZ wallDir = GetWallDirection(wall);

            var sorted = references
                .Select(r =>
                {
                    double u = 0;
                    try
                    {
                        // Get the element owning this reference and probe its location
                        Element el = doc.GetElement(r.ElementId);
                        if (el?.Location is LocationPoint lp)
                            u = (lp.Point - wallStart).DotProduct(wallDir);
                        else if (el?.Location is LocationCurve lc)
                        {
                            XYZ mid = lc.Curve.Evaluate(0.5, true);
                            u = (mid - wallStart).DotProduct(wallDir);
                        }
                    }
                    catch { /* keep u = 0 */ }
                    return (u, r);
                })
                .OrderBy(t => t.u)
                .Select(t => t.r)
                .ToList();

            var ra = new ReferenceArray();
            foreach (var r in sorted)
                ra.Append(r);
            return ra;
        }

        // ------------------------------------------------------------------
        //  8.  Dimension line geometry
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds the Line that defines where the dimension string sits.
        /// Offset from the wall face by request.OffsetDistance in the determined direction.
        /// </summary>
        public static Line BuildDimensionLine(Wall wall, XYZ offsetDirection,
            double offsetDistance, Document doc, View view)
        {
            var centerLine = GetWallCenterLine(wall);
            if (centerLine == null) return null;

            XYZ start = centerLine.GetEndPoint(0);
            XYZ end = centerLine.GetEndPoint(1);

            // Offset outward by half the wall thickness + the requested distance
            double halfThickness = wall.Width / 2.0;
            double totalOffset = halfThickness + offsetDistance;

            XYZ offset = offsetDirection * totalOffset;

            XYZ dimStart = new XYZ(start.X + offset.X, start.Y + offset.Y, start.Z);
            XYZ dimEnd = new XYZ(end.X + offset.X, end.Y + offset.Y, end.Z);

            // Extend slightly beyond wall endpoints so string doesn't crowd end caps
            XYZ wallDir = GetWallDirection(wall);
            double extend = 1.0; // 1 ft extension
            dimStart -= wallDir * extend;
            dimEnd += wallDir * extend;

            return Line.CreateBound(dimStart, dimEnd);
        }
    }
}
