using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class BatchRenameViewsCommand
    {
        private readonly UIApplication _uiapp;

        public BatchRenameViewsCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        public string Execute(string strategy, string find, string replace, string sheetNumber = null)
        {
            Document doc = _uiapp.ActiveUIDocument.Document;
            StringBuilder report = new StringBuilder();
            int count = 0;

            try
            {
                using (Transaction tx = new Transaction(doc, "Vella - Batch Rename"))
                {
                    tx.Start();

                    // ====================================================
                    // 💥 STRATEGY 1: SET TITLE ON SHEET
                    // "Change title to 'Living Room'"
                    // ====================================================
                    if (strategy == "set_title_on_sheet")
                    {
                        string newTitle = replace;
                        if (string.IsNullOrWhiteSpace(newTitle)) return "❌ New title cannot be empty.";

                        List<View> targets = new List<View>();

                        // A. CHECK SELECTION (User explicitly picked a viewport)
                        var selection = _uiapp.ActiveUIDocument.Selection.GetElementIds();
                        if (selection.Count > 0)
                        {
                            foreach (var id in selection)
                            {
                                View v = ConvertIdToView(doc, id);
                                if (v != null) targets.Add(v);
                            }
                        }
                        // B. SMART AUTO-DETECT (User is just looking at the sheet)
                        else if (doc.ActiveView is ViewSheet sheet)
                        {
                            // 1. Get ALL views on this sheet
                            var candidates = new List<View>();
                            foreach (var id in sheet.GetAllPlacedViews())
                            {
                                View v = doc.GetElement(id) as View;
                                if (v == null || v.IsTemplate) continue;

                                // 2. FILTER NOISE (Ignore Legends, Schedules, Sheets)
                                if (v.ViewType == ViewType.Legend ||
                                    v.ViewType == ViewType.Schedule ||
                                    v.ViewType == ViewType.ColumnSchedule ||
                                    v.ViewType == ViewType.PanelSchedule ||
                                    v.ViewType == ViewType.DrawingSheet)
                                    continue;

                                candidates.Add(v);
                            }

                            // 3. DECISION LOGIC
                            if (candidates.Count == 0)
                            {
                                return "❌ No editable model views found on this sheet.";
                            }
                            else if (candidates.Count == 1)
                            {
                                // 🏆 JACKPOT: Only one main view. Rename it!
                                targets.Add(candidates[0]);
                            }
                            else
                            {
                                // ⚠️ AMBIGUOUS: Too many views (e.g. 4 elevations)
                                return $"❌ This sheet has {candidates.Count} main views. Please select the individual Viewport you want to rename.";
                            }
                        }
                        // C. ACTIVE VIEW (User is inside the view)
                        else if (doc.ActiveView != null && doc.ActiveView.ViewType != ViewType.DrawingSheet)
                        {
                            targets.Add(doc.ActiveView);
                        }
                        else
                        {
                            return "❌ Please open a Sheet or View.";
                        }

                        // EXECUTE RENAME
                        foreach (var v in targets)
                        {
                            Parameter pTitle = v.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION);
                            if (pTitle == null) pTitle = v.LookupParameter("Title on Sheet");

                            if (pTitle != null && !pTitle.IsReadOnly)
                            {
                                pTitle.Set(newTitle);
                                count++;
                                report.AppendLine($"• Set Title for '{v.Name}' ➔ '{newTitle}'");
                            }
                            else
                            {
                                report.AppendLine($"• [SKIP] '{v.Name}' (Parameter read-only/missing)");
                            }
                        }

                        tx.Commit();
                        return count > 0 ? $"✔ Updated {count} titles:\n{report}" : $"❌ Could not update titles.\n{report}";
                    }

                    // ... (Strategy 2 & 3 remain unchanged) ...
                    // (I will assume you kept the existing match_titleblock and FIND_REPLACE logic here)
                    if (strategy == "match_titleblock")
                    {
                        // ... [Keep existing code] ...
                        // For compiling sake, inserting the abbreviated logic:
                        List<View> views = new List<View>();
                        ViewSheet targetSheet = null;
                        if (!string.IsNullOrEmpty(sheetNumber))
                            targetSheet = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().FirstOrDefault(s => s.SheetNumber == sheetNumber);
                        else if (doc.ActiveView is ViewSheet s) targetSheet = s;

                        if (targetSheet == null) return "❌ Sheet not found.";

                        foreach (ElementId id in targetSheet.GetAllPlacedViews()) views.Add(doc.GetElement(id) as View);
                        foreach (var v in views)
                        {
                            Parameter p = v.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION);
                            if (p != null && !string.IsNullOrWhiteSpace(p.AsString()) && v.Name != p.AsString()) { v.Name = p.AsString(); count++; }
                        }
                        tx.Commit();
                        return $"✔ Synced {count} views.";
                    }

                    if (strategy == "FIND_REPLACE" || !string.IsNullOrEmpty(find))
                    {
                        var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate && v.Name.Contains(find)).ToList();
                        foreach (var v in views) { v.Name = v.Name.Replace(find, replace ?? ""); count++; }
                        tx.Commit();
                        return $"✔ Replaced {count} view names.";
                    }

                    return "❌ No valid rename strategy found.";
                }
            }
            catch (Exception ex)
            {
                return $"❌ Error in Batch Rename: {ex.Message}";
            }
        }

        private View ConvertIdToView(Document doc, ElementId id)
        {
            Element elem = doc.GetElement(id);
            if (elem is View v) return v;
            if (elem is Viewport vp) return doc.GetElement(vp.ViewId) as View;
            return null;
        }
    }
}