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

                        // PASS 1: Along-wall strings for ALL walls (exterior + interior).
                        // Exterior walls get end-cap + opening + grid strings.
                        // Interior walls get their own detail strings (room widths, door positions).
                        // Pass2 handles the exterior stacked Layer 1/2 strings.
                        // Pass3 handles the cross-building interior room strings (when enabled).
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

                        // ── Layer stacking: fixed 500mm gap between strings ─────────────
                        // offset_mm (baseOffsetFt) = distance from building edge to Layer 3.
                        // Each outer layer adds one fixed gap width beyond the previous.
                        // This keeps strings close together regardless of the base offset.
                        const double fixedGapFt = 900.0 / 304.8; // 900mm between layers — increase this value to space strings further apart

                        // Layer 1: Overall/Total Dimension (Outermost)
                        if (settings.IncludeTotalString)
                        {
                            double totalOffset = baseOffsetFt + fixedGapFt * 2.0;
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
                            double gridOffset = baseOffsetFt + fixedGapFt;
                            var pGrids = Pass2(allWalls, allGrids, view, dimType, settings, false, true, gridOffset);
                            succeeded += pGrids.s;
                            skipped += pGrids.sk;
                            failed += pGrids.f;
                            allErrors.AddRange(pGrids.errors);
                            allSkipReasons.AddRange(pGrids.skips);
                        }

                        // Pass 3: Interior room strings (one horizontal + one vertical)
                        // Enabled via include_interior in wizard. Creates face-to-face
                        // room dimension strings running through the building interior.
                        if (settings.IncludeInteriorStrings)
                        {
                            var p3 = Pass3Interior(allWalls, view, dimType, settings);
                            succeeded += p3.s; skipped += p3.sk; failed += p3.f;
                            allErrors.AddRange(p3.errors);
                            allSkipReasons.AddRange(p3.skips);
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

                            // Only check grids running PARALLEL to this side's normal.
                            // Perpendicular grids (e.g. Grid 1/2 for left/right sides)
                            // would otherwise falsely mark the no-bubble side as visible.
                            XYZ gDirChk = (c.GetEndPoint(1) - c.GetEndPoint(0)).Normalize();
                            if (Math.Abs(gDirChk.DotProduct(normal)) < 0.7) continue;

                            bool end0Vis = false, end1Vis = false;
                            try { end0Vis = grid.IsBubbleVisibleInView(DatumEnds.End0, view); } catch { }
                            try { end1Vis = grid.IsBubbleVisibleInView(DatumEnds.End1, view); } catch { }
                            if (!end0Vis && !end1Vis) continue;

                            XYZ gMid = c.Evaluate(0.5, true);
                            XYZ gEnd0 = c.GetEndPoint(0);
                            XYZ gEnd1 = c.GetEndPoint(1);

                            // DatumEnds.End0 in Revit maps to GetEndPoint(1) of the curve
                            // (the parametric "end"), not GetEndPoint(0) (the "start").
                            // So we cross-check: End0Vis uses gEnd1's position, End1Vis uses gEnd0.
                            // Threshold 0.5ft ensures we only accept endpoints clearly on this side.
                            if ((end0Vis && (gEnd1 - gMid).DotProduct(normal) > 0.5) ||
                                (end1Vis && (gEnd0 - gMid).DotProduct(normal) > 0.5))
                            {
                                sideHasBubbles = true;
                                break;
                            }
                        }
                        catch { }
                    }
                    if (!sideHasBubbles) continue;
                }

                // Direction ALONG the dimension string
                XYZ dir = new XYZ(normal.Y, -normal.X, 0).Normalize();
                bool sideUseX = Math.Abs(normal.X) > 0.9;
                bool sidePositive = sideUseX ? normal.X > 0 : normal.Y > 0;

                // Step 1: All walls whose face normal matches this side (no proximity filter).
                // Needed to compute the LOCAL extreme before applying proximity filter.
                var dirMatchedWalls = allWalls.Where(w => {
                    try
                    {
                        if (w.WallType?.Kind == WallKind.Curtain)
                        {
                            var lc2 = w.Location as LocationCurve;
                            if (lc2 == null) return false;
                            XYZ cwd2 = (lc2.Curve.GetEndPoint(1) - lc2.Curve.GetEndPoint(0)).Normalize();
                            return Math.Abs(new XYZ(-cwd2.Y, cwd2.X, 0).DotProduct(normal)) > 0.8;
                        }
                        return Math.Abs(DimHelpers.GetWallNormal(w).DotProduct(normal)) > 0.8;
                    }
                    catch { return false; }
                }).ToList();

                // Step 2: LOCAL extremeLimit = most extreme wall midpoint for this side.
                // For C/L/T-shaped buildings the global bounding box gives an extremeLimit
                // that doesn't match where walls actually are on each side, causing the
                // proximity filter to exclude real perimeter walls. Local extreme fixes this.
                double extremeLimit;
                {
                    var midPos = dirMatchedWalls
                        .Select(w => { var wlc2 = w.Location as LocationCurve; if (wlc2 == null) return (double?)null; XYZ m2 = wlc2.Curve.Evaluate(0.5, true); return sideUseX ? (double?)m2.X : (double?)m2.Y; })
                        .Where(v => v.HasValue).Select(v => v.Value).ToList();
                    extremeLimit = midPos.Count > 0
                        ? (sidePositive ? midPos.Max() : midPos.Min())
                        : (sideUseX ? (normal.X > 0 ? envelope.Max.X : envelope.Min.X) : (normal.Y > 0 ? envelope.Max.Y : envelope.Min.Y));
                }

                // Step 3: Proximity-filter sideWalls using LOCAL extremeLimit.
                const double sideWallProximityFt = 2.0; // ~600mm
                List<Wall> sideWalls = dirMatchedWalls.Where(w => {
                    try
                    {
                        var wlc = w.Location as LocationCurve;
                        if (wlc == null) return true;
                        XYZ wMid = wlc.Curve.Evaluate(0.5, true);
                        double distFromEdge = sideUseX ? Math.Abs(wMid.X - extremeLimit) : Math.Abs(wMid.Y - extremeLimit);
                        return distFromEdge <= sideWallProximityFt;
                    }
                    catch { return false; }
                }).ToList();

                // Grids crossing this dim line (parallel to normal, positioned along dir)
                var sideGrids = allGrids.Where(g => {
                    try
                    {
                        XYZ gd = (g.Curve.GetEndPoint(1) - g.Curve.GetEndPoint(0)).Normalize();
                        return Math.Abs(gd.DotProduct(dir)) < 0.01;
                    }
                    catch { return false; }
                }).OrderBy(g => g.Curve.Evaluate(0.5, true).DotProduct(dir)).ToList();

                var entries = new List<RefEntry>();

                if (isTotalOnly)
                {
                    // Layer 1: first and last grid = "Grid A to Grid C"
                    // Grid refs sit at design positions independent of wall join geometry.
                    if (sideGrids.Count >= 2)
                    {
                        var fg = sideGrids.First();
                        var lg = sideGrids.Last();
                        XYZ fPt = fg.Curve.Evaluate(0.5, true);
                        XYZ lPt = lg.Curve.Evaluate(0.5, true);
                        entries.Add(new RefEntry { Ref = new Reference(fg), Pos = fPt.DotProduct(dir), Kind = "grid", Pt = fPt });
                        entries.Add(new RefEntry { Ref = new Reference(lg), Pos = lPt.DotProduct(dir), Kind = "grid", Pt = lPt });
                    }
                    else
                    {
                        // Fallback: wall end caps when no grids on this side.
                        // Curtain walls use boundary mullion refs instead of end caps.
                        foreach (Wall wall in sideWalls)
                        {
                            try
                            {
                                if (wall.WallType?.Kind == WallKind.Curtain)
                                {
                                    AddCurtainWallBoundaryRefs(wall, dir, entries, _doc);
                                    continue;
                                }
                                var (sc, ec) = DimHelpers.GetWallEndCapRefs(wall);
                                if (sc == null || ec == null) continue;
                                var cl = DimHelpers.GetWallCenterLine(wall);
                                if (cl == null) continue;
                                XYZ wO = cl.GetEndPoint(0);
                                XYZ wD = (cl.GetEndPoint(1) - wO).Normalize();
                                XYZ sPt = wO + wD * sc.U; double sPos = sPt.DotProduct(dir);
                                XYZ ePt = wO + wD * ec.U; double ePos = ePt.DotProduct(dir);
                                if (!entries.Any(e => Math.Abs(e.Pos - sPos) < 0.15))
                                    entries.Add(new RefEntry { Ref = sc.Ref, Pos = sPos, Kind = "endcap", Pt = sPt, Wall = wall });
                                if (!entries.Any(e => Math.Abs(e.Pos - ePos) < 0.15))
                                    entries.Add(new RefEntry { Ref = ec.Ref, Pos = ePos, Kind = "endcap", Pt = ePt, Wall = wall });
                            }
                            catch { }
                        }
                    }
                }
                else if (isGridOnly)
                {
                    // Layer 2: all grids. Skip when fewer than 3 — would duplicate Layer 1.
                    if (sideGrids.Count < 3)
                    {
                        sk++;
                        skips.Add($"Side {normal}: {sideGrids.Count} grids — Layer 2 redundant with Layer 1, skipped.");
                        continue;
                    }
                    foreach (Grid g in sideGrids)
                    {
                        XYZ gPt = g.Curve.Evaluate(0.5, true);
                        entries.Add(new RefEntry { Ref = new Reference(g), Pos = gPt.DotProduct(dir), Kind = "grid", Pt = gPt });
                    }
                }
                else
                {
                    // Layer 3 (exterior detail) is now handled entirely by Pass 1:
                    // each wall group gets its own dimension string showing end caps,
                    // opening positions, and grid intersections. Pass 1 is cleaner than
                    // a per-side approach because it handles each wall section individually,
                    // which works correctly for non-rectangular (C, L, T) plan shapes.
                    // Interior room strings are handled by Pass 3 (Pass3Interior).
                    sk++;
                    skips.Add($"Side {normal}: Detail layer skipped in Pass2 — Pass1 and Pass3 handle detail strings.");
                    continue;
                }

                var sorted = entries.OrderBy(e => e.Pos).ToList();
                if (sorted.Count < 2)
                {
                    sk++; skips.Add($"Side {normal}: {sorted.Count} refs for {layerName} — skipped.");
                    continue;
                }

                if (isTotalOnly)
                    sorted = new List<RefEntry> { sorted.First(), sorted.Last() };

                // Minimum segment filter: scale with view scale so tiny dims are suppressed
                // automatically on small-scale views (1:200, 1:500 etc).
                // Formula: 2mm on paper = minimum real dimension shown.
                // At 1:100 → 200mm min; at 1:200 → 400mm min; at 1:50 → 100mm min.
                // Floor at 0.5ft (~150mm) prevents over-filtering on very large scale views.
                double minSegmentFt = Math.Max(0.5, view.Scale * 2.0 / 304.8);
                if (sorted.Count > 2)
                {
                    var filtered = new List<RefEntry> { sorted[0] };
                    for (int i = 1; i < sorted.Count - 1; i++)
                    {
                        double dPrev = sorted[i].Pos - filtered.Last().Pos;
                        double dNext = sorted[i + 1].Pos - sorted[i].Pos;
                        if (dPrev >= minSegmentFt && dNext >= minSegmentFt)
                            filtered.Add(sorted[i]);
                        else if (sorted[i].Kind == "grid")
                            filtered.Add(sorted[i]);
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

                // Dimension line offset from building edge.
                // Positive = exterior (pushes outward in normal direction).
                // Negative = interior (pushes inward, for Detail layer).
                double offset = extremeLimit + (normal.X + normal.Y > 0 ? explicitOffsetFt : -explicitOffsetFt);

                // Span the dim line exactly between the outermost references along dir,
                // with a small pad so Revit's witness lines have room to render.
                const double linePad = 0.3; // ~90mm
                double lineStart = sorted.First().Pos - linePad;
                double lineEnd = sorted.Last().Pos + linePad;

                double viewZ = view.Origin.Z;
                XYZ p1, p2;

                if (Math.Abs(normal.X) > 0.9)
                {
                    // Vertical dim line (horizontal wall sides — left/right)
                    p1 = new XYZ(offset, lineStart, viewZ);
                    p2 = new XYZ(offset, lineEnd, viewZ);
                }
                else
                {
                    // Horizontal dim line (vertical wall sides — top/bottom)
                    p1 = new XYZ(lineStart, offset, viewZ);
                    p2 = new XYZ(lineEnd, offset, viewZ);
                }

                // Ensure p1 < p2 so CreateBound doesn't throw
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
            /// <summary>
            /// When true, Layer 3 places strings INSIDE rooms (transWall interior faces).
            /// When false (default), Layer 3 is an exterior detail string closest to the
            /// building face — correct for all plan sizes and standard practice.
            /// </summary>
            public bool DetailAsInterior { get; set; }
            /// <summary>
            /// When true, Pass3Interior runs: creates one horizontal + one vertical
            /// interior room dimension string through the building at its centroid.
            /// References are interior wall faces, giving clear room width/height dims
            /// without wall thickness. Controlled by include_interior in the wizard.
            /// </summary>
            public bool IncludeInteriorStrings { get; set; }
            /// <summary>
            /// How far (ft) from the building centroid to search for interior wall refs.
            /// Limits Pass3 on large complex plans. 0 = no limit.
            /// </summary>
            public double DepthDistance { get; set; }
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
            DetailAsInterior = p.Value<bool?>("detail_interior") ?? false,
            IncludeInteriorStrings = p.Value<bool?>("include_interior") ?? false,
            // depth_mm: max search radius from centroid for Pass3 interior refs.
            // Default 0 = no limit. Increase to restrict on large plans.
            DepthDistance = (p.Value<double?>("depth_mm") ?? 5000.0) / 304.8,
        };


        /// <summary>
        /// Adds boundary mullion (or grid line) references for a curtain wall to the entries list.
        /// Curtain walls have no solid face geometry, so we locate their corner/boundary mullions
        /// and use those as dimension anchors. Falls back to wall extent with a direct wall ref
        /// if no mullions are found at the boundaries.
        /// </summary>
        private static void AddCurtainWallBoundaryRefs(
            Wall curtainWall, XYZ dir, List<RefEntry> entries, Document doc)
        {
            var lc = curtainWall.Location as LocationCurve;
            if (lc == null) return;

            XYZ ep0 = lc.Curve.GetEndPoint(0);
            XYZ ep1 = lc.Curve.GetEndPoint(1);
            double pos0 = ep0.DotProduct(dir);
            double pos1 = ep1.DotProduct(dir);

            Reference ref0 = null, ref1 = null;

            // Try boundary mullion references first (most reliable Revit reference for dims).
            // CurtainGrid.GetMullionIds() is the correct API for curtain wall mullion access.
            try
            {
                var curtainGrid = curtainWall.CurtainGrid;
                if (curtainGrid != null)
                {
                    foreach (ElementId mullionId in curtainGrid.GetMullionIds())
                    {
                        if (!(doc.GetElement(mullionId) is Mullion mullion)) continue;
                        var mullionLocPt = mullion.Location as LocationPoint;
                        if (mullionLocPt == null) continue;
                        XYZ mullionPt = mullionLocPt.Point;
                        double mp = mullionPt.DotProduct(dir);
                        if (ref0 == null && Math.Abs(mp - pos0) < 1.0) ref0 = new Reference(mullion);
                        if (ref1 == null && Math.Abs(mp - pos1) < 1.0) ref1 = new Reference(mullion);
                        if (ref0 != null && ref1 != null) break;
                    }
                }
            }
            catch { }

            // Fallback: direct wall reference for position tracking.
            // Note: a single wall reference cannot anchor two separate dimension points —
            // only one endpoint will be registered per reference.
            if (ref0 == null && ref1 == null)
            {
                try { ref0 = new Reference(curtainWall); } catch { }
            }

            if (ref0 != null && !entries.Any(e => Math.Abs(e.Pos - pos0) < 0.15))
                entries.Add(new RefEntry { Ref = ref0, Pos = pos0, Kind = "curtain", Pt = ep0, Wall = curtainWall });

            if (ref1 != null && ref1 != ref0 && !entries.Any(e => Math.Abs(e.Pos - pos1) < 0.15))
                entries.Add(new RefEntry { Ref = ref1, Pos = pos1, Kind = "curtain", Pt = ep1, Wall = curtainWall });
        }

        // ======================================================================
        //  PASS 3 — Interior room dimension strings (clustered per zone)
        // ======================================================================

        /// <summary>
        /// Creates interior room dimension strings — one H + one V per coherent room zone.
        /// Zones are identified by clustering wall positions along the perpendicular axis
        /// with a gap threshold (clusterGapFt). A C/L-shaped building will typically
        /// produce 2 horizontal and 2 vertical strings (one per arm).
        ///
        /// Dim line perpendicular position: controlled by settings.InsetDistance (inset_mm)
        /// measured from the building envelope edge — the user's "Interior Inset" slider.
        /// DepthDistance (depth_mm) limits how wide each cluster can be in the perp axis.
        /// </summary>
        private (int s, int sk, int f, List<string> errors, List<string> skips)
            Pass3Interior(List<Wall> allWalls, View view, DimensionType dimType, DimSettings settings)
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

            var envelope = DimHelpers.GetProjectEnvelope(allWalls);
            if (envelope == null) return (0, 0, 0, errors, skips);

            double viewZ = view.Origin.Z;
            double minSegFt = Math.Max(0.5, view.Scale * 2.0 / 304.8);

            // Gap threshold for clustering: if two walls are more than clusterGapFt apart
            // along the perpendicular axis they are in different zones.
            const double clusterGapFt = 16.4; // ~5m — tune higher for large buildings

            foreach (bool isHorizontal in new[] { true, false })
            {
                // measureDir: direction the string spans (X for horizontal, Y for vertical)
                // perpDir:    axis perpendicular to string (Y for horizontal, X for vertical)
                XYZ measureDir = isHorizontal ? XYZ.BasisX : XYZ.BasisY;

                // Candidates: walls whose face normal ≈ measureDir
                var candidates = allWalls.Where(w => {
                    try { return Math.Abs(DimHelpers.GetWallNormal(w).DotProduct(measureDir)) > 0.7; }
                    catch { return false; }
                }).ToList();

                if (candidates.Count == 0)
                {
                    sk++;
                    skips.Add($"Pass3 {(isHorizontal ? "H" : "V")}: no candidate walls.");
                    continue;
                }

                // Get each candidate's perpendicular midpoint position
                var wallPerpPos = candidates
                    .Select(w => {
                        try
                        {
                            var wlc = w.Location as LocationCurve;
                            if (wlc == null) return (Wall: w, Perp: (double?)null);
                            XYZ mid = wlc.Curve.Evaluate(0.5, true);
                            return (Wall: w, Perp: (double?)(isHorizontal ? mid.Y : mid.X));
                        }
                        catch { return (Wall: w, Perp: (double?)null); }
                    })
                    .Where(x => x.Perp.HasValue)
                    .OrderBy(x => x.Perp.Value)
                    .ToList();

                if (wallPerpPos.Count == 0) continue;

                // Cluster walls by perpendicular position
                var clusters = new List<List<Wall>>();
                var current = new List<Wall> { wallPerpPos[0].Wall };
                double lastPerp = wallPerpPos[0].Perp.Value;

                for (int i = 1; i < wallPerpPos.Count; i++)
                {
                    double gap = wallPerpPos[i].Perp.Value - lastPerp;
                    if (gap > clusterGapFt)
                    { clusters.Add(current); current = new List<Wall>(); }
                    current.Add(wallPerpPos[i].Wall);
                    lastPerp = wallPerpPos[i].Perp.Value;
                }
                clusters.Add(current);

                foreach (var cluster in clusters)
                {
                    // Perpendicular extent of this cluster
                    var perpPositions = cluster
                        .Select(w => { var wlc = w.Location as LocationCurve; if (wlc == null) return (double?)null; XYZ m = wlc.Curve.Evaluate(0.5, true); return (double?)(isHorizontal ? m.Y : m.X); })
                        .Where(v => v.HasValue).Select(v => v.Value).ToList();
                    if (perpPositions.Count == 0) continue;

                    double clusterMin = perpPositions.Min();
                    double clusterMax = perpPositions.Max();

                    // DepthDistance: if set, skip clusters wider than the limit
                    if (settings.DepthDistance > 0.0 && (clusterMax - clusterMin) > settings.DepthDistance)
                        continue;

                    // Collect interior face refs for this cluster
                    var entries = CollectInteriorFaceRefs(
                        cluster, measureDir, isHorizontal,
                        clusterMin, clusterMax, settings, geomOpts);

                    if (entries.Count < 2)
                    {
                        sk++;
                        skips.Add($"Pass3 {(isHorizontal ? "H" : "V")} cluster@{clusterMin:F1}: <2 refs.");
                        continue;
                    }

                    var sorted = entries.OrderBy(e => e.Pos).ToList();

                    // Minimum segment filter
                    if (sorted.Count > 2)
                    {
                        var filtered = new List<RefEntry> { sorted[0] };
                        for (int i = 1; i < sorted.Count - 1; i++)
                        {
                            double dP = sorted[i].Pos - filtered.Last().Pos;
                            double dN = sorted[i + 1].Pos - sorted[i].Pos;
                            if (dP >= minSegFt && dN >= minSegFt) filtered.Add(sorted[i]);
                        }
                        filtered.Add(sorted.Last());
                        sorted = filtered;
                    }

                    if (sorted.Count < 2) { sk++; continue; }

                    var ra = new ReferenceArray();
                    foreach (var e in sorted) if (e.Ref != null) ra.Append(e.Ref);
                    if (ra.Size < 2) { sk++; continue; }

                    // Dim line perpendicular position:
                    // InsetDistance (inset_mm) measured from the NEAR building edge.
                    // isHorizontal string: near edge is bottom (Min.Y) or top (Max.Y).
                    // Use building envelope edges, offset inward by InsetDistance.
                    // Pick the edge nearest to this cluster's centroid.
                    double clusterCenterPerp = (clusterMin + clusterMax) / 2.0;
                    double nearEdge, farEdge;
                    if (isHorizontal)
                    {
                        // Choose bottom or top based on which the cluster is closer to
                        double distToBottom = Math.Abs(clusterCenterPerp - envelope.Min.Y);
                        double distToTop = Math.Abs(clusterCenterPerp - envelope.Max.Y);
                        if (distToBottom <= distToTop)
                        { nearEdge = envelope.Min.Y; farEdge = envelope.Max.Y; }
                        else
                        { nearEdge = envelope.Max.Y; farEdge = envelope.Min.Y; }
                    }
                    else
                    {
                        double distToLeft = Math.Abs(clusterCenterPerp - envelope.Min.X);
                        double distToRight = Math.Abs(clusterCenterPerp - envelope.Max.X);
                        if (distToLeft <= distToRight)
                        { nearEdge = envelope.Min.X; farEdge = envelope.Max.X; }
                        else
                        { nearEdge = envelope.Max.X; farEdge = envelope.Min.X; }
                    }

                    // Inset from near edge toward far edge
                    double sign = (farEdge > nearEdge) ? 1.0 : -1.0;
                    double perpPos = nearEdge + sign * settings.InsetDistance;

                    const double linePad = 0.3;
                    double lineStart = sorted.First().Pos - linePad;
                    double lineEnd = sorted.Last().Pos + linePad;

                    XYZ p1 = isHorizontal
                        ? new XYZ(lineStart, perpPos, viewZ)
                        : new XYZ(perpPos, lineStart, viewZ);
                    XYZ p2 = isHorizontal
                        ? new XYZ(lineEnd, perpPos, viewZ)
                        : new XYZ(perpPos, lineEnd, viewZ);
                    if (p1.X > p2.X || p1.Y > p2.Y) { var tmp = p1; p1 = p2; p2 = tmp; }

                    try
                    {
                        Line dimLine = Line.CreateBound(p1, p2);
                        Dimension dim = _doc.Create.NewDimension(view, dimLine, ra, dimType);
                        if (dim != null) s++;
                        else { sk++; skips.Add($"Pass3 {(isHorizontal ? "H" : "V")}: NewDimension null."); }
                    }
                    catch (Exception ex)
                    {
                        f++;
                        errors.Add($"Pass3 {(isHorizontal ? "H" : "V")}: {ex.Message}");
                    }
                }
            }

            return (s, sk, f, errors, skips);
        }

        /// <summary>
        /// Collects one interior-facing face reference per wall in the cluster.
        /// clusterMin/Max define the perpendicular extent of the zone being processed.
        /// Interior face = face position closest to the cluster's centre along measureDir.
        /// </summary>
        private List<RefEntry> CollectInteriorFaceRefs(
            List<Wall> candidates, XYZ measureDir,
            bool isHorizontal, double clusterMin, double clusterMax,
            DimSettings settings, Options geomOpts)
        {
            var entries = new List<RefEntry>();
            double centerPerp = (clusterMin + clusterMax) / 2.0; // cluster centre along measureDir

            foreach (Wall wall in candidates)
            {
                try
                {
                    var wlc = wall.Location as LocationCurve;
                    if (wlc == null) continue;

                    // Collect all faces with normal ≈ ±measureDir
                    GeometryElement geo = wall.get_Geometry(geomOpts);
                    if (geo == null) continue;

                    var wallFaces = new List<(double pos, Reference fref)>();
                    foreach (GeometryObject obj in geo)
                    {
                        Solid solid = null;
                        if (obj is Solid ss && ss.Volume > 0) solid = ss;
                        else if (obj is GeometryInstance gi)
                            foreach (GeometryObject sub in gi.GetInstanceGeometry())
                                if (sub is Solid s2 && s2.Volume > 0) solid = s2;
                        if (solid == null) continue;
                        foreach (Face face in solid.Faces)
                        {
                            if (!(face is PlanarFace pf) || pf.Reference == null) continue;
                            if (Math.Abs(pf.FaceNormal.DotProduct(measureDir)) < 0.8) continue;
                            XYZ cen = DimHelpers.FaceCentroid(pf);
                            double pos = isHorizontal ? cen.X : cen.Y;
                            wallFaces.Add((pos, pf.Reference));
                        }
                    }

                    if (wallFaces.Count == 0) continue;

                    // Interior face = closest to cluster centre along measureDir
                    var chosen = wallFaces.OrderBy(f => Math.Abs(f.pos - centerPerp)).First();
                    if (!entries.Any(e => Math.Abs(e.Pos - chosen.pos) < 0.2))
                    {
                        entries.Add(new RefEntry
                        {
                            Ref = chosen.fref,
                            Pos = chosen.pos,
                            Kind = "interior",
                            Pt = new XYZ(
                                isHorizontal ? chosen.pos : centerPerp,
                                isHorizontal ? centerPerp : chosen.pos, 0),
                            Wall = wall
                        });
                    }
                }
                catch { }
            }

            return entries;
        }
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