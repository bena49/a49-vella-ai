using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace A49AIRevitAssistant.Executor.Commands.DimStrategies
{
    public class WallDimStrategy : IDimStrategy
    {
        public string StrategyName => "WallDimStrategy";

        // ── CanDimension ─────────────────────────────────────────────────────

        public bool CanDimension(Element element, View view)
        {
            if (!(element is Wall wall)) return false;
            if (!(wall.Location is LocationCurve lc)) return false;
            if (!(lc.Curve is Line)) return false;

            var planTypes = new[]
            {
                ViewType.FloorPlan, ViewType.CeilingPlan,
                ViewType.EngineeringPlan, ViewType.AreaPlan,
            };
            return planTypes.Contains(view.ViewType);
        }

        // ── Dimension ────────────────────────────────────────────────────────

        public DimResult Dimension(Element element, DimContext context)
        {
            if (!(element is Wall wall))
                return DimResult.Skipped("Not a wall.");

            var doc = context.Document;
            var request = context.Request;
            var view = request.TargetView;
            var cl = DimHelpers.GetWallCenterLine(wall);

            string tag = $"Wall {wall.Id} ({wall.WallType?.Name}, " +
                         $"len={Math.Round(cl?.Length ?? 0, 2)}ft)";

            try
            {
                // ── 1. End-cap references ─────────────────────────────────

                var (startCap, endCap) = DimHelpers.GetWallEndCapRefs(wall);
                if (startCap == null) return DimResult.Skipped($"{tag}: startCap null.");
                if (endCap == null) return DimResult.Skipped($"{tag}: endCap null.");

                // ── 2. Grid references ────────────────────────────────────

                var gridRefs = new List<TaggedRef>();
                if (request.IncludeGrids && context.AllGridsInView.Count > 0)
                    gridRefs = DimHelpers.GetGridRefs(wall, context.AllGridsInView);

                // ── 3. Merge: replace end-caps with nearby grids ──────────
                //
                // If a grid is within ~300mm of a wall end-cap, the grid
                // replaces the end-cap so the string terminates at the grid
                // line (outside corner) rather than the wall face (inside corner).

                List<TaggedRef> baseRefs;
                if (gridRefs.Count > 0)
                {
                    baseRefs = DimHelpers.MergeEndCapsWithGrids(
                        startCap, endCap, gridRefs, snapTol: 1.0);
                }
                else
                {
                    baseRefs = new List<TaggedRef> { startCap, endCap };
                }

                // ── 4. Opening edges ──────────────────────────────────────

                if (request.IncludeOpenings)
                {
                    var openingRefs = DimHelpers.GetOpeningEdgeRefs(
                        wall, doc, startCap.U, endCap.U);
                    baseRefs.AddRange(openingRefs);
                }

                // ── 5. Minimum refs check ─────────────────────────────────

                if (baseRefs.Count < 2)
                    return DimResult.Skipped($"{tag}: fewer than 2 refs.");

                // ── 6. Build ordered ReferenceArray ───────────────────────

                ReferenceArray refArray = DimHelpers.BuildOrderedRefArray(baseRefs);
                if (refArray.Size < 2)
                    return DimResult.Skipped($"{tag}: fewer than 2 refs after dedup.");

                // ── 7. Offset direction ───────────────────────────────────

                XYZ offsetDir = DimHelpers.GetDimLineOffsetDirection(
                    wall, doc, view, request.SmartExteriorPlacement);

                // ── 8. Dimension line spans full u-range ──────────────────

                double minU = baseRefs.Min(t => t.U);
                double maxU = baseRefs.Max(t => t.U);

                Line dimLine = DimHelpers.BuildDimensionLine(
                    wall, offsetDir, request.OffsetDistance, minU, maxU);

                if (dimLine == null)
                    return DimResult.Skipped($"{tag}: BuildDimensionLine null.");

                // ── 9. Create dimension ───────────────────────────────────

                Dimension dim = doc.Create.NewDimension(
                    view, dimLine, refArray, context.LinearDimensionType);

                if (dim == null)
                    return DimResult.Skipped($"{tag}: NewDimension() returned null.");

                return DimResult.Succeeded(dim, refArray.Size);
            }
            catch (Exception ex)
            {
                return DimResult.Failed($"{tag}: {ex.Message}");
            }
        }
    }
}
