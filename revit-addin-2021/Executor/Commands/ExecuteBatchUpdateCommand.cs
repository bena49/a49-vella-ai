// Executor/Commands/ExecuteBatchUpdateCommand.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using A49AIRevitAssistant.Models; // Ensure this matches your namespace for Envelope

namespace A49AIRevitAssistant.Executor.Commands
{
    public class ExecuteBatchUpdateCommand
    {
        private readonly UIApplication _uiapp;

        public ExecuteBatchUpdateCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        public string Execute(RevitCommandEnvelope env)
        {
            Document doc = _uiapp.ActiveUIDocument.Document;
            StringBuilder report = new StringBuilder();
            int successCount = 0;
            int failCount = 0;

            // 1. EXTRACT UPDATES FROM RAW JSON
            // We use the 'raw' field because the specific 'updates' list isn't in our main Envelope class
            if (env.raw == null || !env.raw.ContainsKey("updates"))
            {
                return "❌ No updates found in payload.";
            }

            JArray updates = env.raw["updates"] as JArray;
            if (updates == null || updates.Count == 0) return "ℹ No changes to apply.";

            try
            {
                using (Transaction tx = new Transaction(doc, "Vella - Batch Wizard Update"))
                {
                    tx.Start();

                    foreach (JObject item in updates)
                    {
                        string uniqueId = item["unique_id"]?.ToString();
                        string type = item["element_type"]?.ToString(); // "SHEET" or "VIEW"
                        JObject changes = item["changes"] as JObject;

                        if (string.IsNullOrEmpty(uniqueId) || changes == null) continue;

                        Element elem = doc.GetElement(uniqueId);
                        if (elem == null)
                        {
                            report.AppendLine($"• [FAIL] ID {uniqueId} not found.");
                            failCount++;
                            continue;
                        }

                        // ------------------------------------------------
                        // APPLY CHANGES
                        // ------------------------------------------------
                        try
                        {
                            // A. RENAME / RENUMBER SHEET
                            if (elem is ViewSheet sheet)
                            {
                                if (changes.ContainsKey("number"))
                                {
                                    string newNum = changes["number"].ToString();
                                    if (sheet.SheetNumber != newNum) sheet.SheetNumber = newNum;
                                }
                                if (changes.ContainsKey("name"))
                                {
                                    string newName = changes["name"].ToString();
                                    if (sheet.Name != newName) sheet.Name = newName;
                                }
                            }
                            // B. RENAME VIEW
                            else if (elem is View view)
                            {
                                if (changes.ContainsKey("name"))
                                {
                                    string newName = changes["name"].ToString();
                                    if (view.Name != newName) view.Name = newName;
                                }
                                if (changes.ContainsKey("title_on_sheet"))
                                {
                                    string newTitle = changes["title_on_sheet"].ToString();
                                    Parameter pTitle = view.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION);
                                    if (pTitle == null) pTitle = view.LookupParameter("Title on Sheet");

                                    if (pTitle != null && !pTitle.IsReadOnly)
                                    {
                                        pTitle.Set(newTitle);
                                    }
                                }
                            }

                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            report.AppendLine($"• [FAIL] {elem.Name}: {ex.Message}");
                            failCount++;
                        }
                    }

                    tx.Commit();
                }

                if (failCount == 0)
                    return $"✔ Successfully updated {successCount} items.";
                else
                    return $"⚠ Updated {successCount} items. {failCount} failed:\n{report}";
            }
            catch (Exception ex)
            {
                return $"❌ Critical Error in Batch Update: {ex.Message}";
            }
        }
    }
}