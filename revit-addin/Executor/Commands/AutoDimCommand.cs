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

    /// <summary>
    /// Orchestrates automatic dimensioning for one or more floor-plan views.
    /// Receives a JSON payload from automate_dim.py via the envelope builder.
    ///
    /// Payload shape:
    /// {
    ///   "view_ids":               [123456, 789012],
    ///   "include_openings":       true,
    ///   "include_grids":          true,
    ///   "offset_mm":              800,
    ///   "smart_exterior":         true,
    ///   "dim_type_name":          "A49_Linear"
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
        //  Execute
        // ------------------------------------------------------------------

        public string Execute(string jsonPayload)
        {
            JObject payload;
            try { payload = JObject.Parse(jsonPayload); }
            catch (Exception ex) { return Error($"Invalid JSON payload: {ex.Message}"); }

            try
            {
                // ── 1. Parse settings ────────────────────────────────────

                DimSettings settings = ParseSettings(payload);

                // ── 2. Collect view ids ──────────────────────────────────

                var viewIdTokens = payload["view_ids"] as JArray;
                if (viewIdTokens == null || viewIdTokens.Count == 0)
                    return Error("No view_ids provided in payload.");

                var viewIds = new List<long>();
                foreach (var token in viewIdTokens)
                    if (long.TryParse(token.ToString(), out long vid))
                        viewIds.Add(vid);

                if (viewIds.Count == 0)
                    return Error("Could not parse any valid view_ids.");

                // ── 3. Resolve DimensionType ─────────────────────────────

                string dimTypeName = payload.Value<string>("dim_type_name") ?? "";
                DimensionType dimType = ResolveDimensionType(dimTypeName);
                if (dimType == null)
                    return Error($"No linear DimensionType found. " +
                                 $"Tried '{dimTypeName}'. Please check your project.");

                // ── 4. Process each view ─────────────────────────────────

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
                        allErrors.Add($"View {viewIdLong}: not found.");
                        totalFailed++;
                        continue;
                    }

                    // Accept all plan family view types
                    var planTypes = new[] {
                        ViewType.FloorPlan, ViewType.CeilingPlan,
                        ViewType.EngineeringPlan, ViewType.AreaPlan
                    };
                    if (!planTypes.Contains(view.ViewType))
                    {
                        allSkipReasons.Add($"View '{view.Name}': not a plan view (type={view.ViewType}).");
                        totalSkipped++;
                        continue;
                    }

                    // Build request for this view
                    var request = new DimRequest
                    {
                        TargetView = view,
                        WallIds = new List<ElementId>(),
                        IncludeOpenings = settings.IncludeOpenings,
                        IncludeGrids = settings.IncludeGrids,
                        OffsetDistance = settings.OffsetDistance,
                        SmartExteriorPlacement = settings.SmartExteriorPlacement,
                    };

                    List<Wall> wallsInView = CollectWallsInView(view);
                    List<Grid> gridsInView = settings.IncludeGrids
                        ? CollectGridsInView(view)
                        : new List<Grid>();

                    if (wallsInView.Count == 0)
                    {
                        allSkipReasons.Add($"View '{view.Name}': no straight walls found.");
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
                        LinearDimensionType = dimType,
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
                                allSkipReasons.Add(
                                    $"Wall {wall.Id} ({wall.WallType?.Name}): " +
                                    $"CanDimension=false (ViewType={view.ViewType}).");
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
                                allErrors.Add($"Wall {wall.Id}: {result.ErrorMessage}");
                            }
                            else
                            {
                                skipped++;
                                allSkipReasons.Add(
                                    $"Wall {wall.Id}: {result.SkipReason ?? "No reason given"}");
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

                // ── 5. Return result ─────────────────────────────────────

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
            catch (Exception ex)
            {
                return Error($"Unexpected error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        //  Settings
        // ------------------------------------------------------------------

        private class DimSettings
        {
            public bool IncludeOpenings { get; set; }
            public bool IncludeGrids { get; set; }
            public double OffsetDistance { get; set; }
            public bool SmartExteriorPlacement { get; set; }
        }

        private static DimSettings ParseSettings(JObject payload)
        {
            double offsetMm = payload.Value<double?>("offset_mm") ?? 800.0;

            return new DimSettings
            {
                IncludeOpenings = payload.Value<bool?>("include_openings") ?? true,
                IncludeGrids = payload.Value<bool?>("include_grids") ?? true,
                OffsetDistance = offsetMm / 304.8,
                SmartExteriorPlacement = payload.Value<bool?>("smart_exterior") ?? true,
            };
        }

        // ------------------------------------------------------------------
        //  Collectors
        // ------------------------------------------------------------------

        private List<Wall> CollectWallsInView(View view)
        {
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
            // Try by name first
            if (!string.IsNullOrEmpty(name))
            {
                var match = new FilteredElementCollector(_doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .FirstOrDefault(dt =>
                        dt.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            // Fall back to first available linear type
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .FirstOrDefault(dt => dt.StyleType == DimensionStyleType.Linear);
        }

        // ------------------------------------------------------------------
        //  Error helper
        // ------------------------------------------------------------------

        private static string Error(string message) =>
            JsonConvert.SerializeObject(new
            {
                status = "error",
                command = "auto_dim",
                message,
            });
    }
}
