using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace A49AIRevitAssistant.Executor.Commands
{
    using DimStrategies;

    /// <summary>
    /// Two-pass automatic dimensioning.
    ///
    /// PASS 1 — Along each wall axis:
    ///   One dimension string per collinear wall group measuring
    ///   opening positions along the wall length.
    ///
    /// PASS 2 — One perpendicular string per direction:
    ///   Groups all walls by direction (horizontal / vertical / etc).
    ///   For each direction group, collects all wall side-face references
    ///   and perpendicular grid references, sorts them by position, and
    ///   creates ONE dimension string on the left/bottom side showing
    ///   face-to-face distances between consecutive walls and grids.
    /// </summary>
    public class AutoDimCommand
    {
        private readonly Document _doc;
        private readonly UIDocument _uiDoc;

        public AutoDimCommand(Document doc, UIDocument uiDoc)
        {
            _doc = doc;
            _uiDoc = uiDoc;
        }

        // ======================================================================
        //  Execute
        // ======================================================================

        public string Execute(string jsonPayload)
        {
            JObject payload;
            try { payload = JObject.Parse(jsonPayload); }
            catch (Exception ex) { return Error($"Invalid JSON: {ex.Message}"); }

            try
            {
                DimSettings settings = ParseSettings(payload);

                var viewIdTokens = payload["view_ids"] as JArray;
                if (viewIdTokens == null || viewIdTokens.Count == 0)
                    return Error("No view_ids provided.");

                var viewIds = viewIdTokens
                    .Select(t => { long.TryParse(t.ToString(), out long v); return v; })
                    .Where(v => v != 0).ToList();

                if (viewIds.Count == 0) return Error("No valid view_ids.");

                DimensionType dimType = ResolveDimensionType(
                    payload.Value<string>("dim_type_name") ?? "");
                if (dimType == null) return Error("No linear DimensionType found.");

                int totalSucceeded = 0, totalSkipped = 0, totalFailed = 0, totalWalls = 0;
                var allSkipReasons = new List<string>();
                var allErrors = new List<string>();

                foreach (long viewIdLong in viewIds)
                {
                    var view = _doc.GetElement(new ElementId(viewIdLong)) as View;
                    if (view == null)
                    { allErrors.Add($"View {viewIdLong}: not found."); totalFailed++; continue; }

                    var planTypes = new[] {
                        ViewType.FloorPlan, ViewType.CeilingPlan,
                        ViewType.EngineeringPlan, ViewType.AreaPlan };
                    if (!planTypes.Contains(view.ViewType))
                    { allSkipReasons.Add($"'{view.Name}': not a plan."); totalSkipped++; continue; }

                    List<Wall> allWalls = CollectWallsInView(view);
                    List<Grid> allGrids = settings.IncludeGrids
                        ? CollectGridsInView(view) : new List<Grid>();

                    if (allWalls.Count == 0)
                    { allSkipReasons.Add($"'{view.Name}': no walls."); totalSkipped++; continue; }

                    totalWalls += allWalls.Count;

                    var strategy = new WallDimStrategy();
                    int succeeded = 0, skipped = 0, failed = 0;

                    using (var tx = new Transaction(
                        _doc, $"Vella AI — Auto Dimension: {view.Name}"))
                    {
                        tx.Start();

                        // ── PASS 1: Along-wall strings ───────────────────────
                        foreach (List<Wall> group in GroupCollinearWalls(allWalls))
                        {
                            Wall primary = group
                                .OrderByDescending(w =>
                                    DimHelpers.GetWallCenterLine(w)?.Length ?? 0)
                                .First();

                            if (!strategy.CanDimension(primary, view))
                            { skipped += group.Count; continue; }

                            var request = new DimRequest
                            {
                                TargetView = view,
                                WallIds = group.Select(w => w.Id).ToList(),
                                IncludeOpenings = settings.IncludeOpenings,
                                IncludeGrids = settings.IncludeGrids,
                                OffsetDistance = settings.OffsetDistance,
                                SmartExteriorPlacement = settings.SmartExteriorPlacement,
                            };

                            var context = new DimContext
                            {
                                Document = _doc,
                                Request = request,
                                AllWallsInView = allWalls,
                                AllGridsInView = allGrids,
                                LinearDimensionType = dimType,
                            };

                            DimResult r = group.Count == 1
                                ? strategy.Dimension(primary, context)
                                : DimensionCollinearGroup(group, primary, context);

                            if (r.Success) succeeded++;
                            else if (!string.IsNullOrEmpty(r.ErrorMessage))
                            { failed++; allErrors.Add($"P1 {primary.Id}: {r.ErrorMessage}"); }
                            else
                            { skipped += group.Count; allSkipReasons.Add($"P1 {primary.Id}: {r.SkipReason}"); }
                        }

                        // ── PASS 2: Perpendicular location strings ────────────
                        var p2 = Pass2(allWalls, allGrids, view, dimType, settings);
                        succeeded += p2.s; skipped += p2.sk; failed += p2.f;
                        allErrors.AddRange(p2.errors);
                        allSkipReasons.AddRange(p2.skips);

                        if (failed > 0 && succeeded == 0) tx.RollBack();
                        else tx.Commit();
                    }

                    totalSucceeded += succeeded;
                    totalSkipped += skipped;
                    totalFailed += failed;
                }

                return JsonConvert.SerializeObject(new
                {
                    status = "success",
                    command = "auto_dim",
                    views_processed = viewIds.Count,
                    total_walls = totalWalls,
                    dimensioned = totalSucceeded,
                    skipped = totalSkipped,
                    failed = totalFailed,
                    skip_reasons = allSkipReasons,
                    errors = allErrors,
                });
            }
            catch (Exception ex) { return Error($"Unexpected: {ex.Message}"); }
        }

        // ======================================================================
        //  PASS 2 — One perpendicular string per wall-direction group
        // ======================================================================
        //
        //  Logic (simple and explicit):
        //
        //  For each unique wall direction D (horizontal, vertical, etc.):
        //
        //    1. Collect every wall running in direction D.
        //       For each wall get its TWO side-face references and their
        //       positions measured along the wall NORMAL (perpendicular axis).
        //
        //    2. Collect every grid whose line is perpendicular to D
        //       (i.e. runs in the normal direction). Get its reference and
        //       position along the normal axis.
        //
        //    3. Merge all into one sorted list, deduplicate by position.
        //
        //    4. Create ONE NewDimension with all refs, placed on the LEFT/BOTTOM
        //       side (minimum X for vertical walls, minimum Y for horizontal walls).
        //
        //    The dimension line runs along direction D, placed at the leftmost/
        //    bottommost position minus the user's offset distance.

        private (int s, int sk, int f, List<string> errors, List<string> skips)
            Pass2(List<Wall> allWalls, List<Grid> allGrids,
                  View view, DimensionType dimType, DimSettings settings)
        {
            int s = 0, sk = 0, f = 0;
            var errors = new List<string>();
            var skips = new List<string>();

            var geomOpts = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine,
            };

            // ── Identify unique wall directions ───────────────────────────────

            var dirs = new List<XYZ>();
            foreach (Wall w in allWalls)
            {
                XYZ d = CanonDir(DimHelpers.GetWallDirection(w));
                if (!dirs.Any(x => Math.Abs(x.DotProduct(d)) > 0.99))
                    dirs.Add(d);
            }

            foreach (XYZ dir in dirs)
            {
                // The axis perpendicular to this wall direction
                XYZ normal = new XYZ(-dir.Y, dir.X, 0).Normalize();

                // ── Collect all walls in this direction ───────────────────

                List<Wall> dWalls = allWalls
                    .Where(w => Math.Abs(
                        CanonDir(DimHelpers.GetWallDirection(w)).DotProduct(dir)) > 0.99)
                    .ToList();

                // ── Build sorted reference list ───────────────────────────
                // Each entry: (Reference, position-along-normal, label)
                // Position = dot product of face centroid with normal vector.

                var entries = new List<RefEntry>();

                // Wall side-face references — ONE face per wall only.
                // For each wall collect both side faces, then keep only the
                // face with the LOWER pos (the outer face, towards -normal direction).
                // This prevents wall thickness appearing as a dimension segment.
                foreach (Wall wall in dWalls)
                {
                    XYZ wallNormal = DimHelpers.GetWallNormal(wall);

                    // Collect both side faces for this wall
                    var wallFaces = new List<RefEntry>();
                    foreach (Solid solid in
                        DimHelpers.CollectSolids(wall.get_Geometry(geomOpts)))
                    {
                        foreach (Face face in solid.Faces)
                        {
                            if (!(face is PlanarFace pf)) continue;
                            if (pf.Reference == null) continue;
                            if (Math.Abs(pf.FaceNormal.DotProduct(wallNormal)) < 0.95)
                                continue;

                            XYZ cen = DimHelpers.FaceCentroid(pf);
                            double pos = cen.DotProduct(normal);
                            wallFaces.Add(new RefEntry
                            {
                                Ref = pf.Reference,
                                Pos = pos,
                                Kind = "wall",
                                Pt = cen,
                            });
                        }
                    }

                    if (wallFaces.Count == 0) continue;

                    // Pick the face with the MINIMUM pos (the left/bottom face)
                    // so the dimension measures from the outer face of each wall.
                    RefEntry outerFace = wallFaces.OrderBy(f => f.Pos).First();

                    // Only add if no existing entry within 50mm of this position
                    if (!entries.Any(e => e.Kind == "wall" &&
                            Math.Abs(e.Pos - outerFace.Pos) < 0.164))
                        entries.Add(outerFace);
                }

                if (entries.Count == 0) { sk++; continue; }

                // Grid references — grids perpendicular to this wall direction
                foreach (Grid grid in allGrids)
                {
                    try
                    {
                        XYZ gDir = (grid.Curve.GetEndPoint(1) -
                                    grid.Curve.GetEndPoint(0)).Normalize();
                        if (Math.Abs(gDir.DotProduct(dir)) > 0.15) continue;

                        Reference gRef = GetGridRef(grid, geomOpts);
                        if (gRef == null) continue;

                        XYZ gPt = grid.Curve.Evaluate(0.5, true);
                        double pos = gPt.DotProduct(normal);

                        if (!entries.Any(e => Math.Abs(e.Pos - pos) < 0.05))
                            entries.Add(new RefEntry
                            {
                                Ref = gRef,
                                Pos = pos,
                                Kind = "grid",
                                Pt = gPt,
                            });
                    }
                    catch { }
                }

                // FIX: Keep only ONE face per wall — the face closest to minPos
                // (the left/bottom side). This prevents wall thickness appearing
                // as a zero or tiny segment in the dimension string.
                // Strategy: for faces that are within wall-thickness distance of
                // each other (< 0.7ft ~200mm), keep only the one with lower Pos.
                var wallEntries = entries.Where(e => e.Kind == "wall")
                                         .OrderBy(e => e.Pos).ToList();
                var gridEntries = entries.Where(e => e.Kind == "grid").ToList();

                var filteredWall = new List<RefEntry>();
                foreach (RefEntry we in wallEntries)
                {
                    // Skip if we already have a wall face within 200mm of this one
                    // (same wall, both faces) — keep the first (lower pos = outer face)
                    if (!filteredWall.Any(existing =>
                            Math.Abs(existing.Pos - we.Pos) < 0.66))
                        filteredWall.Add(we);
                }

                var allEntries = filteredWall.Concat(gridEntries)
                                             .OrderBy(e => e.Pos).ToList();

                if (allEntries.Count < 2)
                { sk++; skips.Add("P2 dir: <2 entries after wall-face filter."); continue; }

                // ── Build ReferenceArray ──────────────────────────────────

                var ra = new ReferenceArray();
                foreach (RefEntry e in allEntries)
                    ra.Append(e.Ref);

                // ── Position the dimension line ───────────────────────────
                //
                // KEY INSIGHT: For perpendicular location strings, the dim line
                // must run along the NORMAL direction (perpendicular to wall),
                // NOT along the wall direction.
                //
                // For horizontal walls (dir=+X, normal=+Y):
                //   References are at different Y positions (grids + wall faces)
                //   → dim line must be VERTICAL (run in Y / normal direction)
                //   → dim line is positioned to the LEFT of the wall group
                //     (offset in -dir direction from the wall group left edge)
                //
                // For vertical walls (dir=+Y, normal=-X or +X):
                //   References are at different X positions
                //   → dim line must be HORIZONTAL (run in X / normal direction)
                //   → dim line positioned above or below the wall group
                //
                // Dim line:
                //   - Runs from minPos to maxPos along 'normal' direction
                //   - Positioned at: (left edge of wall group) - offsetDistance
                //     in the 'dir' direction

                double minRefPos = allEntries.Min(e => e.Pos);
                double maxRefPos = allEntries.Max(e => e.Pos);

                // Find extent of wall group along 'dir'
                double wallGroupDirMin = double.MaxValue;
                double wallGroupDirMax = double.MinValue;
                foreach (Wall wall in dWalls)
                {
                    var cl = DimHelpers.GetWallCenterLine(wall);
                    if (cl == null) continue;
                    double u0 = cl.GetEndPoint(0).DotProduct(dir);
                    double u1 = cl.GetEndPoint(1).DotProduct(dir);
                    if (u0 < wallGroupDirMin) wallGroupDirMin = u0;
                    if (u1 < wallGroupDirMin) wallGroupDirMin = u1;
                    if (u0 > wallGroupDirMax) wallGroupDirMax = u0;
                    if (u1 > wallGroupDirMax) wallGroupDirMax = u1;
                }

                if (wallGroupDirMax == double.MinValue) { sk++; continue; }

                // ── Position the dim line at InsetDistance from the min edge ──
                //
                // The dim line runs along 'normal'. It is positioned at
                // InsetDistance from the left/bottom edge of the wall group
                // along 'dir'. This places it visually inside the building
                // at a consistent, configurable offset from one side.
                //
                // Example: horizontal walls span X=0 to X=9, inset=1000mm
                //   → dimDirPos = 0 + 1000/304.8 = ~3.28ft from left edge
                //   → dim line is a vertical line slightly inside left wall

                double dimDirPos = wallGroupDirMin + settings.InsetDistance;

                // Use a representative wall for Z height
                var repCl2 = DimHelpers.GetWallCenterLine(dWalls[0]);
                if (repCl2 == null) { sk++; continue; }
                double z = repCl2.GetEndPoint(0).Z;

                // Pad the dim line slightly beyond the ref extents
                const double pad = 0.33;
                double lineStart = minRefPos - pad;
                double lineEnd = maxRefPos + pad;

                // Build the line: it runs along 'normal', positioned at dimDirPos along 'dir'
                // World point = dir * dimDirPos + normal * normalPos
                XYZ lp1 = new XYZ(
                    dir.X * dimDirPos + normal.X * lineStart,
                    dir.Y * dimDirPos + normal.Y * lineStart,
                    z);
                XYZ lp2 = new XYZ(
                    dir.X * dimDirPos + normal.X * lineEnd,
                    dir.Y * dimDirPos + normal.Y * lineEnd,
                    z);

                if (lp1.DistanceTo(lp2) < 0.1) { sk++; continue; }

                Line dimLine;
                try { dimLine = Line.CreateBound(lp1, lp2); }
                catch (Exception ex)
                { sk++; skips.Add($"P2 CreateBound: {ex.Message}"); continue; }

                try
                {
                    Dimension dim = _doc.Create.NewDimension(
                        view, dimLine, ra, dimType);
                    if (dim != null) s++;
                    else { sk++; skips.Add("P2: NewDimension null."); }
                }
                catch (Exception ex)
                { f++; errors.Add($"P2 NewDimension: {ex.Message}"); }
            }

            return (s, sk, f, errors, skips);
        }

        // ── Helper classes ────────────────────────────────────────────────────

        private class RefEntry
        {
            public Reference Ref { get; set; }
            public double Pos { get; set; }
            public string Kind { get; set; }
            public XYZ Pt { get; set; }
        }

        // ── Canonical direction: always points to +X or +Y half-plane ────────

        private static XYZ CanonDir(XYZ dir)
        {
            if (dir.X < -0.01 || (Math.Abs(dir.X) < 0.01 && dir.Y < 0))
                return new XYZ(-dir.X, -dir.Y, 0).Normalize();
            return new XYZ(dir.X, dir.Y, 0).Normalize();
        }

        // ── Get grid line reference ───────────────────────────────────────────

        private static Reference GetGridRef(Grid grid, Options opts)
        {
            try
            {
                foreach (GeometryObject obj in grid.get_Geometry(opts))
                {
                    if (obj is Line ln && ln.Reference != null) return ln.Reference;
                    if (obj is GeometryInstance gi)
                        foreach (GeometryObject sub in gi.GetInstanceGeometry())
                            if (sub is Line sl && sl.Reference != null)
                                return sl.Reference;
                }
            }
            catch { }
            return null;
        }

        // ======================================================================
        //  PASS 1 helpers
        // ======================================================================

        private DimResult DimensionCollinearGroup(
            List<Wall> group, Wall primary, DimContext context)
        {
            var doc = context.Document;
            var request = context.Request;
            var view = request.TargetView;

            var gridRefs = new List<TaggedRef>();
            if (request.IncludeGrids && context.AllGridsInView.Count > 0)
                gridRefs = DimHelpers.GetGridRefs(primary, context.AllGridsInView);

            var allRefs = new List<TaggedRef>();
            foreach (Wall wall in group)
            {
                var (startCap, endCap) = DimHelpers.GetWallEndCapRefs(wall);
                if (startCap == null || endCap == null) continue;

                List<TaggedRef> baseRefs = gridRefs.Count > 0
                    ? DimHelpers.MergeEndCapsWithGrids(startCap, endCap, gridRefs, 1.0)
                    : new List<TaggedRef> { startCap, endCap };

                allRefs.AddRange(baseRefs);

                if (request.IncludeOpenings)
                    allRefs.AddRange(DimHelpers.GetOpeningEdgeRefs(
                        wall, doc, startCap.U, endCap.U));
            }

            if (allRefs.Count < 2)
                return DimResult.Skipped("Collinear group: <2 refs.");

            ReferenceArray refArray = DimHelpers.BuildOrderedRefArray(allRefs);
            if (refArray.Size < 2)
                return DimResult.Skipped("Collinear group: <2 after dedup.");

            XYZ offsetDir = DimHelpers.GetDimLineOffsetDirection(
                primary, doc, view, request.SmartExteriorPlacement);

            Line dimLine = DimHelpers.BuildDimensionLine(
                primary, offsetDir, request.OffsetDistance,
                allRefs.Min(r => r.U), allRefs.Max(r => r.U));

            if (dimLine == null)
                return DimResult.Skipped("Collinear group: dimLine null.");

            try
            {
                Dimension dim = doc.Create.NewDimension(
                    view, dimLine, refArray, context.LinearDimensionType);
                return dim != null
                    ? DimResult.Succeeded(dim, refArray.Size)
                    : DimResult.Skipped("Collinear group: NewDimension null.");
            }
            catch (Exception ex)
            { return DimResult.Failed($"Collinear group: {ex.Message}"); }
        }

        private List<List<Wall>> GroupCollinearWalls(List<Wall> walls)
        {
            var groups = new List<List<Wall>>();
            var assigned = new HashSet<ElementId>();

            foreach (Wall wall in walls)
            {
                if (assigned.Contains(wall.Id)) continue;
                var cl = DimHelpers.GetWallCenterLine(wall);
                if (cl == null)
                {
                    groups.Add(new List<Wall> { wall });
                    assigned.Add(wall.Id);
                    continue;
                }

                XYZ origin = cl.GetEndPoint(0);
                XYZ wallDir = (cl.GetEndPoint(1) - origin).Normalize();
                var group = new List<Wall> { wall };
                assigned.Add(wall.Id);

                foreach (Wall other in walls)
                {
                    if (assigned.Contains(other.Id)) continue;
                    var oCl = DimHelpers.GetWallCenterLine(other);
                    if (oCl == null) continue;
                    XYZ oDir = (oCl.GetEndPoint(1) - oCl.GetEndPoint(0)).Normalize();
                    if (Math.Abs(wallDir.DotProduct(oDir)) < 0.99) continue;
                    XYZ delta = oCl.GetEndPoint(0) - origin;
                    XYZ perp = delta - wallDir * delta.DotProduct(wallDir);
                    if (perp.GetLength() > 0.5) continue;
                    group.Add(other);
                    assigned.Add(other.Id);
                }

                groups.Add(group);
            }

            return groups;
        }

        // ======================================================================
        //  Utilities
        // ======================================================================

        private class DimSettings
        {
            public bool IncludeOpenings { get; set; }
            public bool IncludeGrids { get; set; }
            public double OffsetDistance { get; set; }
            public double InsetDistance { get; set; }
            public bool SmartExteriorPlacement { get; set; }
        }

        private static DimSettings ParseSettings(JObject p) => new DimSettings
        {
            IncludeOpenings = p.Value<bool?>("include_openings") ?? true,
            IncludeGrids = p.Value<bool?>("include_grids") ?? true,
            OffsetDistance = (p.Value<double?>("offset_mm") ?? 800.0) / 304.8,
            InsetDistance = (p.Value<double?>("inset_mm") ?? 1000.0) / 304.8,
            SmartExteriorPlacement = p.Value<bool?>("smart_exterior") ?? true,
        };

        private List<Wall> CollectWallsInView(View view) =>
            new FilteredElementCollector(_doc, view.Id)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .Where(w => w.Location is LocationCurve lc && lc.Curve is Line)
                .ToList();

        private List<Grid> CollectGridsInView(View view) =>
            new FilteredElementCollector(_doc, view.Id)
                .OfClass(typeof(Grid))
                .WhereElementIsNotElementType()
                .Cast<Grid>()
                .ToList();

        private DimensionType ResolveDimensionType(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                var m = new FilteredElementCollector(_doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .FirstOrDefault(dt =>
                        dt.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (m != null) return m;
            }
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .FirstOrDefault(dt => dt.StyleType == DimensionStyleType.Linear);
        }

        private static string Error(string msg) =>
            JsonConvert.SerializeObject(new
            {
                status = "error",
                command = "auto_dim",
                message = msg
            });
    }
}
