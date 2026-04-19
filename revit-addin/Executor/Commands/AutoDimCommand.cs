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
                // ── 1. Parse settings (everything except view ids) ───────────

                DimSettings settings = ParseSettings(payload, out string parseError);
                if (settings == null)
                    return Error(parseError);

                // ── 2. Collect view ids from payload ─────────────────────────
                // Payload sends "view_ids": [123, 456, 789]

                var viewIdTokens = payload["view_ids"] as JArray;
                if (viewIdTokens == null || viewIdTokens.Count == 0)
                    return Error("No view_ids provided in payload.");

                var viewIds = new List<long>();
                foreach (var token in viewIdTokens)
                {
                    if (long.TryParse(token.ToString(), out long vid))
                        viewIds.Add(vid);
                }

                if (viewIds.Count == 0)
                    return Error("Could not parse any valid view_ids from payload.");

                // ── 3. Resolve DimensionType once for all views ──────────────

                string dimTypeName = payload.Value<string>("dim_type_name") ?? "";
                DimensionType dimType = ResolveDimensionType(dimTypeName);
                if (dimType == null)
                    return Error($"DimensionType '{dimTypeName}' not found in project. " +
                                 "Please check the dimension type name.");

                // ── 4. Process each view ─────────────────────────────────────

                var strategy = new WallDimStrategy();
                int totalSucceeded = 0;
                int totalSkipped = 0;
                int totalFailed = 0;
                int totalWalls = 0;
                var allSkipReasons = new List<string>();
                var allErrors = new List<string>();

                foreach (long viewIdLong in viewIds)
                {
                    var viewId = new ElementId(viewIdLong);
                    var view = _doc.GetElement(viewId) as View;

                    if (view == null)
                    {
                        allErrors.Add($"View {viewIdLong}: not found in document.");
                        totalFailed++;
                        continue;
                    }

                    if (view.ViewType != ViewType.FloorPlan)
                    {
                        allSkipReasons.Add($"View '{view.Name}': not a Floor Plan — skipped.");
                        totalSkipped++;
                        continue;
                    }

                    // Build a DimRequest for this specific view
                    var request = new DimRequest
                    {
                        TargetView = view,
                        WallIds = new List<ElementId>(), // all walls in view
                        IncludeOpenings = settings.IncludeOpenings,
                        IncludeIntersectingWalls = settings.IncludeIntersectingWalls,
                        IncludeGrids = settings.IncludeGrids,
                        OffsetDistance = settings.OffsetDistance,
                        SmartExteriorPlacement = settings.SmartExteriorPlacement,
                    };

                    List<Wall> wallsInView = CollectWallsInView(view, request.WallIds);
                    List<Grid> gridsInView = request.IncludeGrids
                        ? CollectGridsInView(view)
                        : new List<Grid>();

                    if (wallsInView.Count == 0)
                    {
                        allSkipReasons.Add($"View '{view.Name}': no suitable walls found.");
                        totalSkipped++;
                        continue;
                    }

                    totalWalls += wallsInView.Count;

                    var context = new DimContext
                    {
                        Document = _doc,
                        Request = request,
                        AllWallsInView = wallsInView,
                        AllGridsInView = gridsInView,
                        LinearDimensionType = dimType
                    };

                    int succeeded = 0, skipped = 0, failed = 0;

                    using (var tx = new Transaction(_doc, $"Vella AI — Auto Dimension: {view.Name}"))
                    {
                        tx.Start();

                        foreach (Wall wall in wallsInView)
                        {
                            if (!strategy.CanDimension(wall, view))
                            {
                                skipped++;
                                continue;
                            }

                            DimResult result = strategy.Dimension(wall, context);

                            if (result.Success)
                                succeeded++;
                            else if (!string.IsNullOrEmpty(result.ErrorMessage))
                            {
                                failed++;
                                allErrors.Add($"View '{view.Name}' Wall {wall.Id}: {result.ErrorMessage}");
                            }
                            else
                            {
                                skipped++;
                                if (!string.IsNullOrEmpty(result.SkipReason))
                                    allSkipReasons.Add($"View '{view.Name}' Wall {wall.Id}: {result.SkipReason}");
                            }
                        }

                        if (failed > 0 && succeeded == 0)
                            tx.RollBack();
                        else
                            tx.Commit();
                    }

                    totalSucceeded += succeeded;
                    totalSkipped += skipped;
                    totalFailed += failed;
                }

                // ── 5. Return aggregated result ──────────────────────────────

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
                    errors = allErrors
                });
            }
            catch (Exception ex)
            {
                return Error($"Unexpected error in AutoDimCommand: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        //  DimSettings — lightweight settings-only struct (no view)
        // ------------------------------------------------------------------

        private class DimSettings
        {
            public bool IncludeOpenings { get; set; }
            public bool IncludeIntersectingWalls { get; set; }
            public bool IncludeGrids { get; set; }
            public double OffsetDistance { get; set; }
            public bool SmartExteriorPlacement { get; set; }
        }

        // ------------------------------------------------------------------
        //  ParseSettings — parses everything except view ids
        // ------------------------------------------------------------------

        private DimSettings ParseSettings(JObject payload, out string error)
        {
            error = null;

            double offsetMm = payload.Value<double?>("offset_mm") ?? 800.0;
            double offsetFt = offsetMm / 304.8;

            return new DimSettings
            {
                IncludeOpenings = payload.Value<bool?>("include_openings") ?? true,
                IncludeIntersectingWalls = payload.Value<bool?>("include_intersecting") ?? true,
                IncludeGrids = payload.Value<bool?>("include_grids") ?? true,
                OffsetDistance = offsetFt,
                SmartExteriorPlacement = payload.Value<bool?>("smart_exterior_placement") ?? true,
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
