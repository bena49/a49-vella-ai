// ============================================================================
// A49AIRevitAssistant/Executor/Commands/PreflightRepairCommand.cs
// ============================================================================
// Repairs issues found by PreflightCheckCommand:
//   1. Renames misnamed View Templates to correct names
//   2. Renames misnamed Titleblock types to correct names
//   3. Fixes misconfigured parameter values on existing View Templates
//   4. Transfers truly missing View Templates from master .rvt file
//   5. Transfers truly missing Titleblock families from master .rvt file
//
// Repair order: Rename first, then fix parameters, then transfer missing.
// This prevents duplicates from being created.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class PreflightRepairCommand
    {
        private readonly UIApplication _uiapp;

        public PreflightRepairCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        public string Execute(Models.RevitCommandEnvelope env)
        {
            try
            {
                Document doc = _uiapp.ActiveUIDocument.Document;

                // ─────────────────────────────────────────────────────
                // 1. PARSE REPAIR DATA FROM ENVELOPE
                // ─────────────────────────────────────────────────────
                JObject repairData = env.raw;
                if (repairData == null)
                {
                    return SendResult("error", "No repair data received.", null);
                }

                var standards = repairData["standards"] as JObject;
                var preflightResult = repairData["preflight_result"] as JObject;

                if (standards == null || preflightResult == null)
                {
                    return SendResult("error", "Invalid repair data: missing standards or preflight_result.", null);
                }

                // ─────────────────────────────────────────────────────
                // 2. DETERMINE MASTER TEMPLATE FILE PATH
                // ─────────────────────────────────────────────────────
                string fullVersion = _uiapp.Application.VersionNumber;
                string shortVersion = fullVersion.Length >= 4
                    ? fullVersion.Substring(2)
                    : fullVersion;

                var templateFileConfig = standards["template_file"] as JObject;
                string basePath = templateFileConfig?["base_path"]?.ToString() ?? "";
                string filePattern = templateFileConfig?["file_pattern"]?.ToString() ?? "A49_TEMPLATE_RVT{version}.rvt";

                string templateFileName = filePattern.Replace("{version}", shortVersion);
                string templateFilePath = Path.Combine(basePath, templateFileName);

                A49Logger.Log($"🔧 Preflight Repair: Master template path: {templateFilePath}");

                // ─────────────────────────────────────────────────────
                // 3. COLLECT WHAT NEEDS FIXING
                // ─────────────────────────────────────────────────────
                var vtResult = preflightResult["view_templates"] as JObject;
                var tbResult = preflightResult["titleblocks"] as JObject;

                var missingTemplates = vtResult?["missing"]?.ToObject<List<string>>() ?? new List<string>();
                var misconfigured = vtResult?["misconfigured"] as JArray ?? new JArray();
                var misnamedTemplates = vtResult?["misnamed"] as JArray ?? new JArray();

                var missingTitleblocks = tbResult?["missing"]?.ToObject<List<string>>() ?? new List<string>();
                var misnamedTitleblocks = tbResult?["misnamed"] as JArray ?? new JArray();

                int renamedTemplates = 0;
                int renamedTitleblocks = 0;
                int fixedParameters = 0;
                int transferredTemplates = 0;
                int transferredTitleblocks = 0;
                var errors = new List<string>();

                // Build standards lookup for expected parameter values
                var standardsLookup = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
                var standardTemplates = standards["view_templates"] as JArray;
                if (standardTemplates != null)
                {
                    foreach (JObject st in standardTemplates)
                    {
                        string name = st["name"]?.ToString();
                        if (!string.IsNullOrEmpty(name))
                            standardsLookup[name] = st;
                    }
                }

                // ─────────────────────────────────────────────────────
                // 4. RENAME MISNAMED VIEW TEMPLATES
                // ─────────────────────────────────────────────────────
                if (misnamedTemplates.Count > 0)
                {
                    A49Logger.Log($"🔧 Renaming {misnamedTemplates.Count} misnamed view templates...");

                    using (Transaction tx = new Transaction(doc, "Vella - Rename View Templates"))
                    {
                        tx.Start();

                        foreach (JObject item in misnamedTemplates)
                        {
                            string expectedName = item["expected"]?.ToString();
                            string currentName = item["current"]?.ToString();
                            long elementId = item["element_id"]?.ToObject<long>() ?? -1;

                            if (string.IsNullOrEmpty(expectedName) || elementId < 0) continue;

                            try
                            {
                                Element element = doc.GetElement(new ElementId((long)elementId));
                                if (element is View view && view.IsTemplate)
                                {
                                    view.Name = expectedName;
                                    A49Logger.Log($"  ✅ Renamed: '{currentName}' → '{expectedName}'");
                                    renamedTemplates++;

                                    // Also fix parameters if needed
                                    if (standardsLookup.TryGetValue(expectedName, out JObject expected))
                                    {
                                        bool paramFixed = false;
                                        paramFixed |= SetParameterValue(view, "DISCIPLINE", expected["discipline"]?.ToString());
                                        paramFixed |= SetParameterValue(view, "PROJECT PHASE", expected["project_phase"]?.ToString());
                                        paramFixed |= SetParameterValue(view, "VIEW SET", expected["view_set"]?.ToString());
                                        if (paramFixed) fixedParameters++;
                                    }
                                }
                                else
                                {
                                    errors.Add($"Element {elementId} is not a view template.");
                                }
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"Failed to rename '{currentName}': {ex.Message}");
                                A49Logger.Log($"  ❌ Rename failed for '{currentName}': {ex.Message}");
                            }
                        }

                        tx.Commit();
                    }
                }

                // ─────────────────────────────────────────────────────
                // 5. RENAME MISNAMED TITLEBLOCK TYPES
                // ─────────────────────────────────────────────────────
                if (misnamedTitleblocks.Count > 0)
                {
                    A49Logger.Log($"🔧 Renaming {misnamedTitleblocks.Count} misnamed titleblock types...");

                    using (Transaction tx = new Transaction(doc, "Vella - Rename Titleblock Types"))
                    {
                        tx.Start();

                        foreach (JObject item in misnamedTitleblocks)
                        {
                            string familyName = item["family"]?.ToString();
                            string expectedType = item["expected"]?.ToString();
                            string currentType = item["current"]?.ToString();
                            int elementId = item["element_id"]?.ToObject<int>() ?? -1;

                            if (string.IsNullOrEmpty(expectedType) || elementId < 0) continue;

                            try
                            {
                                Element element = doc.GetElement(new ElementId((long)elementId));
                                if (element is FamilySymbol symbol)
                                {
                                    symbol.Name = expectedType;
                                    A49Logger.Log($"  ✅ Renamed: '{familyName} : {currentType}' → '{familyName} : {expectedType}'");
                                    renamedTitleblocks++;
                                }
                                else
                                {
                                    errors.Add($"Element {elementId} is not a titleblock type.");
                                }
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"Failed to rename titleblock '{currentType}': {ex.Message}");
                                A49Logger.Log($"  ❌ Titleblock rename failed for '{currentType}': {ex.Message}");
                            }
                        }

                        tx.Commit();
                    }
                }

                // ─────────────────────────────────────────────────────
                // 6. FIX MISCONFIGURED PARAMETERS
                // ─────────────────────────────────────────────────────
                if (misconfigured.Count > 0)
                {
                    A49Logger.Log($"🔧 Fixing {misconfigured.Count} misconfigured templates...");

                    var allTemplates = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => v.IsTemplate)
                        .ToDictionary(v => v.Name, v => v, StringComparer.OrdinalIgnoreCase);

                    using (Transaction tx = new Transaction(doc, "Vella - Fix Template Parameters"))
                    {
                        tx.Start();

                        foreach (JObject item in misconfigured)
                        {
                            string templateName = item["name"]?.ToString();
                            if (string.IsNullOrEmpty(templateName)) continue;

                            if (!allTemplates.TryGetValue(templateName, out View template)) continue;
                            if (!standardsLookup.TryGetValue(templateName, out JObject expected)) continue;

                            bool fixed_any = false;
                            fixed_any |= SetParameterValue(template, "DISCIPLINE", expected["discipline"]?.ToString());
                            fixed_any |= SetParameterValue(template, "PROJECT PHASE", expected["project_phase"]?.ToString());
                            fixed_any |= SetParameterValue(template, "VIEW SET", expected["view_set"]?.ToString());

                            if (fixed_any) fixedParameters++;
                        }

                        tx.Commit();
                    }

                    A49Logger.Log($"✅ Fixed parameters on {fixedParameters} templates.");
                }

                // ─────────────────────────────────────────────────────
                // 7. TRANSFER TRULY MISSING ITEMS FROM MASTER FILE
                // ─────────────────────────────────────────────────────
                bool needsMasterFile = missingTemplates.Count > 0 || missingTitleblocks.Count > 0;

                if (needsMasterFile)
                {
                    if (!File.Exists(templateFilePath))
                    {
                        string errMsg = $"Master template file not found: {templateFilePath}";
                        A49Logger.Log($"❌ {errMsg}");
                        errors.Add(errMsg);
                    }
                    else
                    {
                        Document masterDoc = null;

                        try
                        {
                            A49Logger.Log("📂 Opening master template file...");
                            var openOptions = new OpenOptions();
                            openOptions.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;

                            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(templateFilePath);
                            masterDoc = _uiapp.Application.OpenDocumentFile(modelPath, openOptions);

                            if (masterDoc == null)
                            {
                                errors.Add("Failed to open master template file.");
                            }
                            else
                            {
                                // ── TRANSFER MISSING VIEW TEMPLATES ──
                                if (missingTemplates.Count > 0)
                                {
                                    A49Logger.Log($"🔧 Transferring {missingTemplates.Count} missing view templates...");

                                    var masterTemplates = new FilteredElementCollector(masterDoc)
                                        .OfClass(typeof(View))
                                        .Cast<View>()
                                        .Where(v => v.IsTemplate)
                                        .ToDictionary(v => v.Name, v => v, StringComparer.OrdinalIgnoreCase);

                                    var elementsToCopy = new List<ElementId>();

                                    foreach (string name in missingTemplates)
                                    {
                                        if (masterTemplates.TryGetValue(name, out View masterTemplate))
                                        {
                                            elementsToCopy.Add(masterTemplate.Id);
                                        }
                                        else
                                        {
                                            errors.Add($"Template '{name}' not found in master file.");
                                        }
                                    }

                                    if (elementsToCopy.Count > 0)
                                    {
                                        using (Transaction tx = new Transaction(doc, "Vella - Import Missing Templates"))
                                        {
                                            tx.Start();

                                            var copyOptions = new CopyPasteOptions();
                                            copyOptions.SetDuplicateTypeNamesHandler(new OverwriteDuplicateHandler());

                                            try
                                            {
                                                ElementTransformUtils.CopyElements(
                                                    masterDoc, elementsToCopy, doc,
                                                    Transform.Identity, copyOptions);
                                                transferredTemplates = elementsToCopy.Count;
                                                A49Logger.Log($"✅ Transferred {transferredTemplates} view templates.");
                                            }
                                            catch (Exception ex)
                                            {
                                                errors.Add($"Template transfer error: {ex.Message}");
                                            }

                                            tx.Commit();
                                        }
                                    }
                                }

                                // ── TRANSFER MISSING TITLEBLOCKS ──
                                if (missingTitleblocks.Count > 0)
                                {
                                    A49Logger.Log($"🔧 Transferring {missingTitleblocks.Count} missing titleblocks...");

                                    var neededFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    foreach (string entry in missingTitleblocks)
                                    {
                                        string familyName = entry.Split(new[] { " : " }, StringSplitOptions.None)[0].Trim();
                                        neededFamilies.Add(familyName);
                                    }

                                    var masterTBFamilies = new FilteredElementCollector(masterDoc)
                                        .OfClass(typeof(Family))
                                        .Cast<Family>()
                                        .Where(f => f.FamilyCategory != null &&
                                               f.FamilyCategory.Id.Value == (long)BuiltInCategory.OST_TitleBlocks &&
                                               neededFamilies.Contains(f.Name))
                                        .ToList();

                                    var tbElementsToCopy = new List<ElementId>();
                                    var tbNamesFound = new List<string>();

                                    foreach (var family in masterTBFamilies)
                                    {
                                        foreach (var symbolId in family.GetFamilySymbolIds())
                                        {
                                            tbElementsToCopy.Add(symbolId);
                                        }
                                        tbNamesFound.Add(family.Name);
                                    }

                                    foreach (string needed in neededFamilies)
                                    {
                                        if (!tbNamesFound.Any(n => string.Equals(n, needed, StringComparison.OrdinalIgnoreCase)))
                                        {
                                            errors.Add($"Titleblock family '{needed}' not found in master file.");
                                        }
                                    }

                                    if (tbElementsToCopy.Count > 0)
                                    {
                                        using (Transaction tx = new Transaction(doc, "Vella - Import Missing Titleblocks"))
                                        {
                                            tx.Start();

                                            var copyOptions = new CopyPasteOptions();
                                            copyOptions.SetDuplicateTypeNamesHandler(new OverwriteDuplicateHandler());

                                            try
                                            {
                                                ElementTransformUtils.CopyElements(
                                                    masterDoc, tbElementsToCopy, doc,
                                                    Transform.Identity, copyOptions);
                                                transferredTitleblocks = missingTitleblocks.Count;
                                                A49Logger.Log($"✅ Transferred {tbNamesFound.Count} titleblock families.");
                                            }
                                            catch (Exception ex)
                                            {
                                                errors.Add($"Titleblock transfer error: {ex.Message}");
                                            }

                                            tx.Commit();
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Error opening master file: {ex.Message}");
                            A49Logger.Log($"❌ Error opening master file: {ex.Message}");
                        }
                        finally
                        {
                            if (masterDoc != null && masterDoc.IsValidObject)
                            {
                                masterDoc.Close(false);
                                A49Logger.Log("📂 Master template file closed.");
                            }
                        }
                    }
                }

                // ─────────────────────────────────────────────────────
                // 8. BUILD REPAIR REPORT
                // ─────────────────────────────────────────────────────
                int totalFixed = renamedTemplates + renamedTitleblocks + fixedParameters + transferredTemplates + transferredTitleblocks;

                var summary = new
                {
                    templates_renamed = renamedTemplates,
                    titleblocks_renamed = renamedTitleblocks,
                    parameters_fixed = fixedParameters,
                    templates_transferred = transferredTemplates,
                    titleblocks_transferred = transferredTitleblocks,
                    errors = errors
                };

                string status = errors.Count > 0 ? "completed_with_errors" : "success";
                string message = errors.Count > 0
                    ? $"Repair completed with {errors.Count} error(s). {totalFixed} issue(s) fixed."
                    : $"All {totalFixed} issue(s) fixed successfully!";

                A49Logger.Log($"🏁 Preflight Repair complete: {totalFixed} fixed, {errors.Count} errors");

                return SendResult(status, message, summary);
            }
            catch (Exception ex)
            {
                A49Logger.Log($"❌ PreflightRepairCommand error: {ex.Message}");
                return SendResult("error", $"Preflight Repair failed: {ex.Message}", null);
            }
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private bool SetParameterValue(Element element, string paramName, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            foreach (Parameter p in element.Parameters)
            {
                if (p.Definition != null &&
                    string.Equals(p.Definition.Name, paramName, StringComparison.Ordinal) &&
                    !p.IsReadOnly)
                {
                    if (p.StorageType == StorageType.String)
                    {
                        string current = p.AsString() ?? "";
                        if (!string.Equals(current, value, StringComparison.Ordinal))
                        {
                            p.Set(value);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private string SendResult(string status, string message, object summary)
        {
            var result = new
            {
                preflight_repair_result = new
                {
                    status = status,
                    message = message,
                    summary = summary
                }
            };

            string jsonResult = JsonConvert.SerializeObject(result);

            try
            {
                A49AIRevitAssistant.UI.DockablePaneViewer.Instance.Dispatcher.Invoke(() =>
                {
                    A49AIRevitAssistant.UI.DockablePaneViewer.Instance.SendRawMessage(jsonResult);
                });
            }
            catch (Exception ex)
            {
                A49Logger.Log($"❌ Failed to send repair result to Vue: {ex.Message}");
            }

            return "{\"status\":\"silent\"}";
        }

        // =====================================================================
        // DUPLICATE TYPE NAME HANDLER
        // =====================================================================
        private class OverwriteDuplicateHandler : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
            {
                return DuplicateTypeAction.UseDestinationTypes;
            }
        }
    }
}
