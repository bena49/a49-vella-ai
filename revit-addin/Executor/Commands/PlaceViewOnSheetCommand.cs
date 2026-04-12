// ============================================================================
// A49AIRevitAssistant/Executor/Commands/PlaceViewOnSheetCommand.cs 
// ============================================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class PlaceViewOnSheetCommand
    {
        private readonly UIApplication _uiapp;

        public PlaceViewOnSheetCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        // 💥 ADDED: bool useTransaction = true
        public string Execute(string viewName, string sheetNumber, string placementMode = "CENTER", string refSheetNumber = null, bool useTransaction = true)
        {
            Document doc = _uiapp.ActiveUIDocument.Document;
            Transaction tx = null;

            try
            {
                if (useTransaction)
                {
                    tx = new Transaction(doc, "Vella - Place View");
                    tx.Start();
                }

                viewName = viewName?.Trim();
                sheetNumber = sheetNumber?.Trim();
                refSheetNumber = refSheetNumber?.Trim();

                View view = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => v.Name.Equals(viewName, StringComparison.OrdinalIgnoreCase));

                if (view == null) return $"❌ View '{viewName}' not found.";

                ViewSheet sheet = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .FirstOrDefault(s => s.SheetNumber.Equals(sheetNumber, StringComparison.OrdinalIgnoreCase));

                if (sheet == null) return $"❌ Target Sheet '{sheetNumber}' not found.";

                XYZ placementPoint = null;

                if (placementMode == "MATCH" && !string.IsNullOrEmpty(refSheetNumber))
                {
                    ViewSheet refSheet = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .FirstOrDefault(s => s.SheetNumber.Equals(refSheetNumber, StringComparison.OrdinalIgnoreCase));

                    if (refSheet != null)
                    {
                        var viewports = refSheet.GetAllViewports();
                        if (viewports.Count > 0)
                        {
                            ElementId vpId = viewports.First();
                            Viewport refVp = doc.GetElement(vpId) as Viewport;
                            if (refVp != null)
                            {
                                placementPoint = refVp.GetBoxCenter();
                            }
                        }
                    }
                }

                if (placementPoint == null)
                {
                    BoundingBoxUV sheetBox = sheet.Outline;
                    double uMid = (sheetBox.Min.U + sheetBox.Max.U) / 2.0;
                    double vMid = (sheetBox.Min.V + sheetBox.Max.V) / 2.0;
                    placementPoint = new XYZ(uMid, vMid, 0);
                }

                Viewport.Create(doc, sheet.Id, view.Id, placementPoint);

                if (useTransaction)
                {
                    tx.Commit();
                }

                string msg = $"✔ Placed '{viewName}' on '{sheetNumber}'";
                if (placementMode == "MATCH" && placementPoint != null)
                    msg += $" (Matched {refSheetNumber})";
                else if (placementMode == "MATCH" && placementPoint == null)
                    msg += $" (Ref '{refSheetNumber}' missing - Centered)";

                return msg + ".";
            }
            catch (Exception ex)
            {
                if (useTransaction && tx != null && tx.HasStarted())
                {
                    tx.RollBack();
                }
                return $"❌ Failed to place view: {ex.Message}";
            }
        }
    }
}