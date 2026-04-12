// ============================================================================
// RenameSheetCommand.cs — Vella AI
// ============================================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;
using System.Text.RegularExpressions; // 💥 Required for Regex

namespace A49AIRevitAssistant.Executor.Commands
{
    public class RenameSheetCommand
    {
        private readonly UIApplication _uiapp;

        public RenameSheetCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        // ============================================================================
        // EXECUTE
        // ============================================================================
        public string Execute(string sheetNumber, string newValue)
        {
            if (string.IsNullOrWhiteSpace(sheetNumber))
                return "❌ Sheet number is empty.";

            if (string.IsNullOrWhiteSpace(newValue))
                return "❌ New value is empty.";

            UIDocument uidoc = _uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            // 1. FIND THE SHEET
            var sheet = FindSheet(doc, sheetNumber);
            if (sheet == null)
                return $"❌ Sheet '{sheetNumber}' not found.";

            using (Transaction tx = new Transaction(doc, "Vella - Modify Sheet"))
            {
                tx.Start();
                try
                {
                    // 2. DETECT INTENT: RENUMBER vs. RENAME
                    // Regex Strategy:
                    // Looks for standard sheet number formats:
                    // - "A1.01" (Letters + Dot + Digits)
                    // - "101"   (Digits only)
                    // - "A-101" (Letters + Dash + Digits)
                    // - "X9.99"
                    // It rejects titles with spaces like "Floor Plan" or "Detail Sheet".

                    bool isSheetNumberFormat = Regex.IsMatch(newValue, @"^[A-Za-z]{0,3}[\.\-]?\d+(\.\d+)?$");

                    if (isSheetNumberFormat)
                    {
                        // 💥 ACTION: RENUMBER (Changing Sheet Number)

                        // Check for conflict first (Sheet Numbers must be unique)
                        bool exists = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewSheet))
                            .Cast<ViewSheet>()
                            .Any(s => s.SheetNumber.Equals(newValue, StringComparison.OrdinalIgnoreCase));

                        if (exists)
                        {
                            tx.RollBack();
                            return $"❌ Cannot renumber to '{newValue}'. A sheet with that number already exists.";
                        }

                        string oldNum = sheet.SheetNumber;
                        sheet.SheetNumber = newValue; // The API call to change number

                        tx.Commit();
                        return $"✔ Sheet renumbered: '{oldNum}' ➔ '{newValue}'.";
                    }
                    else
                    {
                        // 💥 ACTION: RENAME (Changing Sheet Title)

                        string oldName = sheet.Name;
                        sheet.Name = newValue; // The API call to change title/name

                        tx.Commit();
                        return $"✔ Sheet renamed: '{oldName}' ➔ '{newValue}'.";
                    }
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    tx.RollBack();
                    return "❌ Invalid character in name or number.";
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return $"❌ Error modifying sheet: {ex.Message}";
                }
            }
        }

        // ============================================================================
        // FIND SHEET BY SHEET NUMBER
        // ============================================================================
        private ViewSheet FindSheet(Document doc, string number)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => s.SheetNumber.Equals(number, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
        }
    }
}