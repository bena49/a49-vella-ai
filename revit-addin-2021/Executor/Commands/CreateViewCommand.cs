// ============================================================================
// A49AIRevitAssistant/Executor/Commands/CreateViewCommand.cs (FINAL)
// ============================================================================

using A49AIRevitAssistant.Executor.Contracts;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class CreateViewCommand
    {
        private readonly UIApplication _uiapp;

        public CreateViewCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        // 💥 ADDED: bool useTransaction = true
        public string Execute(List<CreateViewRequest> items, bool useTransaction = true)
        {
            if (items == null || items.Count == 0)
                return "❌ No view items provided.";

            UIDocument uidoc = _uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            List<string> successList = new List<string>();
            List<string> errorList = new List<string>();

            Transaction tx = null;

            try
            {
                // 💥 Only start a new transaction if the flag says it's okay
                if (useTransaction)
                {
                    tx = new Transaction(doc, "Vella - Create Views");
                    tx.Start();
                }

                foreach (var item in items)
                {
                    Level level = null;
                    if (!string.IsNullOrEmpty(item.level) && item.level.ToUpper() != "NONE")
                    {
                        level = ResolveLevel(doc, item.level);
                        if (level == null && IsLevelBasedView(item.view_type))
                        {
                            errorList.Add($"Skipped '{item.name}': Level '{item.level}' not found.");
                            continue;
                        }
                    }

                    View view = CreateView(doc, item.view_type, level);

                    if (view == null)
                    {
                        errorList.Add($"Skipped '{item.name}': Could not generate view type '{item.view_type}'.");
                        continue;
                    }

                    if (!string.IsNullOrEmpty(item.template))
                    {
                        ApplyTemplate(doc, view, item.template);
                    }

                    if (!string.IsNullOrEmpty(item.scope_box_id))
                    {
                        ApplyScopeBox(doc, view, item.scope_box_id);
                    }

                    try
                    {
                        if (view.Name != item.name) view.Name = item.name;
                    }
                    catch
                    {
                        string safeName = $"{item.name}_{Guid.NewGuid().ToString().Substring(0, 4)}";
                        view.Name = safeName;
                        item.name = safeName;
                    }

                    successList.Add(item.name);
                }

                // 💥 Only commit or rollback the transaction if we were the ones who opened it
                if (useTransaction)
                {
                    if (successList.Count > 0)
                    {
                        tx.Commit();
                    }
                    else
                    {
                        tx.RollBack();
                        return "❌ No views could be created.\n" + string.Join("\n", errorList);
                    }
                }

                if (successList.Count == 0 && !useTransaction)
                {
                    return "❌ No views could be created.\n" + string.Join("\n", errorList);
                }

                string resultMsg = $"{successList.Count} View(s) created successfully.";
                if (successList.Count > 0) resultMsg += "\n" + string.Join("\n", successList.Select(n => "• " + n));
                if (errorList.Count > 0) resultMsg += "\n\n⚠️ Warnings:\n" + string.Join("\n", errorList);

                return resultMsg;
            }
            catch (Exception ex)
            {
                // 💥 Safe rollback checking
                if (useTransaction && tx != null && tx.HasStarted())
                {
                    tx.RollBack();
                }
                return $"❌ Critical Error in CreateViewCommand: {ex.Message}";
            }
        }

        // ============================================================================
        // HELPERS
        // ============================================================================

        private bool IsLevelBasedView(string type)
        {
            string t = type.ToLower();
            if (t.Contains("drafting") || t.Contains("detail") || t.Contains("schedule")) return false;
            return t.Contains("floor") || t.Contains("ceiling") || t.Contains("plan") || t.Contains("site") || t.Contains("area");
        }

        private View CreateView(Document doc, string type, Level level)
        {
            string t = type.ToLower();
            if (t.Contains("schedule")) return CreateDraftingView(doc);
            if (t.Contains("drafting") || t.Contains("detail")) return CreateDraftingView(doc);
            if (t.Contains("area")) return CreateAreaPlan(doc, level, t);
            if (t.Contains("ceiling") || t.Contains("cp") || t.Contains("reflected")) return CreateCeilingPlan(doc, level);
            if (t.Contains("floor") || t.Contains("plan") || t.Contains("site")) return CreateFloorPlan(doc, level);
            return null;
        }

        private View CreateAreaPlan(Document doc, Level level, string viewTypeString)
        {
            if (level == null) return null;
            string targetScheme = "";
            if (viewTypeString.Contains("eia")) targetScheme = "EIA";
            else if (viewTypeString.Contains("nfa")) targetScheme = "NFA";
            else targetScheme = "Gross Building";

            var schemes = new FilteredElementCollector(doc).OfClass(typeof(AreaScheme)).Cast<AreaScheme>().ToList();
            AreaScheme chosenScheme = schemes.FirstOrDefault(s => s.Name.IndexOf(targetScheme, StringComparison.OrdinalIgnoreCase) >= 0);
            if (chosenScheme == null) chosenScheme = schemes.FirstOrDefault();
            if (chosenScheme == null) return null;

            return ViewPlan.CreateAreaPlan(doc, chosenScheme.Id, level.Id);
        }

        private View CreateDraftingView(Document doc)
        {
            var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.Drafting);
            if (vft == null) return null;
            return ViewDrafting.Create(doc, vft.Id);
        }

        private View CreateFloorPlan(Document doc, Level level)
        {
            if (level == null) return null;
            ElementId vtid = GetDefaultViewFamilyType(doc, ViewFamily.FloorPlan);
            if (vtid == ElementId.InvalidElementId) return null;
            return ViewPlan.Create(doc, vtid, level.Id);
        }

        private View CreateCeilingPlan(Document doc, Level level)
        {
            if (level == null) return null;
            ElementId vtid = GetDefaultViewFamilyType(doc, ViewFamily.CeilingPlan);
            if (vtid == ElementId.InvalidElementId) return null;
            return ViewPlan.Create(doc, vtid, level.Id);
        }

        private ElementId GetDefaultViewFamilyType(Document doc, ViewFamily vf)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().Where(x => x.ViewFamily == vf).Select(x => x.Id).FirstOrDefault();
        }

        private Level ResolveLevel(Document doc, string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;
            string target = token.Trim();
            var allLevels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();

            var match = allLevels.FirstOrDefault(l => l.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            match = allLevels.FirstOrDefault(l => l.Name.Equals("Level " + target, StringComparison.OrdinalIgnoreCase) || target.Equals("Level " + l.Name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            if (target.StartsWith("L", StringComparison.OrdinalIgnoreCase) && char.IsDigit(target.Last()))
            {
                string expanded = target.Replace("L", "Level ");
                match = allLevels.FirstOrDefault(l => l.Name.Equals(expanded, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            var digitMatch = Regex.Match(target, @"(\d+)");
            if (digitMatch.Success && int.TryParse(digitMatch.Value, out int num))
            {
                string s1 = num.ToString();
                string s2 = num.ToString("D2");
                return allLevels.FirstOrDefault(l => {
                    string ln = l.Name.ToUpper();
                    return Regex.IsMatch(ln, $@"\b{s1}\b") || Regex.IsMatch(ln, $@"\b{s2}\b");
                });
            }
            return null;
        }

        private void ApplyTemplate(Document doc, View view, string templateName)
        {
            if (string.IsNullOrEmpty(templateName)) return;
            var template = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().FirstOrDefault(v => v.IsTemplate && v.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase));
            if (template != null) view.ViewTemplateId = template.Id;
        }

        private void ApplyScopeBox(Document doc, View view, string scopeBoxUniqueId)
        {
            try
            {
                Element sb = null;
                try { sb = doc.GetElement(scopeBoxUniqueId); } catch { }
                if (sb == null)
                {
                    sb = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_VolumeOfInterest).WhereElementIsNotElementType().FirstOrDefault(e => e.Name.Equals(scopeBoxUniqueId, StringComparison.OrdinalIgnoreCase));
                }
                if (sb != null)
                {
                    Parameter p = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                    if (p != null && !p.IsReadOnly) p.Set(sb.Id);
                }
            }
            catch (Exception) { }
        }
    }
}