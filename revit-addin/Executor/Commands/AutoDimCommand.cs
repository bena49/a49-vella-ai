// ============================================================================
// A49AIRevitAssistant/Executor/Commands/AutoDimCommand.cs
// ============================================================================
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

                    using (var tx = new Transaction(_doc, $"Vella AI — Auto Dimension: {view.Name}"))
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
                        // Layer 1: Overall/Total Dimension (Outermost)
                        if (settings.IncludeTotalString)
                        {
                            var pTotal = Pass2(allWalls, allGrids, view, dimType, settings, true, false);
                            succeeded += pTotal.s;
                        }

                        // Layer 2: Grid-to-Grid Only (Middle)
                        if (settings.IncludeGridsOnlyString) // Ensure this is in your DimSettings
                        {
                            var pGrids = Pass2(allWalls, allGrids, view, dimType, settings, false, true);
                            succeeded += pGrids.s;
                        }

                        // Layer 3: Detail Perimeter (Innermost: Openings + Walls + Grids)
                        var p2 = Pass2(allWalls, allGrids, view, dimType, settings, false, false);
                        succeeded += p2.s; skipped += p2.sk; failed += p2.f;
                        allErrors.AddRange(p2.errors);
                        allSkipReasons.AddRange(p2.skips);

                        if (failed > 0 && succeeded == 0) tx.RollBack();
                        else tx.Commit();
                    }

                    totalSucceeded += (succeeded > 0) ? 1 : 0; // Count 1 success per view, not per string
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

            ReferenceArray refArray = DimHelpers.BuildOrderedRefArray(allRefs);
            if (refArray.Size < 2)
                return DimResult.Skipped("Collinear group: <2 after dedup.");

            // ========== INSERT THE FILTER CODE RIGHT HERE ==========
            // Filter out tiny segments (0.2 ft wall thickness)
            const double minSegmentLength = 0.5; // 0.5 ft = 150mm minimum segment
            var filteredRefs = new List<Reference>();

            for (int i = 0; i < refArray.Size; i++)
            {
                // Always keep first and last reference
                if (i == 0 || i == refArray.Size - 1)
                {
                    filteredRefs.Add(refArray.get_Item(i));
                    continue;
                }

                // Calculate U positions (need to get from allRefs)
                // Find matching references in allRefs to get U positions
                Reference currentRef = refArray.get_Item(i);
                Reference prevRef = refArray.get_Item(i - 1);
                Reference nextRef = refArray.get_Item(i + 1);

                // Find corresponding TaggedRef to get U positions
                var currentTagged = allRefs.FirstOrDefault(r => r.Ref == currentRef);
                var prevTagged = allRefs.FirstOrDefault(r => r.Ref == prevRef);
                var nextTagged = allRefs.FirstOrDefault(r => r.Ref == nextRef);

                if (currentTagged != null && prevTagged != null && nextTagged != null)
                {
                    double distToPrev = currentTagged.U - prevTagged.U;
                    double distToNext = nextTagged.U - currentTagged.U;

                    // Keep if both adjacent segments are large enough
                    // OR if this is a grid reference (grids are important)
                    if (distToPrev >= minSegmentLength && distToNext >= minSegmentLength)
                    {
                        filteredRefs.Add(currentRef);
                    }
                    else if (currentTagged.Kind == "grid")
                    {
                        // Always keep grid references
                        filteredRefs.Add(currentRef);
                    }
                    // Otherwise skip (removes 0.2 wall thickness segments)
                }
                else
                {
                    filteredRefs.Add(currentRef);
                }
            }

            // Rebuild reference array with filtered references
            var finalRefArray = new ReferenceArray();
            foreach (var r in filteredRefs)
                finalRefArray.Append(r);
            // ========== END OF FILTER CODE ==========

            XYZ offsetDir = DimHelpers.GetDimLineOffsetDirection(
                primary, doc, view, request.SmartExteriorPlacement);

            Line dimLine = DimHelpers.BuildDimensionLine(
                primary, offsetDir, request.OffsetDistance, allRefs);

            if (dimLine == null)
                return DimResult.Skipped("Collinear group: dimLine null.");

            try
            {
                // USE finalRefArray instead of refArray
                Dimension dim = doc.Create.NewDimension(
                    view, dimLine, finalRefArray, context.LinearDimensionType);
                return dim != null
                    ? DimResult.Succeeded(dim, finalRefArray.Size)
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
                View view, DimensionType dimType, DimSettings settings,
                bool isTotalOnly, bool isGridOnly)
        {
            int s = 0, sk = 0, f = 0;
            var errors = new List<string>();
            var skips = new List<string>();

            var geomOpts = new Options
            {
                ComputeReferences = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            // 1. Get Project Envelope (The absolute box)
            var envelope = DimHelpers.GetProjectEnvelope(allWalls);
            if (envelope == null) return (0, 0, 0, errors, skips);

            // 2. Process each cardinal side (N, S, E, W)
            var sides = new List<XYZ> { XYZ.BasisX, -XYZ.BasisX, XYZ.BasisY, -XYZ.BasisY };

            foreach (XYZ normal in sides)
            {

                // 2a. REFINED GRID HEAD DETECTION: Detect bubbles on this specific side
                if (isGridOnly || isTotalOnly)
                {
                    // Find if ANY grid that is perpendicular to this side has a bubble visible on this side
                    bool sideHasBubbles = allGrids.Any(g => {
                        Curve c = g.Curve;
                        // Get the end of the grid closer to our 'normal' direction
                        int endIdx = (c.GetEndPoint(1).DotProduct(normal) > c.GetEndPoint(0).DotProduct(normal)) ? 1 : 0;
                        return g.IsBubbleVisibleInView((endIdx == 0 ? DatumEnds.End0 : DatumEnds.End1), view);
                    });

                    // Professional Standard: Always keep Top/Left, but only keep Bottom/Right if Bubbles exist
                    bool isTopOrLeft = normal.Y > 0.9 || normal.X < -0.9;
                    if (!isTopOrLeft && !sideHasBubbles) continue;
                }

                // 'dir' is the direction ALONG the dimension string
                XYZ dir = new XYZ(normal.Y, -normal.X, 0).Normalize();

        double extremeLimit = (Math.Abs(normal.X) > 0.9)
            ? (normal.X > 0 ? envelope.Max.X : envelope.Min.X)
            : (normal.Y > 0 ? envelope.Max.Y : envelope.Min.Y);

        // Filter: Only walls on THIS side and within 5ft of the project edge
        List<Wall> sideWalls = allWalls
            .Where(w => Math.Abs(DimHelpers.GetWallNormal(w).DotProduct(normal)) > 0.9)
            .Where(w => {
                XYZ mid = (w.Location as LocationCurve).Curve.Evaluate(0.5, true);
                double val = (Math.Abs(normal.X) > 0.9) ? mid.X : mid.Y;
                return Math.Abs(val - extremeLimit) < 5.0; // 5ft buffer
            }).ToList();

        if (sideWalls.Count == 0 && !isGridOnly) continue;

        var entries = new List<RefEntry>();

                // 3. Select Outermost Faces (Solves Pink/Brown Dots & Butt-Joints)
                if (!isGridOnly)
                {
                    foreach (Wall wall in sideWalls)
                    {
                        var wallFaces = new List<RefEntry>();
                        foreach (Solid solid in DimHelpers.CollectSolids(wall.get_Geometry(geomOpts)))
                        {
                            foreach (Face face in solid.Faces)
                            {
                                if (!(face is PlanarFace pf) || pf.Reference == null) continue;

                                // Ensure face is perpendicular to the dimension string direction
                                if (Math.Abs(pf.FaceNormal.DotProduct(normal)) < 0.95) continue;

                                XYZ cen = DimHelpers.FaceCentroid(pf);
                                wallFaces.Add(new RefEntry
                                {
                                    Ref = pf.Reference,
                                    Pos = cen.DotProduct(dir),
                                    Kind = "wall",
                                    Pt = cen
                                });
                            }
                        }
                        if (wallFaces.Count == 0) continue;

                        // Pick the face furthest in the 'normal' direction (The Outside Face)
                        var chosen = wallFaces.OrderByDescending(f => f.Pt.DotProduct(normal)).First();

                        if (!entries.Any(e => Math.Abs(e.Pos - chosen.Pos) < 0.1))
                            entries.Add(chosen);
                    }

                    // REVISION: Force Snap to Envelope Extents
                    // Because Revit uses butt-joints, one wall might be "shorter" than the building edge.
                    // We sort our current entries and check if they reach the absolute envelope limits.
                    if (entries.Count > 0)
                    {
                        var currentSorted = entries.OrderBy(e => e.Pos).ToList();
                        double envelopeMin = (Math.Abs(normal.Y) > 0.9) ? envelope.Min.X : envelope.Min.Y;
                        double envelopeMax = (Math.Abs(normal.Y) > 0.9) ? envelope.Max.X : envelope.Max.Y;

                        // If our current dimension doesn't reach the edge, it means a wall end was swallowed by a joint.
                        // The logic above already collected the outermost available face references. 
                        // By building the DimLine (Step 6) using the envelope, the string will physically 
                        // extend to the corner even if the Reference is a face slightly set back.
                    }
                }

                // 4. Add Grids (Strict Parallel Check)
                // REVISION: If we are in 'Total Only' mode, we MUST NOT add intermediate grids
                if (!isTotalOnly)
                {
                    foreach (Grid grid in allGrids)
                    {
                        try
                        {
                            XYZ gDir = (grid.Curve.GetEndPoint(1) - grid.Curve.GetEndPoint(0)).Normalize();
                            if (Math.Abs(gDir.DotProduct(dir)) > 0.0001) continue;

                            XYZ gPt = grid.Curve.Evaluate(0.5, true);
                            double gPos = gPt.DotProduct(dir);

                            if (!entries.Any(e => Math.Abs(e.Pos - gPos) < 0.05))
                            {
                                entries.Add(new RefEntry
                                {
                                    Ref = new Reference(grid),
                                    Pos = gPos,
                                    Kind = "grid",
                                    Pt = gPt
                                });
                            }
                        }
                        catch { }
                    }
                }

                // 5. Build the Dimension Line
                var sorted = entries.OrderBy(e => e.Pos).ToList();
                if (sorted.Count < 2) continue;

                // REVISION: Mutually Exclusive Layer Logic
                if (isTotalOnly)
                {
                    // Layer 1: Strictly ONLY the absolute start and end
                    sorted = new List<RefEntry> { sorted.First(), sorted.Last() };
                }
                else if (isGridOnly)
                {
                    // Layer 2: Strictly ONLY Grid references
                    sorted = sorted.Where(e => e.Kind == "grid").ToList();
                    if (sorted.Count < 2) continue;
                }

                ReferenceArray ra = new ReferenceArray();
                foreach (var e in sorted) ra.Append(e.Ref);

                // 6. THE OFFSET STACK & BUTT-JOINT FIX
                double layerMult = isTotalOnly ? 3.0 : (isGridOnly ? 2.0 : 1.0);
                double currentOffsetDist = settings.OffsetDistance * layerMult;
                double offset = extremeLimit + (normal.X + normal.Y > 0 ? currentOffsetDist : -currentOffsetDist);

                // Use Envelope bounds to bridge butt-joints
                double minU = (Math.Abs(normal.Y) > 0.9) ? envelope.Min.X : envelope.Min.Y;
                double maxU = (Math.Abs(normal.Y) > 0.9) ? envelope.Max.X : envelope.Max.Y;

                // Force perfectly parallel Z to kill the "Non-parallel" error
                double viewZ = view.Origin.Z;
                XYZ p1 = (Math.Abs(normal.X) > 0.9) ? new XYZ(offset, minU, viewZ) : new XYZ(minU, offset, viewZ);
                XYZ p2 = (Math.Abs(normal.X) > 0.9) ? new XYZ(offset, maxU, viewZ) : new XYZ(maxU, offset, viewZ);

                try
                {
            Dimension dim = _doc.Create.NewDimension(view, Line.CreateBound(p1, p2), ra, dimType);
            if (dim != null) s++;
        }
        catch (Exception ex) 
        { 
            f++; 
            errors.Add($"Side {normal} failed (Total:{isTotalOnly}, Grid:{isGridOnly}): {ex.Message}"); 
        }
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
            try { return new Reference(grid); }
            catch { return null; }
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
            public bool IncludeTotalString { get; set; }
            public bool IncludeGridsOnlyString { get; set; }
        }

        private static DimSettings ParseSettings(JObject p) => new DimSettings
        {
            IncludeOpenings = p.Value<bool?>("include_openings") ?? true,
            IncludeGrids = p.Value<bool?>("include_grids") ?? true,
            OffsetDistance = (p.Value<double?>("offset_mm") ?? 800.0) / 304.8,
            InsetDistance = (p.Value<double?>("inset_mm") ?? 1000.0) / 304.8,
            SmartExteriorPlacement = p.Value<bool?>("smart_exterior") ?? true,

            IncludeTotalString = p.Value<bool?>("include_total") ?? true,
            IncludeGridsOnlyString = p.Value<bool?>("include_grids_only") ?? true,
        };

        private List<Wall> CollectWallsInView(View view) =>
            new FilteredElementCollector(_doc, view.Id)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                // REVISION: Include all walls that have a location curve (includes Curtain Walls)
                .Where(w => w.Location is LocationCurve)
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