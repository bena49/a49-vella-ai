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

                        // =========================================================
                        // PASS 2: Perpendicular location strings
                        // =========================================================

                        double baseOffsetFt = settings.OffsetDistance;
                        double baseInsetFt = settings.InsetDistance;

                        // ── Absolute stacking offsets (ft from wall face outward) ──────────
                        // Layer 3 (Detail/innermost):  baseOffsetFt
                        // Layer 2 (Grid-to-Grid):      baseOffsetFt + spacingFt
                        // Layer 1 (Total/outermost):   baseOffsetFt + spacingFt * 2
                        // Using a fixed spacing equal to baseOffsetFt keeps strings
                        // proportional to the user-chosen offset and prevents overlap.
                        double spacingFt = baseOffsetFt;

                        // Layer 1: Overall/Total Dimension (Outermost)
                        if (settings.IncludeTotalString)
                        {
                            double totalOffset = baseOffsetFt + spacingFt * 2.0;
                            var pTotal = Pass2(allWalls, allGrids, view, dimType, settings, true, false, totalOffset);
                            succeeded += pTotal.s;
                            skipped += pTotal.sk;
                            failed += pTotal.f;
                            allErrors.AddRange(pTotal.errors);
                            allSkipReasons.AddRange(pTotal.skips);
                        }

                        // Layer 2: Grid-to-Grid Only (Middle)
                        if (settings.IncludeGridsOnlyString)
                        {
                            int gridCount = allGrids.Count;
                            if (gridCount > 2)
                            {
                                double gridOffset = baseOffsetFt + spacingFt;
                                var pGrids = Pass2(allWalls, allGrids, view, dimType, settings, false, true, gridOffset);
                                succeeded += pGrids.s;
                                skipped += pGrids.sk;
                                failed += pGrids.f;
                                allErrors.AddRange(pGrids.errors);
                                allSkipReasons.AddRange(pGrids.skips);
                            }
                            else
                            {
                                allSkipReasons.Add($"Grid layer skipped: only {gridCount} grids (redundant with Total)");
                            }
                        }

                        // Layer 3: Detail Perimeter (Innermost) — controlled by IncludeDetailString
                        if (settings.IncludeDetailString)
                        {
                            double detailOffset = baseInsetFt;
                            var p2 = Pass2(allWalls, allGrids, view, dimType, settings, false, false, detailOffset);
                            succeeded += p2.s; skipped += p2.sk; failed += p2.f;
                            allErrors.AddRange(p2.errors);
                            allSkipReasons.AddRange(p2.skips);
                        }

                        if (failed > 0 && succeeded == 0) tx.RollBack();
                        else tx.Commit();

                        // Count this view once: succeeded if any dimension was created
                        if (succeeded > 0) totalSucceeded++;
                        else if (failed > 0) totalFailed++;
                        totalSkipped += skipped;
                        totalFailed += failed;
                    }
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

            // Filter out tiny segments
            const double minSegmentLength = 0.5;
            var filteredRefs = new List<Reference>();

            for (int i = 0; i < refArray.Size; i++)
            {
                if (i == 0 || i == refArray.Size - 1)
                {
                    filteredRefs.Add(refArray.get_Item(i));
                    continue;
                }

                Reference currentRef = refArray.get_Item(i);
                Reference prevRef = refArray.get_Item(i - 1);
                Reference nextRef = refArray.get_Item(i + 1);

                var currentTagged = allRefs.FirstOrDefault(r => r.Ref == currentRef);
                var prevTagged = allRefs.FirstOrDefault(r => r.Ref == prevRef);
                var nextTagged = allRefs.FirstOrDefault(r => r.Ref == nextRef);

                if (currentTagged != null && prevTagged != null && nextTagged != null)
                {
                    double distToPrev = currentTagged.U - prevTagged.U;
                    double distToNext = nextTagged.U - currentTagged.U;

                    if (distToPrev >= minSegmentLength && distToNext >= minSegmentLength)
                    {
                        filteredRefs.Add(currentRef);
                    }
                    else if (currentTagged.Kind == "grid")
                    {
                        filteredRefs.Add(currentRef);
                    }
                }
                else
                {
                    filteredRefs.Add(currentRef);
                }
            }

            var finalRefArray = new ReferenceArray();
            foreach (var r in filteredRefs)
                finalRefArray.Append(r);

            XYZ offsetDir = DimHelpers.GetDimLineOffsetDirection(
                primary, doc, view, request.SmartExteriorPlacement);

            Line dimLine = DimHelpers.BuildDimensionLine(
                primary, offsetDir, request.OffsetDistance, allRefs);

            if (dimLine == null)
                return DimResult.Skipped("Collinear group: dimLine null.");

            try
            {
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
                    if (perp.GetLength() > 0.1) continue;  // ~30mm — walls must be truly collinear
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

        private class RefEntry
        {
            public Reference Ref { get; set; }
            public double Pos { get; set; }
            public string Kind { get; set; }
            public XYZ Pt { get; set; }
            public Wall Wall { get; set; }
        }

        private (int s, int sk, int f, List<string> errors, List<string> skips)
            Pass2(List<Wall> allWalls, List<Grid> allGrids,
                View view, DimensionType dimType, DimSettings settings,
                bool isTotalOnly, bool isGridOnly, double explicitOffsetFt)
        {
            int s = 0, sk = 0, f = 0;
            var errors = new List<string>();
            var skips = new List<string>();

            var geomOpts = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            // Get Project Envelope
            var envelope = DimHelpers.GetProjectEnvelope(allWalls);
            if (envelope == null) return (0, 0, 0, errors, skips);

            // Building centroid — used for interior face selection in Detail layer
            XYZ buildingCentroid = new XYZ(
                (envelope.Min.X + envelope.Max.X) / 2.0,
                (envelope.Min.Y + envelope.Max.Y) / 2.0,
                0);

            // Process each cardinal side
            var sides = new List<XYZ> { XYZ.BasisX, -XYZ.BasisX, XYZ.BasisY, -XYZ.BasisY };
            string layerName = isTotalOnly ? "TOTAL" : (isGridOnly ? "GRID" : "DETAIL");

            foreach (XYZ normal in sides)
            {
                // ── Robust bubble visibility check ─────────────────────────────────
                // Checks BOTH DatumEnds for each grid (the End0/End1 to physical
                // endpoint mapping is not guaranteed to match draw direction).
                // A side is considered "bubble-side" only if a visible bubble endpoint
                // lies further in the normal direction than the grid midpoint.
                // ALL layers respect this check — never place strings on no-bubble sides.
                bool sideHasBubbles = false;
                if (allGrids.Count > 0)
                {
                    foreach (Grid grid in allGrids)
                    {
                        try
                        {
                            Curve c = grid.Curve;
                            if (c == null) continue;

                            bool end0Vis = false, end1Vis = false;
                            try { end0Vis = grid.IsBubbleVisibleInView(DatumEnds.End0, view); } catch { }
                            try { end1Vis = grid.IsBubbleVisibleInView(DatumEnds.End1, view); } catch { }
                            if (!end0Vis && !end1Vis) continue;

                            // Verify the visible bubble endpoint is on the "normal" side:
                            // its position past the grid midpoint in the normal direction.
                            XYZ gMid = c.Evaluate(0.5, true);
                            XYZ p0 = c.GetEndPoint(0);
                            XYZ p1 = c.GetEndPoint(1);

                            if ((end0Vis && (p0 - gMid).DotProduct(normal) > -0.01) ||
                                (end1Vis && (p1 - gMid).DotProduct(normal) > -0.01))
                            {
                                sideHasBubbles = true;
                                break;
                            }
                        }
                        catch { }
                    }

                    // All layers skip sides with no visible grid bubble.
                    // This prevents orphaned reference strings on bubble-free sides.
                    if (!sideHasBubbles) continue;
                }

                // Direction ALONG the dimension string
                XYZ dir = new XYZ(normal.Y, -normal.X, 0).Normalize();

                double extremeLimit = (Math.Abs(normal.X) > 0.9)
                    ? (normal.X > 0 ? envelope.Max.X : envelope.Min.X)
                    : (normal.Y > 0 ? envelope.Max.Y : envelope.Min.Y);

                // Collect walls on this side
                List<Wall> sideWalls = allWalls
                    .Where(w => {
                        try
                        {
                            return Math.Abs(DimHelpers.GetWallNormal(w).DotProduct(normal)) > 0.8;
                        }
                        catch { return false; }
                    }).ToList();

                if (sideWalls.Count == 0 && !isGridOnly) continue;

                var entries = new List<RefEntry>();

                // ── Collect wall references ────────────────────────────────────────
                if (!isGridOnly)
                {
                    if (isTotalOnly)
                    {
                        // ── Layer 1 (Total): use wall END CAP references ──────────
                        // Each wall contributes TWO references (start cap + end cap),
                        // measured along `dir`. This guarantees a valid min/max even
                        // when the entire side is ONE continuous wall element.
                        foreach (Wall wall in sideWalls)
                        {
                            try
                            {
                                var (startCap, endCap) = DimHelpers.GetWallEndCapRefs(wall);
                                if (startCap == null || endCap == null) continue;

                                var cl = DimHelpers.GetWallCenterLine(wall);
                                if (cl == null) continue;
                                XYZ wOrigin = cl.GetEndPoint(0);
                                XYZ wDir = (cl.GetEndPoint(1) - wOrigin).Normalize();

                                // Convert U-position along wall direction to position along dim dir
                                XYZ startPt = wOrigin + wDir * startCap.U;
                                XYZ endPt = wOrigin + wDir * endCap.U;

                                double startPos = startPt.DotProduct(dir);
                                double endPos = endPt.DotProduct(dir);

                                if (!entries.Any(e => Math.Abs(e.Pos - startPos) < 0.15))
                                    entries.Add(new RefEntry { Ref = startCap.Ref, Pos = startPos, Kind = "endcap", Pt = startPt, Wall = wall });
                                if (!entries.Any(e => Math.Abs(e.Pos - endPos) < 0.15))
                                    entries.Add(new RefEntry { Ref = endCap.Ref, Pos = endPos, Kind = "endcap", Pt = endPt, Wall = wall });
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        // ── Layer 2 / Detail: use wall face references ────────────
                        var allCandidates = new List<RefEntry>();
                        double centroidInNormal = buildingCentroid.DotProduct(normal);

                        foreach (Wall wall in sideWalls)
                        {
                            var wallFaces = new List<RefEntry>();

                            try
                            {
                                GeometryElement geo = wall.get_Geometry(geomOpts);
                                if (geo == null) continue;

                                foreach (GeometryObject obj in geo)
                                {
                                    Solid solid = null;
                                    if (obj is Solid solidObj && solidObj.Volume > 0)
                                        solid = solidObj;
                                    else if (obj is GeometryInstance gi)
                                    {
                                        foreach (GeometryObject sub in gi.GetInstanceGeometry())
                                            if (sub is Solid subSolid && subSolid.Volume > 0)
                                                solid = subSolid;
                                    }

                                    if (solid == null) continue;

                                    foreach (Face face in solid.Faces)
                                    {
                                        if (!(face is PlanarFace pf)) continue;
                                        if (pf.Reference == null) continue;

                                        double dot = pf.FaceNormal.DotProduct(normal);
                                        if (Math.Abs(dot) < 0.8) continue;

                                        XYZ cen = DimHelpers.FaceCentroid(pf);
                                        double posAlongDim = cen.DotProduct(dir);

                                        wallFaces.Add(new RefEntry
                                        {
                                            Ref = pf.Reference,
                                            Pos = posAlongDim,
                                            Kind = "wall",
                                            Pt = cen,
                                            Wall = wall
                                        });
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error getting geometry for wall {wall.Id}: {ex.Message}");
                            }

                            if (wallFaces.Count == 0)
                            {
                                var lc = wall.Location as LocationCurve;
                                if (lc != null && lc.Curve is Line line)
                                {
                                    XYZ midPoint = line.Evaluate(0.5, true);
                                    double posAlongDim = midPoint.DotProduct(dir);
                                    allCandidates.Add(new RefEntry
                                    {
                                        Ref = new Reference(wall),
                                        Pos = posAlongDim,
                                        Kind = "wall_fallback",
                                        Pt = midPoint,
                                        Wall = wall
                                    });
                                }
                                continue;
                            }

                            // ── Face selection ────────────────────────────────────
                            // Detail layer: pick the INTERIOR face (closest to building centroid)
                            //   so room-clear dimensions are measured face-to-face without
                            //   crossing wall thickness.
                            // Grid layer:   pick the outermost face (standard exterior ref).
                            RefEntry chosen;
                            if (!isGridOnly && wallFaces.Count > 1)
                            {
                                chosen = wallFaces
                                    .OrderBy(f => Math.Abs(f.Pt.DotProduct(normal) - centroidInNormal))
                                    .First();
                            }
                            else
                            {
                                chosen = wallFaces.OrderByDescending(f => f.Pt.DotProduct(normal)).First();
                            }

                            allCandidates.Add(chosen);
                        }

                        if (allCandidates.Count == 0) continue;

                        foreach (var candidate in allCandidates)
                        {
                            bool isDuplicate = entries.Any(e => Math.Abs(e.Pos - candidate.Pos) < 0.2);
                            if (!isDuplicate)
                                entries.Add(candidate);
                        }

                        if (entries.Count < 2 && allCandidates.Count >= 2)
                        {
                            double minPos = allCandidates.Min(c => c.Pos);
                            double maxPos = allCandidates.Max(c => c.Pos);
                            var minEntry = allCandidates.First(c => Math.Abs(c.Pos - minPos) < 0.01);
                            var maxEntry = allCandidates.First(c => Math.Abs(c.Pos - maxPos) < 0.01);
                            entries.Clear();
                            entries.Add(minEntry);
                            entries.Add(maxEntry);
                        }
                    }
                }

                // Add grid references (for Detail and Grid-only layers)
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

                            if (!entries.Any(e => Math.Abs(e.Pos - gPos) < 0.2))
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

                // Sort and filter by layer type
                var sorted = entries.OrderBy(e => e.Pos).ToList();
                if (sorted.Count < 2)
                {
                    sk++;
                    skips.Add($"Side {normal}: Only {sorted.Count} references for {layerName}");
                    continue;
                }

                if (isTotalOnly)
                {
                    // Total: only first and last
                    sorted = new List<RefEntry> { sorted.First(), sorted.Last() };
                }
                else if (isGridOnly)
                {
                    // Grid-only: filter to grids only
                    var gridOnlyEntries = sorted.Where(e => e.Kind == "grid").ToList();
                    if (gridOnlyEntries.Count < 2)
                    {
                        sk++;
                        skips.Add($"Side {normal}: Only {gridOnlyEntries.Count} grids for Grid layer");
                        continue;
                    }
                    sorted = gridOnlyEntries;
                }
                else
                {
                    // Detail: deduplicate, prefer grids over walls
                    var deduped = new List<RefEntry>();
                    foreach (var e in sorted)
                    {
                        var existing = deduped.FirstOrDefault(d => Math.Abs(d.Pos - e.Pos) < 0.2);
                        if (existing == null)
                            deduped.Add(e);
                        else if (e.Kind == "grid" && existing.Kind != "grid")
                        {
                            deduped.Remove(existing);
                            deduped.Add(e);
                        }
                    }
                    sorted = deduped.OrderBy(e => e.Pos).ToList();
                }

                // ── Minimum segment filter ─────────────────────────────────────────
                // Remove intermediate references that create near-zero segments.
                // Caused by butt-wall corners where the end cap and the through-wall
                // face land within wall-thickness distance (~75mm) of each other.
                // Grids are always preserved regardless of segment length.
                const double minSegmentFt = 0.25; // ~75 mm
                if (sorted.Count > 2)
                {
                    var filtered = new List<RefEntry> { sorted[0] };
                    for (int i = 1; i < sorted.Count - 1; i++)
                    {
                        double distToPrev = sorted[i].Pos - filtered.Last().Pos;
                        double distToNext = sorted[i + 1].Pos - sorted[i].Pos;
                        if (distToPrev >= minSegmentFt && distToNext >= minSegmentFt)
                            filtered.Add(sorted[i]);
                        else if (sorted[i].Kind == "grid")
                            filtered.Add(sorted[i]); // grids always kept
                    }
                    filtered.Add(sorted.Last());
                    sorted = filtered;
                }

                // Build ReferenceArray
                ReferenceArray ra = new ReferenceArray();
                foreach (var e in sorted)
                {
                    if (e.Ref != null)
                        ra.Append(e.Ref);
                }

                if (ra.Size < 2)
                {
                    sk++;
                    skips.Add($"Side {normal}: Only {ra.Size} valid references for {layerName}");
                    continue;
                }

                // Calculate offset position using the explicit offset
                double offset = extremeLimit + (normal.X + normal.Y > 0 ? explicitOffsetFt : -explicitOffsetFt);

                // Use envelope bounds
                double minU = (Math.Abs(normal.Y) > 0.9) ? envelope.Min.X : envelope.Min.Y;
                double maxU = (Math.Abs(normal.Y) > 0.9) ? envelope.Max.X : envelope.Max.Y;

                double viewZ = view.Origin.Z;
                XYZ p1, p2;

                if (Math.Abs(normal.X) > 0.9)
                {
                    p1 = new XYZ(offset, minU, viewZ);
                    p2 = new XYZ(offset, maxU, viewZ);
                }
                else
                {
                    p1 = new XYZ(minU, offset, viewZ);
                    p2 = new XYZ(maxU, offset, viewZ);
                }

                // Ensure correct order
                if (p1.X > p2.X || p1.Y > p2.Y)
                {
                    var temp = p1;
                    p1 = p2;
                    p2 = temp;
                }

                try
                {
                    Line dimLine = Line.CreateBound(p1, p2);
                    Dimension dim = _doc.Create.NewDimension(view, dimLine, ra, dimType);
                    if (dim != null)
                    {
                        s++;
                        System.Diagnostics.Debug.WriteLine($"✅ Created {layerName} dimension on side {normal} with offset {explicitOffsetFt}ft, {ra.Size} refs");
                    }
                }
                catch (Exception ex)
                {
                    f++;
                    errors.Add($"Side {normal} failed ({layerName}): {ex.Message} - Refs: {ra.Size}");
                    System.Diagnostics.Debug.WriteLine($"❌ Failed: {ex.Message}");
                }
            }

            return (s, sk, f, errors, skips);
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
            public bool IncludeDetailString { get; set; }
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
            IncludeDetailString = p.Value<bool?>("include_detail") ?? true,
        };

        private List<Wall> CollectWallsInView(View view) =>
            new FilteredElementCollector(_doc, view.Id)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .Cast<Wall>()
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