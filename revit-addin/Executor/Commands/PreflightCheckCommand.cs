// ============================================================================
// A49AIRevitAssistant/Executor/Commands/PreflightCheckCommand.cs
// ============================================================================
// Scans the active Revit document against A49 standards and reports:
//   1. Missing, misnamed, or misconfigured View Templates
//   2. Missing or misnamed Titleblock types
//   3. Missing Project Parameters (DISCIPLINE, PROJECT PHASE, VIEW SET)
//
// Standards data is received in the envelope from Django (standards.json).
// Results are sent directly to Vue via SendRawMessage.
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
                string fullVersion = _uiapp.Application.VersionNumber;
                string shortVersion = fullVersion.Length >= 4
                    ? fullVersion.Substring(2)
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
                    || templateResult.Misnamed.Count > 0
                    || titleblockResult.Missing.Count > 0
                    || titleblockResult.Misnamed.Count > 0
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
                            misnamed_count = templateResult.Misnamed.Count,
                            missing = templateResult.Missing,
                            misconfigured = templateResult.Misconfigured,
                            misnamed = templateResult.Misnamed
                        },

                        titleblocks = new
                        {
                            total_required = titleblockResult.TotalRequired,
                            present = titleblockResult.Present,
                            missing_count = titleblockResult.Missing.Count,
                            misnamed_count = titleblockResult.Misnamed.Count,
                            missing = titleblockResult.Missing,
                            misnamed = titleblockResult.Misnamed
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
                    $"{parameterResult.Present}/{parameterResult.TotalRequired} parameters | " +
                    $"Misnamed: {templateResult.Misnamed.Count} templates, {titleblockResult.Misnamed.Count} titleblocks");

                // ─────────────────────────────────────────────────────
                // 7. SEND DIRECTLY TO VUE
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

            var allTemplates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .ToList();

            var templateLookup = new Dictionary<string, View>(StringComparer.OrdinalIgnoreCase);
            foreach (var vt in allTemplates)
            {
                if (!templateLookup.ContainsKey(vt.Name))
                    templateLookup[vt.Name] = vt;
            }

            var requiredTemplates = standards["view_templates"] as JArray;
            if (requiredTemplates == null) return result;

            result.TotalRequired = requiredTemplates.Count;

            // Collect all required names
            var requiredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JObject req in requiredTemplates)
            {
                string name = req["name"]?.ToString();
                if (!string.IsNullOrEmpty(name)) requiredNames.Add(name);
            }

            // Find A49_ templates in doc that are NOT in the required list — misname candidates
            var unmatchedDocTemplates = allTemplates
                .Where(v => v.Name.StartsWith("A49_", StringComparison.OrdinalIgnoreCase)
                            && !requiredNames.Contains(v.Name))
                .ToList();

            foreach (JObject req in requiredTemplates)
            {
                string reqName = req["name"]?.ToString();
                if (string.IsNullOrEmpty(reqName)) continue;

                string expectedDiscipline = req["discipline"]?.ToString() ?? "";
                string expectedPhase = req["project_phase"]?.ToString() ?? "";
                string expectedViewSet = req["view_set"]?.ToString() ?? "";

                // Check if template exists with exact name
                if (templateLookup.TryGetValue(reqName, out View foundTemplate))
                {
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
                    continue;
                }

                // Template NOT found — look for misnamed candidate
                var candidate = FindMisnamedCandidate(reqName, unmatchedDocTemplates);

                if (candidate != null)
                {
                    var paramIssues = new List<object>();
                    CheckParameterValue(candidate, "DISCIPLINE", expectedDiscipline, paramIssues);
                    CheckParameterValue(candidate, "PROJECT PHASE", expectedPhase, paramIssues);
                    CheckParameterValue(candidate, "VIEW SET", expectedViewSet, paramIssues);

                    result.Misnamed.Add(new
                    {
                        expected = reqName,
                        current = candidate.Name,
                        element_id = candidate.Id.Value,
                        parameter_issues = paramIssues
                    });

                    unmatchedDocTemplates.Remove(candidate);
                }
                else
                {
                    result.Missing.Add(reqName);
                }
            }

            return result;
        }

        private View FindMisnamedCandidate(string requiredName, List<View> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;

            string[] parts = requiredName.Split('_');
            if (parts.Length < 3) return null;

            string stage = parts[1]; // CD, DD, PD, WV

            var identifiers = new List<string>();
            for (int i = 2; i < parts.Length; i++)
            {
                foreach (string word in parts[i].Split(' '))
                {
                    string clean = word.Trim().ToUpper();
                    if (!string.IsNullOrEmpty(clean) && clean != "A49")
                        identifiers.Add(clean);
                }
            }

            if (identifiers.Count == 0) return null;

            View bestMatch = null;
            int bestScore = 0;

            foreach (var candidate in candidates)
            {
                string candidateUpper = candidate.Name.ToUpper();

                if (!candidateUpper.Contains($"_{stage}_") && !candidateUpper.StartsWith($"A49_{stage}"))
                    continue;

                int score = 0;
                foreach (string id in identifiers)
                {
                    if (candidateUpper.Contains(id)) score++;
                }

                if (score > bestScore && score >= Math.Max(1, identifiers.Count / 2))
                {
                    bestScore = score;
                    bestMatch = candidate;
                }
            }

            return bestMatch;
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

            var titleblockFamilies = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.FamilyCategory != null &&
                       f.FamilyCategory.Id.Value == (long)BuiltInCategory.OST_TitleBlocks)
                .ToList();

            var familyTypeLookup = new Dictionary<string, List<TypeInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var family in titleblockFamilies)
            {
                var typeInfos = new List<TypeInfo>();
                foreach (var typeId in family.GetFamilySymbolIds())
                {
                    var symbol = doc.GetElement(typeId) as FamilySymbol;
                    if (symbol != null)
                        typeInfos.Add(new TypeInfo { Name = symbol.Name, ElementId = typeId.Value });
                }
                familyTypeLookup[family.Name] = typeInfos;
            }

            var requiredTitleblocks = standards["titleblocks"] as JArray;
            if (requiredTitleblocks == null) return result;

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

                if (!familyTypeLookup.TryGetValue(familyName, out List<TypeInfo> loadedTypes))
                {
                    foreach (var t in requiredTypes)
                    {
                        result.Missing.Add($"{familyName} : {t}");
                    }
                    continue;
                }

                var matchedTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var t in requiredTypes)
                {
                    string typeName = t.ToString();

                    var exactMatch = loadedTypes.FirstOrDefault(lt =>
                        string.Equals(lt.Name, typeName, StringComparison.OrdinalIgnoreCase));

                    if (exactMatch != null)
                    {
                        result.Present++;
                        matchedTypeNames.Add(exactMatch.Name);
                        continue;
                    }

                    // Look for misnamed candidate
                    var candidate = loadedTypes.FirstOrDefault(lt =>
                        !matchedTypeNames.Contains(lt.Name) &&
                        IsSimilarTypeName(typeName, lt.Name));

                    if (candidate != null)
                    {
                        result.Misnamed.Add(new
                        {
                            family = familyName,
                            expected = typeName,
                            current = candidate.Name,
                            element_id = candidate.ElementId
                        });
                        matchedTypeNames.Add(candidate.Name);
                    }
                    else
                    {
                        var unmatchedType = loadedTypes.FirstOrDefault(lt =>
                            !matchedTypeNames.Contains(lt.Name));

                        if (unmatchedType != null)
                        {
                            result.Misnamed.Add(new
                            {
                                family = familyName,
                                expected = typeName,
                                current = unmatchedType.Name,
                                element_id = unmatchedType.ElementId
                            });
                            matchedTypeNames.Add(unmatchedType.Name);
                        }
                        else
                        {
                            result.Missing.Add($"{familyName} : {typeName}");
                        }
                    }
                }
            }

            return result;
        }

        private bool IsSimilarTypeName(string expected, string actual)
        {
            var expectedWords = expected.ToUpper().Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var actualWords = actual.ToUpper().Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

            int matches = expectedWords.Count(ew => actualWords.Any(aw => aw.Contains(ew) || ew.Contains(aw)));
            return matches >= Math.Max(1, expectedWords.Length / 2);
        }

        // =====================================================================
        // PROJECT PARAMETER CHECKER
        // =====================================================================
        private ParameterCheckResult CheckProjectParameters(Document doc, JObject standards)
        {
            var result = new ParameterCheckResult();

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
        // HELPERS
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
        // RESULT MODELS
        // =====================================================================
        private class TemplateCheckResult
        {
            public int TotalRequired { get; set; } = 0;
            public int Present { get; set; } = 0;
            public List<string> Missing { get; set; } = new List<string>();
            public List<object> Misconfigured { get; set; } = new List<object>();
            public List<object> Misnamed { get; set; } = new List<object>();
        }

        private class TitleblockCheckResult
        {
            public int TotalRequired { get; set; } = 0;
            public int Present { get; set; } = 0;
            public List<string> Missing { get; set; } = new List<string>();
            public List<object> Misnamed { get; set; } = new List<object>();
        }

        private class ParameterCheckResult
        {
            public int TotalRequired { get; set; } = 0;
            public int Present { get; set; } = 0;
            public List<string> Missing { get; set; } = new List<string>();
        }

        private class TypeInfo
        {
            public string Name { get; set; }
            public long ElementId { get; set; }
        }
    }
}
