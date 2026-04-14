// ============================================================================
// A49AIRevitAssistant/Executor/Commands/PreflightCheckCommand.cs
// ============================================================================
// Scans the active Revit document against A49 standards and reports:
//   1. Missing or misconfigured View Templates
//   2. Missing Titleblock families
//   3. Missing Project Parameters (DISCIPLINE, PROJECT PHASE, VIEW SET)
//
// Standards data is received in the envelope from Django (standards.json).
// Results are sent directly to Vue via SendRawMessage (same pattern as
// GetProjectInfoCommand / fetch_project_info).
// ============================================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class PreflightCheckCommand
    {
        private readonly UIApplication _uiapp;

        public PreflightCheckCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        public string Execute(Models.RevitCommandEnvelope env)
        {
            try
            {
                Document doc = _uiapp.ActiveUIDocument.Document;

                // ─────────────────────────────────────────────────────
                // 1. PARSE STANDARDS FROM ENVELOPE
                // ─────────────────────────────────────────────────────
                JObject standards = env.raw;

                if (standards == null)
                {
                    return SendError("No standards data received in envelope.");
                }

                // ─────────────────────────────────────────────────────
                // 2. DETECT REVIT VERSION
                // ─────────────────────────────────────────────────────
                string fullVersion = _uiapp.Application.VersionNumber; // e.g. "2024"
                string shortVersion = fullVersion.Length >= 4
                    ? fullVersion.Substring(2) // "24"
                    : fullVersion;
                string templateFileName = $"A49_TEMPLATE_RVT{shortVersion}.rvt";

                A49Logger.Log($"🔍 Preflight: Revit version {fullVersion}, template file: {templateFileName}");

                // ─────────────────────────────────────────────────────
                // 3. CHECK VIEW TEMPLATES
                // ─────────────────────────────────────────────────────
                var templateResult = CheckViewTemplates(doc, standards);

                // ─────────────────────────────────────────────────────
                // 4. CHECK TITLEBLOCKS
                // ─────────────────────────────────────────────────────
                var titleblockResult = CheckTitleblocks(doc, standards);

                // ─────────────────────────────────────────────────────
                // 5. CHECK PROJECT PARAMETERS
                // ─────────────────────────────────────────────────────
                var parameterResult = CheckProjectParameters(doc, standards);

                // ─────────────────────────────────────────────────────
                // 6. BUILD FINAL REPORT
                // ─────────────────────────────────────────────────────
                bool hasIssues = templateResult.Missing.Count > 0
                    || templateResult.Misconfigured.Count > 0
                    || titleblockResult.Missing.Count > 0
                    || parameterResult.Missing.Count > 0;

                var report = new
                {
                    preflight_result = new
                    {
                        status = hasIssues ? "issues_found" : "all_clear",
                        revit_version = fullVersion,
                        template_file = templateFileName,

                        view_templates = new
                        {
                            total_required = templateResult.TotalRequired,
                            present = templateResult.Present,
                            missing_count = templateResult.Missing.Count,
                            misconfigured_count = templateResult.Misconfigured.Count,
                            missing = templateResult.Missing,
                            misconfigured = templateResult.Misconfigured
                        },

                        titleblocks = new
                        {
                            total_required = titleblockResult.TotalRequired,
                            present = titleblockResult.Present,
                            missing_count = titleblockResult.Missing.Count,
                            missing = titleblockResult.Missing
                        },

                        project_parameters = new
                        {
                            total_required = parameterResult.TotalRequired,
                            present = parameterResult.Present,
                            missing_count = parameterResult.Missing.Count,
                            missing = parameterResult.Missing
                        }
                    }
                };

                string jsonResult = JsonConvert.SerializeObject(report);

                A49Logger.Log($"✅ Preflight complete: {templateResult.Present}/{templateResult.TotalRequired} templates, " +
                    $"{titleblockResult.Present}/{titleblockResult.TotalRequired} titleblocks, " +
                    $"{parameterResult.Present}/{parameterResult.TotalRequired} parameters");

                // ─────────────────────────────────────────────────────
                // 7. SEND DIRECTLY TO VUE (Same pattern as fetch_project_info)
                // ─────────────────────────────────────────────────────
                A49AIRevitAssistant.UI.DockablePaneViewer.Instance.Dispatcher.Invoke(() =>
                {
                    A49AIRevitAssistant.UI.DockablePaneViewer.Instance.SendRawMessage(jsonResult);
                });

                return "{\"status\":\"silent\"}";
            }
            catch (Exception ex)
            {
                A49Logger.Log($"❌ PreflightCheckCommand error: {ex.Message}");
                return $"❌ Preflight Check failed: {ex.Message}";
            }
        }

        // =====================================================================
        // VIEW TEMPLATE CHECKER
        // =====================================================================
        private TemplateCheckResult CheckViewTemplates(Document doc, JObject standards)
        {
            var result = new TemplateCheckResult();

            // Get all View Templates in the document
            var allTemplates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .ToList();

            // Build a lookup: template name -> View element
            var templateLookup = new Dictionary<string, View>(StringComparer.OrdinalIgnoreCase);
            foreach (var vt in allTemplates)
            {
                if (!templateLookup.ContainsKey(vt.Name))
                    templateLookup[vt.Name] = vt;
            }

            // Check each required template from standards
            var requiredTemplates = standards["view_templates"] as JArray;
            if (requiredTemplates == null) return result;

            result.TotalRequired = requiredTemplates.Count;

            foreach (JObject req in requiredTemplates)
            {
                string reqName = req["name"]?.ToString();
                if (string.IsNullOrEmpty(reqName)) continue;

                string expectedDiscipline = req["discipline"]?.ToString() ?? "";
                string expectedPhase = req["project_phase"]?.ToString() ?? "";
                string expectedViewSet = req["view_set"]?.ToString() ?? "";

                // Check if template exists
                if (!templateLookup.TryGetValue(reqName, out View foundTemplate))
                {
                    result.Missing.Add(reqName);
                    continue;
                }

                // Template exists — check parameter values
                var issues = new List<object>();

                CheckParameterValue(foundTemplate, "DISCIPLINE", expectedDiscipline, issues);
                CheckParameterValue(foundTemplate, "PROJECT PHASE", expectedPhase, issues);
                CheckParameterValue(foundTemplate, "VIEW SET", expectedViewSet, issues);

                if (issues.Count > 0)
                {
                    result.Misconfigured.Add(new
                    {
                        name = reqName,
                        issues = issues
                    });
                }
                else
                {
                    result.Present++;
                }
            }

            return result;
        }

        private void CheckParameterValue(View template, string paramName, string expected, List<object> issues)
        {
            if (string.IsNullOrEmpty(expected)) return;

            string actual = GetParameterValue(template, paramName);

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new
                {
                    parameter = paramName,
                    expected = expected,
                    actual = string.IsNullOrEmpty(actual) ? "(empty)" : actual
                });
            }
        }

        private string GetParameterValue(Element element, string paramName)
        {
            // Try by name lookup (works for Project Parameters)
            foreach (Parameter p in element.Parameters)
            {
                if (p.Definition != null &&
                    string.Equals(p.Definition.Name, paramName, StringComparison.Ordinal))
                {
                    if (p.HasValue)
                    {
                        return p.StorageType == StorageType.String
                            ? p.AsString() ?? ""
                            : p.AsValueString() ?? "";
                    }
                    return "";
                }
            }
            return "";
        }

        // =====================================================================
        // TITLEBLOCK CHECKER
        // =====================================================================
        private TitleblockCheckResult CheckTitleblocks(Document doc, JObject standards)
        {
            var result = new TitleblockCheckResult();

            // Get all loaded titleblock families with their types
            var titleblockFamilies = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.FamilyCategory != null &&
                       f.FamilyCategory.Id.Value == (int)BuiltInCategory.OST_TitleBlocks)
                .ToList();

            // Build lookup: family name -> list of type names
            var familyTypeLookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var family in titleblockFamilies)
            {
                var typeNames = new List<string>();
                foreach (var typeId in family.GetFamilySymbolIds())
                {
                    var symbol = doc.GetElement(typeId) as FamilySymbol;
                    if (symbol != null)
                        typeNames.Add(symbol.Name);
                }
                familyTypeLookup[family.Name] = typeNames;
            }

            var requiredTitleblocks = standards["titleblocks"] as JArray;
            if (requiredTitleblocks == null) return result;

            // Count total required = sum of all types across all families
            int totalRequired = 0;
            foreach (JObject req in requiredTitleblocks)
            {
                var types = req["types"] as JArray;
                if (types != null) totalRequired += types.Count;
            }
            result.TotalRequired = totalRequired;

            foreach (JObject req in requiredTitleblocks)
            {
                string familyName = req["family"]?.ToString();
                if (string.IsNullOrEmpty(familyName)) continue;

                var requiredTypes = req["types"] as JArray;
                if (requiredTypes == null) continue;

                // Check if family exists
                if (!familyTypeLookup.TryGetValue(familyName, out List<string> loadedTypes))
                {
                    // Family missing entirely — all its types are missing
                    foreach (var t in requiredTypes)
                    {
                        result.Missing.Add($"{familyName} : {t}");
                    }
                    continue;
                }

                // Family exists — check each required type
                foreach (var t in requiredTypes)
                {
                    string typeName = t.ToString();
                    if (loadedTypes.Any(lt => string.Equals(lt, typeName, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Present++;
                    }
                    else
                    {
                        result.Missing.Add($"{familyName} : {typeName}");
                    }
                }
            }

            return result;
        }

        // =====================================================================
        // PROJECT PARAMETER CHECKER
        // =====================================================================
        private ParameterCheckResult CheckProjectParameters(Document doc, JObject standards)
        {
            var result = new ParameterCheckResult();

            // Get all parameter bindings in the document
            var bindingMap = doc.ParameterBindings;
            var iterator = bindingMap.ForwardIterator();

            var existingParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (iterator.MoveNext())
            {
                if (iterator.Key is Definition def)
                {
                    existingParams.Add(def.Name);
                }
            }

            var requiredParams = standards["project_parameters"] as JArray;
            if (requiredParams == null) return result;

            result.TotalRequired = requiredParams.Count;

            foreach (JObject req in requiredParams)
            {
                string paramName = req["name"]?.ToString();
                if (string.IsNullOrEmpty(paramName)) continue;

                if (existingParams.Contains(paramName))
                {
                    result.Present++;
                }
                else
                {
                    result.Missing.Add(paramName);
                }
            }

            return result;
        }

        // =====================================================================
        // HELPER: Send error via raw message
        // =====================================================================
        private string SendError(string message)
        {
            string errorJson = JsonConvert.SerializeObject(new
            {
                preflight_result = new
                {
                    status = "error",
                    message = message
                }
            });

            try
            {
                A49AIRevitAssistant.UI.DockablePaneViewer.Instance.Dispatcher.Invoke(() =>
                {
                    A49AIRevitAssistant.UI.DockablePaneViewer.Instance.SendRawMessage(errorJson);
                });
            }
            catch { }

            return "{\"status\":\"silent\"}";
        }

        // =====================================================================
        // RESULT MODELS (Internal)
        // =====================================================================
        private class TemplateCheckResult
        {
            public int TotalRequired { get; set; } = 0;
            public int Present { get; set; } = 0;
            public List<string> Missing { get; set; } = new List<string>();
            public List<object> Misconfigured { get; set; } = new List<object>();
        }

        private class TitleblockCheckResult
        {
            public int TotalRequired { get; set; } = 0;
            public int Present { get; set; } = 0;
            public List<string> Missing { get; set; } = new List<string>();
        }

        private class ParameterCheckResult
        {
            public int TotalRequired { get; set; } = 0;
            public int Present { get; set; } = 0;
            public List<string> Missing { get; set; } = new List<string>();
        }
    }
}
