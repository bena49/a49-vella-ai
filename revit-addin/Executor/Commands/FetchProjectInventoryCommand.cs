using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class FetchProjectInventoryCommand
    {
        private readonly UIApplication _uiapp;

        // 💥 MAP OF VALUES TO CODES (From your A49 Standards)
        private readonly Dictionary<string, string> _stageKeywords = new Dictionary<string, string>
        {
            { "03 - CONSTRUCTION DOCUMENTS", "CD" },
            { "CONSTRUCTION DOCUMENTS", "CD" },
            { "CD", "CD" },
            { "02 - DESIGN DEVELOPMENT", "DD" },
            { "DESIGN DEVELOPMENT", "DD" },
            { "DD", "DD" },
            { "01 - PRE-DESIGN", "PD" },
            { "PRE-DESIGN", "PD" },
            { "PD", "PD" },
            { "00 - WORKING VIEW", "WV" },
            { "WORKING VIEW", "WV" },
            { "WV", "WV" }
        };

        public FetchProjectInventoryCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        public string Execute()
        {
            Document doc = _uiapp.ActiveUIDocument.Document;

            // 1. COLLECT SHEETS
            var sheetCollector = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            var sheetData = new List<object>();

            foreach (var s in sheetCollector)
            {
                // A. Category (A1, A2...)
                string category = "Uncategorized";
                var match = Regex.Match(s.SheetNumber, @"^([A-Z]+[0-9]?)");
                if (match.Success) category = match.Groups[1].Value;

                // B. Detect Stage (The "Silver Bullet" Logic)
                string detectedStage = DetectStage(s, doc);

                sheetData.Add(new
                {
                    unique_id = s.UniqueId,
                    number = s.SheetNumber,
                    name = s.Name,
                    category = category,
                    stage = detectedStage // Sends "CD", "DD", etc.
                });
            }

            // 2. COLLECT VIEWS (Standard Logic)
            var viewCollector = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.CanBePrinted && v.ViewType != ViewType.DrawingSheet)
                .Where(v => v.ViewType != ViewType.Legend)
                .Where(v => v.ViewType != ViewType.Schedule)
                .OrderBy(v => v.Name)
                .ToList();

            var viewData = new List<object>();

            foreach (var v in viewCollector)
            {
                string titleOnSheet = "";
                Parameter pTitle = v.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION);
                if (pTitle == null) pTitle = v.LookupParameter("Title on Sheet");
                if (pTitle != null) titleOnSheet = pTitle.AsString();

                string sheetNum = null;
                Parameter pSheet = v.get_Parameter(BuiltInParameter.VIEWPORT_SHEET_NUMBER);
                if (pSheet != null) sheetNum = pSheet.AsString();

                // Detect Stage for View (Check name first)
                string viewStage = "";
                string vName = v.Name.ToUpper();
                if (vName.Contains("_CD_") || vName.Contains(" CD ")) viewStage = "CD";
                else if (vName.Contains("_DD_") || vName.Contains(" DD ")) viewStage = "DD";
                else if (vName.Contains("_PD_") || vName.Contains(" PD ")) viewStage = "PD";
                else if (vName.Contains("_WV_") || vName.Contains(" WV ")) viewStage = "WV";

                viewData.Add(new
                {
                    unique_id = v.UniqueId,
                    name = v.Name,
                    title_on_sheet = titleOnSheet ?? "",
                    type = v.ViewType.ToString(),
                    sheet_number = sheetNum ?? "Not Placed",
                    is_placed = !string.IsNullOrEmpty(sheetNum),
                    stage = viewStage
                });
            }

            // 3. SEND TO UI
            var payload = new
            {
                project_inventory = new
                {
                    sheets = sheetData,
                    views = viewData
                }
            };

            string jsonString = JsonConvert.SerializeObject(payload);
            try
            {
                A49AIRevitAssistant.UI.DockablePaneViewer.Instance.Dispatcher.Invoke(() =>
                {
                    A49AIRevitAssistant.UI.DockablePaneViewer.Instance.SendRawMessage(jsonString);
                });
            }
            catch (Exception ex)
            {
                A49Logger.Log("Error sending to UI: " + ex.Message);
            }

            _ = DjangoBridge.SendAsync(payload);
            return "{\"status\":\"silent\"}";
        }

        // 💥 NEW: SMART STAGE DETECTOR
        private string DetectStage(ViewSheet s, Document doc)
        {
            // 1. Check Parameters (Browser Organization)
            // This scans ALL text parameters to see if any match "03 - CONSTRUCTION DOCUMENTS"
            foreach (Parameter p in s.Parameters)
            {
                if (p.StorageType == StorageType.String && p.HasValue)
                {
                    string val = p.AsString().ToUpper().Trim();
                    if (_stageKeywords.ContainsKey(val)) return _stageKeywords[val];
                }
            }

            // 2. Check Placed Views (Inference Fallback)
            var placedViews = s.GetAllPlacedViews();
            foreach (ElementId id in placedViews)
            {
                View v = doc.GetElement(id) as View;
                if (v == null) continue;
                string vName = v.Name.ToUpper();

                // Relaxed checking
                if (vName.Contains("_CD_") || vName.Contains(" CD ") || vName.Contains("-CD-")) return "CD";
                if (vName.Contains("_DD_") || vName.Contains(" DD ") || vName.Contains("-DD-")) return "DD";
                if (vName.Contains("_PD_") || vName.Contains(" PD ") || vName.Contains("-PD-")) return "PD";
                if (vName.Contains("_WV_") || vName.Contains(" WV ") || vName.Contains("-WV-")) return "WV";
            }

            // 3. Check Sheet Name/Number (Last Resort)
            string sName = s.Name.ToUpper();
            string sNum = s.SheetNumber.ToUpper();
            if (sName.Contains("CONSTRUCTION DOCUMENT") || sNum.Contains("CD")) return "CD";
            if (sName.Contains("DESIGN DEVELOPMENT") || sNum.Contains("DD")) return "DD";
            if (sName.Contains("PRE-DESIGN") || sNum.Contains("PD")) return "PD";

            return ""; // Unknown
        }
    }
}