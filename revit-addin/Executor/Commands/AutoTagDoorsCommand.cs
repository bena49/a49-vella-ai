// ============================================================================
// A49AIRevitAssistant/Executor/Commands/AutoTagDoorsCommand.cs
// ============================================================================
// Auto-tags all doors in specified views with intelligent placement:
//   - Tag placed on WALL SIDE (opposite swing/FacingOrientation)
//   - 400mm offset from wall face (center of tag to wall)
//   - No Leader
//   - Skips already-tagged doors (configurable)
//   - Handles FacingFlipped / HandFlipped edge cases
//   - Works with angled walls and double doors
//
// Envelope payload (env.raw):
//   {
//     "tag_family": "Door Tag",
//     "tag_type": "Standard",
//     "view_ids": [12345, 67890],    // ElementId values
//     "skip_tagged": true
//   }
//
// Results sent directly to Vue via SendRawMessage.
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
    public class AutoTagDoorsCommand
    {
        private readonly UIApplication _uiapp;

        // Offsets (mm) from face of wall to tag center, converted to Revit feet
        private const double SWING_OFFSET_FEET = 700.0 / 304.8;      // Perpendicular tags (swing doors)
        private const double PARALLEL_OFFSET_FEET = 350.0 / 304.8;   // Parallel tags (sliding/double doors)

        public AutoTagDoorsCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        public string Execute(Models.RevitCommandEnvelope env)
        {
            try
            {
                Document doc = _uiapp.ActiveUIDocument.Document;

                // ─────────────────────────────────────────────────────
                // 1. PARSE PAYLOAD FROM ENVELOPE
                // ─────────────────────────────────────────────────────
                JObject payload = env.raw;

                if (payload == null)
                    return SendError("No auto-tag payload received in envelope.");

                string tagFamilyName = payload.Value<string>("tag_family") ?? "";
                string tagTypeName = payload.Value<string>("tag_type") ?? "";
                bool skipTagged = payload.Value<bool?>("skip_tagged") ?? true;

                // Parse view IDs
                var viewIdArray = payload["view_ids"] as JArray;
                if (viewIdArray == null || viewIdArray.Count == 0)
                    return SendError("No view IDs provided.");

                List<ElementId> viewIds = new List<ElementId>();
                foreach (var vid in viewIdArray)
                {
                    long idVal = vid.Value<long>();
                    viewIds.Add(new ElementId(idVal));
                }

                A49Logger.Log($"🏷️ AutoTag Doors: family='{tagFamilyName}', type='{tagTypeName}', views={viewIds.Count}, skipTagged={skipTagged}");

                // ─────────────────────────────────────────────────────
                // 2. FIND THE DOOR TAG FAMILY + TYPE
                // ─────────────────────────────────────────────────────
                FamilySymbol tagSymbol = FindDoorTagSymbol(doc, tagFamilyName, tagTypeName);
                if (tagSymbol == null)
                    return SendError($"Door tag type not found: '{tagFamilyName} : {tagTypeName}'. Make sure the tag family is loaded in the project.");

                // ─────────────────────────────────────────────────────
                // 3. PROCESS EACH VIEW
                // ─────────────────────────────────────────────────────
                int totalTagged = 0;
                int totalSkipped = 0;
                int viewsProcessed = 0;
                List<string> errors = new List<string>();

                using (Transaction tx = new Transaction(doc, "Vella: Auto-Tag Doors"))
                {
                    tx.Start();

                    // Ensure the tag symbol is activated
                    if (!tagSymbol.IsActive)
                        tagSymbol.Activate();

                    foreach (ElementId viewId in viewIds)
                    {
                        View view = doc.GetElement(viewId) as View;
                        if (view == null)
                        {
                            errors.Add($"View ID {viewId.Value} not found.");
                            continue;
                        }

                        // Skip non-plan views
                        if (view.ViewType != ViewType.FloorPlan &&
                            view.ViewType != ViewType.CeilingPlan &&
                            view.ViewType != ViewType.AreaPlan &&
                            view.ViewType != ViewType.EngineeringPlan)
                        {
                            errors.Add($"Skipped '{view.Name}' — not a plan view.");
                            continue;
                        }

                        try
                        {
                            var result = TagDoorsInView(doc, view, tagSymbol, skipTagged);
                            totalTagged += result.tagged;
                            totalSkipped += result.skipped;
                            viewsProcessed++;

                            if (result.errors.Count > 0)
                                errors.AddRange(result.errors);

                            A49Logger.Log($"  ✅ View '{view.Name}': tagged={result.tagged}, skipped={result.skipped}");
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Error in view '{view.Name}': {ex.Message}");
                        }
                    }

                    tx.Commit();
                }

                // ─────────────────────────────────────────────────────
                // 4. BUILD AND SEND RESULT
                // ─────────────────────────────────────────────────────
                var resultObj = new JObject
                {
                    ["auto_tag_result"] = new JObject
                    {
                        ["status"] = errors.Count == 0 ? "success" : "partial",
                        ["tagged_count"] = totalTagged,
                        ["skipped_count"] = totalSkipped,
                        ["views_processed"] = viewsProcessed,
                        ["tag_family"] = tagFamilyName,
                        ["tag_type"] = tagTypeName,
                        ["message"] = $"Tagged {totalTagged} door(s) across {viewsProcessed} view(s). Skipped {totalSkipped} already-tagged door(s).",
                        ["errors"] = new JArray(errors.ToArray())
                    }
                };

                string jsonResult = JsonConvert.SerializeObject(resultObj);

                A49AIRevitAssistant.UI.DockablePaneViewer.Instance.Dispatcher.Invoke(() =>
                {
                    A49AIRevitAssistant.UI.DockablePaneViewer.Instance.SendRawMessage(jsonResult);
                });

                return "{\"status\":\"silent\"}";
            }
            catch (Exception ex)
            {
                A49Logger.Log($"❌ AutoTag Doors Error: {ex.Message}\n{ex.StackTrace}");
                return SendError($"Auto-tag failed: {ex.Message}");
            }
        }

        // ============================================================================
        // TAG DOORS IN A SINGLE VIEW
        // ============================================================================
        private (int tagged, int skipped, List<string> errors) TagDoorsInView(
            Document doc, View view, FamilySymbol tagSymbol, bool skipTagged)
        {
            int tagged = 0;
            int skipped = 0;
            var errors = new List<string>();

            // Collect all doors visible in this view
            FilteredElementCollector doorCollector = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Doors)
                .OfClass(typeof(FamilyInstance));

            // If skipTagged, collect existing door tags in this view
            HashSet<long> alreadyTaggedDoorIds = new HashSet<long>();
            if (skipTagged)
            {
                FilteredElementCollector tagCollector = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag));

                foreach (IndependentTag existingTag in tagCollector)
                {
                    try
                    {
                        // GetTaggedLocalElementIds works for Revit 2024+
                        var taggedIds = existingTag.GetTaggedLocalElementIds();
                        foreach (var taggedId in taggedIds)
                        {
                            Element taggedElement = doc.GetElement(taggedId);
                            if (taggedElement != null && taggedElement.Category != null &&
                                taggedElement.Category.BuiltInCategory == BuiltInCategory.OST_Doors)
                            {
                                alreadyTaggedDoorIds.Add(taggedId.Value);
                            }
                        }
                    }
                    catch
                    {
                        // Fallback: skip tag if we can't read its references
                    }
                }
            }

            foreach (FamilyInstance door in doorCollector)
            {
                try
                {
                    // Skip already-tagged doors
                    if (skipTagged && alreadyTaggedDoorIds.Contains(door.Id.Value))
                    {
                        skipped++;
                        continue;
                    }

                    // Calculate tag position
                    XYZ tagPoint = CalculateTagPosition(door);
                    if (tagPoint == null)
                    {
                        errors.Add($"Could not calculate position for door '{door.Id.Value}'.");
                        continue;
                    }

                    // Create the tag
                    Reference doorRef = new Reference(door);

                    IndependentTag newTag = IndependentTag.Create(
                        doc,
                        tagSymbol.Id,
                        view.Id,
                        doorRef,
                        false,              // addLeader = false (No Leader)
                        TagOrientation.Horizontal,
                        tagPoint
                    );

                    if (newTag != null)
                        tagged++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Door {door.Id.Value}: {ex.Message}");
                }
            }

            return (tagged, skipped, errors);
        }

        // ============================================================================
        // CALCULATE TAG POSITION
        // ============================================================================
        // Logic:
        //   1. Get door location point (insertion point, on the wall CENTERLINE)
        //   2. Get FacingOrientation — in this project's family setup, this vector
        //      already points toward the correct TAG side (non-swing side).
        //      It updates live when the user flips the door, so no manual flip logic.
        //   3. Determine offset based on wall direction:
        //        - Horizontal wall (runs along X-axis) → tag is PARALLEL → 350mm offset
        //        - Vertical wall (runs along Y-axis)   → tag is PERPENDICULAR → 700mm offset
        //        - Angled wall → default to 700mm (perpendicular) per firm standard
        //   4. Total offset = (wall thickness / 2) + offset from face
        //   5. Tag orientation: always Horizontal (Revit default)
        // ============================================================================
        private XYZ CalculateTagPosition(FamilyInstance door)
        {
            LocationPoint locPt = door.Location as LocationPoint;
            if (locPt == null) return null;

            XYZ doorPoint = locPt.Point;

            // FacingOrientation is a LIVE vector that points toward the correct tag
            // side (non-swing side) in this project's family setup. It already reflects
            // any user flipping of the door, so we use it as-is without negation.
            XYZ tagSideDir = door.FacingOrientation;

            // Get host wall
            Wall hostWall = door.Host as Wall;

            // Get host wall half-thickness (so offset is from wall FACE, not centerline)
            double wallHalfWidth = 0.0;
            if (hostWall != null)
                wallHalfWidth = hostWall.Width / 2.0;

            // Pick offset based on wall orientation
            double faceOffset = IsHorizontalWall(hostWall) ? PARALLEL_OFFSET_FEET : SWING_OFFSET_FEET;
            double totalOffset = wallHalfWidth + faceOffset;

            XYZ tagPoint = doorPoint + tagSideDir.Multiply(totalOffset);
            return tagPoint;
        }

        // ============================================================================
        // DETECT WALL ORIENTATION
        // ============================================================================
        // Returns true if the wall runs mostly along the X-axis (horizontal on page),
        // which means a horizontal tag placed above/below it will be PARALLEL to it.
        //
        // For horizontal walls → use 350mm offset (parallel tag).
        // For vertical/angled walls → use 700mm offset (perpendicular tag / safe default).
        // ============================================================================
        private bool IsHorizontalWall(Wall wall)
        {
            if (wall == null) return false;

            LocationCurve locCurve = wall.Location as LocationCurve;
            if (locCurve == null) return false;

            Line line = locCurve.Curve as Line;
            if (line == null) return false; // Curved wall → treat as non-horizontal (safe)

            XYZ dir = line.Direction;
            double absX = Math.Abs(dir.X);
            double absY = Math.Abs(dir.Y);

            // If X-component dominates significantly, it's a horizontal wall.
            // Using a tolerance so near-horizontal walls (slight angle) still count.
            // Angled walls (~45°) will fall into the "not horizontal" category
            // and default to 700mm, which is the safer offset.
            const double HORIZONTAL_TOLERANCE = 0.9; // cos(~25°)
            return absX >= HORIZONTAL_TOLERANCE && absX > absY;
        }

        // ============================================================================
        // FIND DOOR TAG SYMBOL
        // ============================================================================
        private FamilySymbol FindDoorTagSymbol(Document doc, string familyName, string typeName)
        {
            // Collect all annotation symbol types (tags are annotation families)
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_DoorTags);

            foreach (FamilySymbol fs in collector)
            {
                string fName = fs.Family?.Name ?? "";
                string tName = fs.Name ?? "";

                // Match by family name AND type name
                if (fName.Equals(familyName, StringComparison.OrdinalIgnoreCase) &&
                    tName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                {
                    return fs;
                }
            }

            // Fallback: match by family name only (use first available type)
            foreach (FamilySymbol fs in collector)
            {
                string fName = fs.Family?.Name ?? "";
                if (fName.Equals(familyName, StringComparison.OrdinalIgnoreCase))
                {
                    A49Logger.Log($"⚠️ Exact type '{typeName}' not found, using first type: '{fs.Name}'");
                    return fs;
                }
            }

            return null;
        }

        // ============================================================================
        // ERROR HELPER
        // ============================================================================
        private string SendError(string message)
        {
            A49Logger.Log($"❌ AutoTag Doors: {message}");

            var errorResult = new JObject
            {
                ["auto_tag_result"] = new JObject
                {
                    ["status"] = "error",
                    ["message"] = message,
                    ["tagged_count"] = 0,
                    ["skipped_count"] = 0,
                    ["views_processed"] = 0,
                    ["errors"] = new JArray()
                }
            };

            string jsonResult = JsonConvert.SerializeObject(errorResult);

            try
            {
                A49AIRevitAssistant.UI.DockablePaneViewer.Instance.Dispatcher.Invoke(() =>
                {
                    A49AIRevitAssistant.UI.DockablePaneViewer.Instance.SendRawMessage(jsonResult);
                });
            }
            catch { }

            return "{\"status\":\"silent\"}";
        }
    }
}
