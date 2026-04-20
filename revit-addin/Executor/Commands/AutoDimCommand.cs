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

    public class AutoDimCommand
    {
        private readonly Document _doc;
        private readonly UIDocument _uiDoc;

        public AutoDimCommand(Document doc, UIDocument uiDoc)
        {
            _doc = doc;
            _uiDoc = uiDoc;
        }

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

                    int succeeded = 0, skipped = 0, failed = 0;

                    using (var tx = new Transaction(
                        _doc, $"Vella AI — Auto Dimension: {view.Name}"))
                    {
                        tx.Start();

                        // PASS 1: Along-wall strings
                        foreach (List<Wall> group in GroupCollinearWalls(allWalls))
                        {
                            Wall primary = group
                                .OrderByDescending(w =>
                                    DimHelpers.GetWallCenterLine(w)?.Length ?? 0)
                                .First();

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
                                ? DimensionSingleWall(primary, context)
                                : DimensionCollinearGroup(group, primary, context);

                            if (r.Success) succeeded++;
                            else if (!string.IsNullOrEmpty(r.ErrorMessage))
                            { failed++; allErrors.Add($"P1 {primary.Id}: {r.ErrorMessage}"); }
                            else
                            { skipped += group.Count; allSkipReasons.Add($"P1 {primary.Id}: {r.SkipReason}"); }
                        }

                        // PASS 2: Perpendicular location strings
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
        //  PASS 1 helpers
        // ======================================================================

        private DimResult DimensionSingleWall(Wall wall, DimContext context)
        {
            var doc = context.Document;
            var request = context.Request;
            var view = request.TargetView;

            var gridRefs = new List<TaggedRef>();
            if (request.IncludeGrids && context.AllGridsInView.Count > 0)
                gridRefs = DimHelpers.GetGridRefs(wall, context.AllGridsInView);

            var (startCap, endCap) = DimHelpers.GetWallEndCapRefs(wall);
            if (startCap == null || endCap == null)
                return DimResult.Skipped("Wall: end caps null.");

            List<TaggedRef> baseRefs = gridRefs.Count > 0
                ? DimHelpers.MergeEndCapsWithGrids(startCap, endCap, gridRefs, 10.0)
                : new List<TaggedRef> { startCap, endCap };

            if (request.IncludeOpenings)
            {
                var openingRefs = DimHelpers.GetOpeningEdgeRefs(wall, doc, startCap.U, endCap.U);
                baseRefs.AddRange(openingRefs);
            }

            if (baseRefs.Count < 2)
                return DimResult.Skipped("Wall: <2 refs.");

            ReferenceArray refArray = DimHelpers.BuildOrderedRefArray(baseRefs);
            if (refArray.Size < 2)
                return DimResult.Skipped("Wall: <2 after dedup.");

            XYZ offsetDir = DimHelpers.GetDimLineOffsetDirection(
                wall, doc, view, request.SmartExteriorPlacement);

            Line dimLine = DimHelpers.BuildDimensionLine(
                wall, offsetDir, request.OffsetDistance, baseRefs);

            if (dimLine == null)
                return DimResult.Skipped("Wall: dimLine null.");

            try
            {
                Dimension dim = doc.Create.NewDimension(
                    view, dimLine, refArray, context.LinearDimensionType);
                return dim != null
                    ? DimResult.Succeeded(dim, refArray.Size)
                    : DimResult.Skipped("Wall: NewDimension null.");
            }
            catch (Exception ex)
            { return DimResult.Failed($"Wall: {ex.Message}"); }
        }

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
                    ? DimHelpers.MergeEndCapsWithGrids(startCap, endCap, gridRefs, 10.0)
                    : new List<TaggedRef> { startCap, endCap };

                allRefs.AddRange(baseRefs);

                if (request.IncludeOpenings)
                    allRefs.AddRange(DimHelpers.GetOpeningEdgeRefs(
                        wall, doc, startCap.U, endCap.U));
            }

            if (allRefs.Count < 2)
                return DimResult.Skipped("Collinear group: <2 refs.");

            string debugMsg = $"Final references for wall group:\n";
            foreach (var refItem in allRefs.OrderBy(r => r.U))
            {
                debugMsg += $"  {refItem.Kind} at U={refItem.U:F2} ft (ID: {refItem.SourceId})\n";
            }
            TaskDialog.Show("Dimension References", debugMsg);

            ReferenceArray refArray = DimHelpers.BuildOrderedRefArray(allRefs);
            if (refArray.Size < 2)
                return DimResult.Skipped("Collinear group: <2 after dedup.");

            // Add debug to see what made it through deduplication
            string finalDebug = $"After deduplication ({refArray.Size} references):\n";
            var finalRefs = allRefs.OrderBy(r => r.U).ToList();
            for (int i = 0; i < finalRefs.Count && i < 20; i++)
            {
                finalDebug += $"  {finalRefs[i].Kind} at U={finalRefs[i].U:F2}\n";
            }
            TaskDialog.Show("Final References After Dedup", finalDebug);


            XYZ offsetDir = DimHelpers.GetDimLineOffsetDirection(
                primary, doc, view, request.SmartExteriorPlacement);

            Line dimLine = DimHelpers.BuildDimensionLine(
                primary, offsetDir, request.OffsetDistance, allRefs);

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
        //  PASS 2 — Perpendicular location strings
        // ======================================================================

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

            var dirs = new List<XYZ>();
            foreach (Wall w in allWalls)
            {
                XYZ d = CanonDir(DimHelpers.GetWallDirection(w));
                if (!dirs.Any(x => Math.Abs(x.DotProduct(d)) > 0.99))
                    dirs.Add(d);
            }

            foreach (XYZ dir in dirs)
            {
                XYZ normal = new XYZ(-dir.Y, dir.X, 0).Normalize();

                List<Wall> dWalls = allWalls
                    .Where(w => Math.Abs(
                        CanonDir(DimHelpers.GetWallDirection(w)).DotProduct(dir)) > 0.99)
                    .ToList();

                var entries = new List<RefEntry>();

                foreach (Wall wall in dWalls)
                {
                    XYZ wallNormal = DimHelpers.GetWallNormal(wall);

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

                    RefEntry outerFace = wallFaces.OrderBy(f => f.Pos).First();

                    if (!entries.Any(e => e.Kind == "wall" &&
                            Math.Abs(e.Pos - outerFace.Pos) < 0.164))
                        entries.Add(outerFace);
                }

                if (entries.Count == 0) { sk++; continue; }

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

                var wallEntries = entries.Where(e => e.Kind == "wall")
                                         .OrderBy(e => e.Pos).ToList();
                var gridEntries = entries.Where(e => e.Kind == "grid").ToList();

                var filteredWall = new List<RefEntry>();
                foreach (RefEntry we in wallEntries)
                {
                    if (!filteredWall.Any(existing =>
                            Math.Abs(existing.Pos - we.Pos) < 0.66))
                        filteredWall.Add(we);
                }

                var allEntries = filteredWall.Concat(gridEntries)
                                             .OrderBy(e => e.Pos).ToList();

                if (allEntries.Count < 2)
                { sk++; skips.Add("P2 dir: <2 entries after wall-face filter."); continue; }

                var ra = new ReferenceArray();
                foreach (RefEntry e in allEntries)
                    ra.Append(e.Ref);

                double minRefPos = allEntries.Min(e => e.Pos);
                double maxRefPos = allEntries.Max(e => e.Pos);

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

                double dimDirPos = wallGroupDirMin + settings.InsetDistance;

                var repCl2 = DimHelpers.GetWallCenterLine(dWalls[0]);
                if (repCl2 == null) { sk++; continue; }
                double z = repCl2.GetEndPoint(0).Z;

                const double pad = 0.33;
                double lineStart = minRefPos - pad;
                double lineEnd = maxRefPos + pad;

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

        private class RefEntry
        {
            public Reference Ref { get; set; }
            public double Pos { get; set; }
            public string Kind { get; set; }
            public XYZ Pt { get; set; }
        }

        private static XYZ CanonDir(XYZ dir)
        {
            if (dir.X < -0.01 || (Math.Abs(dir.X) < 0.01 && dir.Y < 0))
                return new XYZ(-dir.X, -dir.Y, 0).Normalize();
            return new XYZ(dir.X, dir.Y, 0).Normalize();
        }

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