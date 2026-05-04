using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;
using System.Collections.Generic;
using A49AIRevitAssistant.Executor.Contracts;

namespace A49AIRevitAssistant.Executor.Commands.Sheets
{
    public class CreateSheetsCommand
    {
        private readonly UIApplication _uiapp;

        public CreateSheetsCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        // 💥 ADDED: bool useTransaction = true
        public string Execute(List<CreateSheetRequest> sheets, bool useTransaction = true)
        {
            if (_uiapp == null) return "❌ Revit UIApplication not available.";
            if (sheets == null || sheets.Count == 0) return "❌ No sheets provided.";

            UIDocument uidoc = _uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            List<string> createdSheets = new List<string>();
            List<string> errors = new List<string>();

            Transaction tx = null;

            try
            {
                // 💥 Safe Transaction handling
                if (useTransaction)
                {
                    tx = new Transaction(doc, "Vella - Create Sheets");
                    tx.Start();
                }

                foreach (var s in sheets)
                {
                    string sheetNumber = s.sheet_number;
                    string sheetName = s.sheet_name;

                    if (string.IsNullOrWhiteSpace(sheetNumber) || string.IsNullOrWhiteSpace(sheetName))
                    {
                        errors.Add($"Skipped: Missing Name/Number");
                        continue;
                    }

                    FamilySymbol titleblockSymbol = null;
                    bool tbRequested = !string.IsNullOrWhiteSpace(s.titleblock_family);

                    if (tbRequested && !string.IsNullOrWhiteSpace(s.titleblock_type))
                    {
                        titleblockSymbol = FindTitleblockSymbol(doc, s.titleblock_family, s.titleblock_type);
                        if (titleblockSymbol != null && !titleblockSymbol.IsActive)
                        {
                            titleblockSymbol.Activate();
                        }
                    }

                    ViewSheet sheet = null;
                    try
                    {
                        if (titleblockSymbol == null)
                            sheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
                        else
                            sheet = ViewSheet.Create(doc, titleblockSymbol.Id);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Failed to create sheet element: {ex.Message}");
                        continue;
                    }

                    try
                    {
                        sheet.SheetNumber = sheetNumber;
                        sheet.Name = sheetName;
                    }
                    catch
                    {
                        doc.Delete(sheet.Id);
                        errors.Add($"Skipped '{sheetNumber}': Number already exists.");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(s.project_phase))
                        SetSheetParam(sheet, "PROJECT PHASE", s.project_phase);

                    if (!string.IsNullOrWhiteSpace(s.discipline))
                        SetSheetParam(sheet, "DISCIPLINE", s.discipline);

                    if (!string.IsNullOrWhiteSpace(s.sheet_set))
                        SetSheetParam(sheet, "SHEET SET", s.sheet_set);

                    string note = (tbRequested && titleblockSymbol == null) ? " (⚠️ No Titleblock)" : "";
                    createdSheets.Add($"{sheetNumber} — {sheetName}{note}");
                }

                if (useTransaction)
                {
                    if (createdSheets.Count > 0)
                    {
                        tx.Commit();
                    }
                    else
                    {
                        tx.RollBack();
                        return "❌ No sheets could be created.\n" + string.Join("\n", errors);
                    }
                }

                if (createdSheets.Count == 0 && !useTransaction)
                {
                    return "❌ No sheets could be created.\n" + string.Join("\n", errors);
                }

                string result = $"{createdSheets.Count} Sheet(s) created successfully.";
                if (createdSheets.Count > 0) result += "\n" + string.Join("\n", createdSheets.Select(n => "• " + n));
                if (errors.Count > 0) result += "\n\n⚠️ Warnings:\n" + string.Join("\n", errors);

                return result;
            }
            catch (Exception ex)
            {
                if (useTransaction && tx != null && tx.HasStarted())
                {
                    tx.RollBack();
                }
                return $"❌ Critical Error: {ex.Message}";
            }
        }

        private FamilySymbol FindTitleblockSymbol(Document doc, string familyName, string typeName)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs =>
                    fs.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase) &&
                    fs.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
        }

        private void SetSheetParam(ViewSheet sheet, string paramName, string value)
        {
            Parameter p = sheet.LookupParameter(paramName);
            if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
            {
                p.Set(value);
            }
        }
    }
}