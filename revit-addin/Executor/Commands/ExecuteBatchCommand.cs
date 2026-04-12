// ============================================================================
// ExecuteBatchCommand.cs — Vella AI
// ----------------------------------------------------------------------------
// Executes a sequential list of commands from a BatchCommandEnvelope.
// ============================================================================

using Autodesk.Revit.UI;
using Autodesk.Revit.DB; // 💥 REQUIRED FOR MASTER TRANSACTION
using System;
using System.Collections.Generic;
using System.Text;
using A49AIRevitAssistant.Models;
using System.Linq;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class ExecuteBatchCommand
    {
        private readonly UIApplication _uiapp;
        private readonly DraftingCommandExecutor _executor;

        public ExecuteBatchCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
            _executor = new DraftingCommandExecutor(uiapp);
        }

        public string Execute(RevitCommandEnvelope env)
        {
            List<RevitCommandEnvelope> steps = env.steps;
            Dictionary<string, string> messageOverrideData = env.message_override_data;

            if (steps == null || steps.Count == 0)
                return "❌ Batch command failed: No steps provided.";

            A49Logger.Log($"🚀 Starting batch execution of {steps.Count} steps...");

            StringBuilder summary = new StringBuilder();
            int successfulSteps = 0;

            Document doc = _uiapp.ActiveUIDocument.Document;

            try
            {
                // 💥 ONE MASTER TRANSACTION FOR THE ENTIRE BATCH
                using (Transaction masterTx = new Transaction(doc, "Vella: Batch Execution"))
                {
                    masterTx.Start();

                    foreach (var step in steps)
                    {
                        if (string.IsNullOrWhiteSpace(step?.command)) continue;

                        A49Logger.Log($"    -> Executing step: {step.command}");

                        // 💥 Pass 'false' here to tell the assistant NOT to use inner transactions!
                        string result = _executor.ExecuteEnvelope(step, false);

                        if (result.StartsWith("❌"))
                        {
                            summary.AppendLine($"❌ Batch interrupted by error in '{step.command}':");
                            summary.AppendLine(result);
                            A49Logger.Log($"    -> Step failed. Aborting batch.");

                            masterTx.RollBack(); // Cancel the whole batch if one step fails
                            return summary.ToString();
                        }
                        else
                        {
                            summary.AppendLine($"✔ Step '{step.command}' completed.");
                            successfulSteps++;
                        }
                    }

                    // 💥 COMMIT ONCE AT THE VERY END
                    masterTx.Commit();
                }

                // Final success message logic
                if (successfulSteps == steps.Count)
                {
                    if (messageOverrideData != null)
                    {
                        string sheetName = messageOverrideData.ContainsKey("sheet_name") ? messageOverrideData["sheet_name"] : "";

                        if (sheetName.Contains("•") || sheetName.Contains("created successfully"))
                        {
                            return sheetName;
                        }

                        string viewName = messageOverrideData.ContainsKey("view_name") ? messageOverrideData["view_name"] : "the view";
                        string sheetNumber = messageOverrideData.ContainsKey("sheet_number") ? messageOverrideData["sheet_number"] : "the sheet";
                        string safeSheetName = string.IsNullOrEmpty(sheetName) ? "unknown" : sheetName;

                        return $"View {viewName} created and successfully placed on Sheet {sheetNumber} - {safeSheetName} in your design stage.";
                    }

                    return $"Batch Success! All {successfulSteps} steps completed.";
                }
                else
                {
                    return $"⚠️ Batch finished with {successfulSteps} successful steps out of {steps.Count}:\n" + summary.ToString();
                }
            }
            catch (Exception ex)
            {
                A49Logger.Log($"CRITICAL BATCH EXECUTION ERROR: {ex.Message}");
                return $"❌ Batch command failed due to an unexpected exception: {ex.Message}";
            }
        }
    }
}