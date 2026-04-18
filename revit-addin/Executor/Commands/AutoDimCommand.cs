using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using A49AIRevitAssistant.Executor.Commands.DimStrategies;

namespace A49AIRevitAssistant.Executor.Commands
{

    /// <summary>
    /// Orchestrates automatic dimensioning for a floor-plan view.
    /// Mirrors AutoTagCommand.cs — receives a JSON payload from the backend,
    /// builds a DimContext, runs WallDimStrategy per wall, returns a JSON result.
    ///
    /// Expected JSON payload shape (from automate_dim.py envelope):
    /// {
    ///   "view_id":                  "3456789",
    ///   "wall_ids":                 [],            // empty = all walls in view
    ///   "include_openings":         true,
    ///   "include_intersecting":     true,
    ///   "include_grids":            true,
    ///   "offset_mm":                800,           // converted to feet internally
    ///   "smart_exterior_placement": true,
    ///   "dim_type_name":            "A49_Linear"   // name of DimensionType in project
    /// }
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

        // ------------------------------------------------------------------
        //  Execute — main entry point called by DraftingCommandExecutor
        // ------------------------------------------------------------------

        public string Execute(string jsonPayload)
        {
            JObject payload;
            try
            {
                payload = JObject.Parse(jsonPayload);
            }
            catch (Exception ex)
            {
                return Error($"Invalid JSON payload: {ex.Message}");
            }

            try
            {
                // ── 1. Parse request ─────────────────────────────────────

                DimRequest request = ParseRequest(payload, out string parseError);
                if (request == null)
                    return Error(parseError);

                // ── 2. Resolve view ──────────────────────────────────────

                View view = _doc.GetElement(request.TargetView.Id) as View;
                if (view == null)
                    return Error("Target view not found in document.");

                if (view.ViewType != ViewType.FloorPlan)
                    return Error("Dimensioning is only supported for Floor Plan views.");

                // ── 3. Resolve DimensionType ─────────────────────────────

                string dimTypeName = payload.Value<string>("dim_type_name") ?? "";
                DimensionType dimType = ResolveDimensionType(dimTypeName);
                if (dimType == null)
                    return Error($"DimensionType '{dimTypeName}' not found in project. " +
                                 "Please check the dimension type name.");

                // ── 4. Pre-collect walls and grids in view ───────────────

                List<Wall> wallsInView = CollectWallsInView(view, request.WallIds);
                List<Grid> gridsInView = request.IncludeGrids
                    ? CollectGridsInView(view)
                    : new List<Grid>();

                if (wallsInView.Count == 0)
                    return Error("No suitable walls found in the selected view.");

                // ── 5. Build context ─────────────────────────────────────

                var context = new DimContext
                {
                    Document = _doc,
                    Request = request,
                    AllWallsInView = wallsInView,
                    AllGridsInView = gridsInView,
                    LinearDimensionType = dimType
                };

                // ── 6. Initialise strategy ───────────────────────────────

                var strategy = new WallDimStrategy();

                // ── 7. Run in transaction ────────────────────────────────

                int succeeded = 0;
                int skipped = 0;
                int failed = 0;
                var skipReasons = new List<string>();
                var errors = new List<string>();

                using (var tx = new Transaction(_doc, "Vella AI — Auto Dimension Walls"))
                {
                    tx.Start();

                    foreach (Wall wall in wallsInView)
                    {
                        if (!strategy.CanDimension(wall, view))
                        {
                            skipped++;
                            skipReasons.Add($"Wall {wall.Id}: CanDimension returned false.");
                            continue;
                        }

                        DimResult result = strategy.Dimension(wall, context);

                        if (result.Success)
                        {
                            succeeded++;
                        }
                        else if (!string.IsNullOrEmpty(result.ErrorMessage))
                        {
                            failed++;
                            errors.Add($"Wall {wall.Id}: {result.ErrorMessage}");
                        }
                        else
                        {
                            skipped++;
                            if (!string.IsNullOrEmpty(result.SkipReason))
                                skipReasons.Add($"Wall {wall.Id}: {result.SkipReason}");
                        }
                    }

                    if (failed > 0 && succeeded == 0)
                    {
                        tx.RollBack();
                        return Error($"All walls failed. First error: {errors.FirstOrDefault()}");
                    }

                    tx.Commit();
                }

                // ── 8. Return result ─────────────────────────────────────

                return JsonConvert.SerializeObject(new
                {
                    status = "success",
                    command = "auto_dim",
                    total_walls = wallsInView.Count,
                    dimensioned = succeeded,
                    skipped = skipped,
                    failed = failed,
                    skip_reasons = skipReasons,
                    errors = errors
                });
            }
            catch (Exception ex)
            {
                return Error($"Unexpected error in AutoDimCommand: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        //  ParseRequest
        // ------------------------------------------------------------------

        private DimRequest ParseRequest(JObject payload, out string error)
        {
            error = null;

            // Resolve view
            string viewIdStr = payload.Value<string>("view_id");
            if (!long.TryParse(viewIdStr, out long viewIdLong))
            {
                error = $"Invalid view_id: '{viewIdStr}'";
                return null;
            }

            var viewId = new ElementId(viewIdLong);
            var view = _doc.GetElement(viewId) as View;
            if (view == null)
            {
                error = $"View with id {viewIdStr} not found.";
                return null;
            }

            // Optional explicit wall ids
            var wallIds = new List<ElementId>();
            var wallIdTokens = payload["wall_ids"] as JArray;
            if (wallIdTokens != null)
            {
                foreach (var token in wallIdTokens)
                {
                    if (long.TryParse(token.ToString(), out long wid))
                        wallIds.Add(new ElementId(wid));
                }
            }

            // Convert mm offset to feet (Revit internal units)
            double offsetMm = payload.Value<double?>("offset_mm") ?? 800.0;
            double offsetFt = offsetMm / 304.8;

            return new DimRequest
            {
                TargetView = view,
                WallIds = wallIds,
                IncludeOpenings = payload.Value<bool?>("include_openings") ?? true,
                IncludeIntersectingWalls = payload.Value<bool?>("include_intersecting") ?? true,
                IncludeGrids = payload.Value<bool?>("include_grids") ?? true,
                OffsetDistance = offsetFt,
                SmartExteriorPlacement = payload.Value<bool?>("smart_exterior_placement") ?? true
            };
        }

        // ------------------------------------------------------------------
        //  Element collectors
        // ------------------------------------------------------------------

        private List<Wall> CollectWallsInView(View view, List<ElementId> explicitIds)
        {
            if (explicitIds != null && explicitIds.Count > 0)
            {
                // Use only the walls the user explicitly selected
                return explicitIds
                    .Select(id => _doc.GetElement(id) as Wall)
                    .Where(w => w != null)
                    .ToList();
            }

            // Collect all walls visible in the view
            return new FilteredElementCollector(_doc, view.Id)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .Where(w => w.Location is LocationCurve lc && lc.Curve is Line)
                .ToList();
        }

        private List<Grid> CollectGridsInView(View view)
        {
            return new FilteredElementCollector(_doc, view.Id)
                .OfClass(typeof(Grid))
                .WhereElementIsNotElementType()
                .Cast<Grid>()
                .ToList();
        }

        // ------------------------------------------------------------------
        //  DimensionType resolver
        // ------------------------------------------------------------------

        private DimensionType ResolveDimensionType(string name)
        {
            // Try to find by name first
            if (!string.IsNullOrEmpty(name))
            {
                var match = new FilteredElementCollector(_doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .FirstOrDefault(dt =>
                        dt.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (match != null) return match;
            }

            // Fall back to the first available linear DimensionType
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .FirstOrDefault(dt =>
                    dt.StyleType == DimensionStyleType.Linear);
        }

        // ------------------------------------------------------------------
        //  Error helper
        // ------------------------------------------------------------------

        private static string Error(string message) =>
            JsonConvert.SerializeObject(new
            {
                status = "error",
                command = "auto_dim",
                message
            });
    }
}
