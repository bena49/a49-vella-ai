using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace A49AIRevitAssistant.Executor.Commands.DimStrategies
{
    /// <summary>
    /// Dimensions a single straight wall in a floor-plan view.
    /// Collects references for: wall end caps, opening edges,
    /// intersecting wall faces, and grid intersections — then
    /// calls doc.Create.NewDimension() once per wall.
    ///
    /// Placement follows A49 standard:
    ///   Exterior walls → dimension line placed outward from building perimeter
    ///   Interior walls → dimension line placed on the +normal side
    /// </summary>
    public class WallDimStrategy : IDimStrategy
    {
        public string StrategyName => "WallDimStrategy";

        // ------------------------------------------------------------------
        //  CanDimension — routing check
        // ------------------------------------------------------------------

        public bool CanDimension(Element element, View view)
        {
            if (!(element is Wall wall)) return false;

            // Only straight walls (not curved)
            if (!(wall.Location is LocationCurve lc)) return false;
            if (!(lc.Curve is Line)) return false;

            // Must be a floor-plan view
            if (view.ViewType != ViewType.FloorPlan) return false;

            return true;
        }

        // ------------------------------------------------------------------
        //  Dimension — main entry point
        // ------------------------------------------------------------------

        public DimResult Dimension(Element element, DimContext context)
        {
            if (!(element is Wall wall))
                return DimResult.Skipped("Element is not a Wall.");

            var doc = context.Document;
            var request = context.Request;
            var view = request.TargetView;

            try
            {
                // ── 1. Collect all references ────────────────────────────

                var allRefs = new List<Reference>();

                // 1a. Wall end-cap faces (always included — these are the outer bounds)
                var (startRef, endRef) = DimHelpers.GetWallEndFaceReferences(wall);
                if (startRef == null || endRef == null)
                    return DimResult.Skipped("Could not extract wall end-face references.");

                allRefs.Add(startRef);
                allRefs.Add(endRef);

                // 1b. Opening edges (doors + windows)
                if (request.IncludeOpenings)
                {
                    var openingRefs = DimHelpers.GetOpeningEdgeReferences(wall, doc);
                    allRefs.AddRange(openingRefs);
                }

                // 1c. Intersecting wall faces
                // NOTE: Disabled for now — intersecting wall face normals point in
                // a different direction to the target wall end-cap normals, causing
                // Revit's "References are no longer parallel" error in NewDimension().
                // This requires a separate dimension string per intersecting wall,
                // which is a future enhancement.
                // if (request.IncludeIntersectingWalls) { ... }

                // 1d. Grid intersections
                if (request.IncludeGrids)
                {
                    var gridRefs = DimHelpers.GetGridReferences(wall, context.AllGridsInView);
                    allRefs.AddRange(gridRefs);
                }

                // ── 2. Need at least 2 references to create a dimension ──

                // Deduplicate (same reference can appear from multiple passes)
                allRefs = DeduplicateReferences(allRefs);

                if (allRefs.Count < 2)
                    return DimResult.Skipped(
                        $"Only {allRefs.Count} reference(s) collected — need at least 2.");

                // ── 3. Order references along the wall axis ──────────────

                ReferenceArray refArray = DimHelpers.OrderReferencesAlongWall(
                    allRefs, wall, doc);

                // ── 4. Determine offset direction ────────────────────────

                XYZ offsetDir = DimHelpers.GetDimLineOffsetDirection(
                    wall, doc, view, request.SmartExteriorPlacement);

                // ── 5. Build the dimension line ──────────────────────────

                Line dimLine = DimHelpers.BuildDimensionLine(
                    wall, offsetDir, request.OffsetDistance, doc, view);

                if (dimLine == null)
                    return DimResult.Skipped("Could not build dimension line geometry.");

                // ── 6. Create the dimension ──────────────────────────────

                Dimension dim = doc.Create.NewDimension(
                    view,
                    dimLine,
                    refArray,
                    context.LinearDimensionType);

                if (dim == null)
                    return DimResult.Skipped("NewDimension() returned null.");

                return DimResult.Succeeded(dim, refArray.Size);
            }
            catch (Exception ex)
            {
                return DimResult.Failed($"[{StrategyName}] {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        //  Private helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Removes duplicate references by comparing ElementId + stable representation.
        /// NewDimension() will throw if the same reference appears twice.
        /// </summary>
        private static List<Reference> DeduplicateReferences(List<Reference> refs)
        {
            var seen = new HashSet<string>();
            var result = new List<Reference>();

            foreach (var r in refs)
            {
                // ConvertToStableRepresentation is the canonical uniqueness key
                string key;
                try { key = r.ConvertToStableRepresentation(null) ?? r.ElementId.ToString(); }
                catch { key = r.ElementId.ToString(); }

                if (seen.Add(key))
                    result.Add(r);
            }

            return result;
        }
    }
}
