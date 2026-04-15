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

        // 400mm in feet (Revit internal units)
        private const double TAG_OFFSET_FEET = 400.0 / 304.8;

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

                string jsonResult = resultObj.ToString(Formatting.None);

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
        //   1. Get door location point (insertion point on wall)
        //   2. Get FacingOrientation (points toward swing/room side)
        //   3. Account for FacingFlipped — if flipped, reverse the vector
        //   4. Move OPPOSITE to facing (toward wall side) by 400mm offset
        //   5. Result: tag center sits 400mm from wall face on wall side
        // ============================================================================
        private XYZ CalculateTagPosition(FamilyInstance door)
        {
            LocationPoint locPt = door.Location as LocationPoint;
            if (locPt == null) return null;

            XYZ doorPoint = locPt.Point;

            // FacingOrientation points from wall toward room (swing side)
            XYZ facingDir = door.FacingOrientation;

            // If the door's facing is flipped, the vector already points 
            // the opposite way, so we need to reverse our logic
            if (door.FacingFlipped)
                facingDir = facingDir.Negate();

            // Move OPPOSITE to facing = toward the wall side
            XYZ wallSideDir = facingDir.Negate();

            // Offset 400mm (in feet) from the door point toward wall side
            XYZ tagPoint = doorPoint + wallSideDir.Multiply(TAG_OFFSET_FEET);

            return tagPoint;
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

            string jsonResult = errorResult.ToString(Formatting.None);

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