using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace A49AIRevitAssistant.Executor.Commands.DimStrategies
{
    public class WallDimStrategy : IDimStrategy
    {
        public string StrategyName => "WallDimStrategy";

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
                var (startCap, endCap) = DimHelpers.GetWallEndCapRefs(wall);
                if (startCap == null) return DimResult.Skipped($"{tag}: startCap null.");
                if (endCap == null) return DimResult.Skipped($"{tag}: endCap null.");

                var gridRefs = new List<TaggedRef>();
                if (request.IncludeGrids && context.AllGridsInView.Count > 0)
                    gridRefs = DimHelpers.GetGridRefs(wall, context.AllGridsInView);

                List<TaggedRef> baseRefs;
                if (gridRefs.Count > 0)
                {
                    baseRefs = DimHelpers.MergeEndCapsWithGrids(
                        startCap, endCap, gridRefs, 10.0);  // 10.0 ft search distance
                }
                else
                {
                    baseRefs = new List<TaggedRef> { startCap, endCap };
                }

                if (request.IncludeOpenings)
                {
                    var openingRefs = DimHelpers.GetOpeningEdgeRefs(
                        wall, doc, startCap.U, endCap.U);
                    baseRefs.AddRange(openingRefs);
                }

                if (baseRefs.Count < 2)
                    return DimResult.Skipped($"{tag}: fewer than 2 refs.");

                ReferenceArray refArray = DimHelpers.BuildOrderedRefArray(baseRefs);
                if (refArray.Size < 2)
                    return DimResult.Skipped($"{tag}: fewer than 2 refs after dedup.");

                XYZ offsetDir = DimHelpers.GetDimLineOffsetDirection(
                    wall, doc, view, request.SmartExteriorPlacement);

                Line dimLine = DimHelpers.BuildDimensionLine(
                    wall, offsetDir, request.OffsetDistance, baseRefs);

                if (dimLine == null)
                    return DimResult.Skipped($"{tag}: BuildDimensionLine null.");

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