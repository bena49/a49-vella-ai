using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace A49AIRevitAssistant.Executor.Commands.DimStrategies
{
    public class WallDimStrategy : IDimStrategy
    {
        public string StrategyName => "WallDimStrategy";

        // ------------------------------------------------------------------
        //  CanDimension
        // ------------------------------------------------------------------

        public bool CanDimension(Element element, View view)
        {
            if (!(element is Wall wall)) return false;
            if (!(wall.Location is LocationCurve lc)) return false;
            if (!(lc.Curve is Line)) return false;

            // Accept all plan-family view types
            var planTypes = new[]
            {
                ViewType.FloorPlan,
                ViewType.CeilingPlan,
                ViewType.EngineeringPlan,
                ViewType.AreaPlan,
            };
            return planTypes.Contains(view.ViewType);
        }

        // ------------------------------------------------------------------
        //  Dimension
        // ------------------------------------------------------------------

        public DimResult Dimension(Element element, DimContext context)
        {
            if (!(element is Wall wall))
                return DimResult.Skipped("Element is not a Wall.");

            var doc = context.Document;
            var request = context.Request;
            var view = request.TargetView;

            string wallInfo =
                $"Wall {wall.Id} ({wall.WallType?.Name ?? "?"}, " +
                $"len={Math.Round((DimHelpers.GetWallCenterLine(wall)?.Length ?? 0), 2)}ft)";

            try
            {
                // ── 1a. End-cap references ───────────────────────────────
                var (startRef, endRef) = DimHelpers.GetWallEndFaceReferences(wall);
                if (startRef == null)
                    return DimResult.Skipped($"{wallInfo}: startRef null — end-cap not found.");
                if (endRef == null)
                    return DimResult.Skipped($"{wallInfo}: endRef null — end-cap not found.");

                var allRefs = new List<Reference> { startRef, endRef };

                // ── 1b. Opening edges ────────────────────────────────────
                if (request.IncludeOpenings)
                    allRefs.AddRange(DimHelpers.GetOpeningEdgeReferences(wall, doc));

                // ── 1c. Grid references ──────────────────────────────────
                if (request.IncludeGrids)
                    allRefs.AddRange(DimHelpers.GetGridReferences(wall, context.AllGridsInView));

                // ── 2. Deduplicate ───────────────────────────────────────
                allRefs = Deduplicate(allRefs);
                if (allRefs.Count < 2)
                    return DimResult.Skipped(
                        $"{wallInfo}: Only {allRefs.Count} ref(s) after dedup.");

                // ── 3. Order along wall axis ─────────────────────────────
                ReferenceArray refArray =
                    DimHelpers.OrderReferencesAlongWall(allRefs, wall, doc);

                // ── 4. Offset direction ──────────────────────────────────
                XYZ offsetDir = DimHelpers.GetDimLineOffsetDirection(
                    wall, doc, view, request.SmartExteriorPlacement);

                // ── 5. Build dimension line ──────────────────────────────
                Line dimLine = DimHelpers.BuildDimensionLine(
                    wall, offsetDir, request.OffsetDistance);
                if (dimLine == null)
                    return DimResult.Skipped($"{wallInfo}: BuildDimensionLine returned null.");

                // ── 6. Create dimension ──────────────────────────────────
                Dimension dim = doc.Create.NewDimension(
                    view, dimLine, refArray, context.LinearDimensionType);

                if (dim == null)
                    return DimResult.Skipped($"{wallInfo}: NewDimension() returned null.");

                return DimResult.Succeeded(dim, refArray.Size);
            }
            catch (Exception ex)
            {
                return DimResult.Failed($"{wallInfo}: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        //  Deduplicate — index-based fallback prevents false collapse
        // ------------------------------------------------------------------

        private static List<Reference> Deduplicate(List<Reference> refs)
        {
            var seen = new HashSet<string>();
            var result = new List<Reference>();
            int idx = 0;

            foreach (var r in refs)
            {
                string key;
                try
                {
                    string stable = r.ConvertToStableRepresentation(null);
                    key = string.IsNullOrEmpty(stable)
                        ? $"idx_{idx}_{r.ElementId}"
                        : stable;
                }
                catch
                {
                    key = $"idx_{idx}_{r.ElementId}";
                }

                if (seen.Add(key)) result.Add(r);
                idx++;
            }

            return result;
        }
    }
}
