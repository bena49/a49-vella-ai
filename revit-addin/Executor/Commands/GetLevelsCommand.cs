// ============================================================================
// GetLevelsCommand.cs  —  Vella AI
// ----------------------------------------------------------------------------
// Purpose:
//   • Collects the active project's levels and posts them to Django as
//     "list_levels_result" so the level_matcher can resolve user-supplied
//     tokens (SITE, TOP, RF, L1, B1M ...) to the project's exact Revit
//     level names regardless of naming convention (English / Thai / mixed).
//   • Follows the same Django-round-trip pattern as ListViewsCommand /
//     ListSheetsCommand: posts the result, then auto-executes any
//     "revit_command" follow-up envelope returned by Django.
//
// Called by envelope: { "command": "get_levels", "session_key": "..." }
// ============================================================================

using A49AIRevitAssistant.Models;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class GetLevelsCommand
    {
        private readonly UIApplication _uiapp;

        public GetLevelsCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        public string Execute(string sessionKey)
        {
            try
            {
                UIDocument uidoc = _uiapp.ActiveUIDocument;
                Document doc = uidoc.Document;

                A49Logger.Log("🔍 GetLevelsCommand: Collecting project levels...");

                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .Select(l => new
                    {
                        name = l.Name ?? "Unnamed",
                        elevation_mm = Math.Round(
                            UnitUtils.ConvertFromInternalUnits(l.Elevation, UnitTypeId.Millimeters),
                            2)
                    })
                    .ToList();

                A49Logger.Log($"📤 Sending {levels.Count} levels to Django for Session: {sessionKey}");

                JObject response = Task.Run(async () => await DjangoBridge.SendAsync(new
                {
                    list_levels_result = levels,
                    session_key = sessionKey
                })).GetAwaiter().GetResult();

                if (response == null)
                    return "❌ No response from Vella (Check Internet/Logs).";

                // Auto-execute follow-up command (the original create_view /
                // create_sheet envelope, now with resolved level names).
                if (response["revit_command"] is JObject cmdObj)
                {
                    A49Logger.Log("⚡ Follow-up command detected in GetLevels response.");
                    try
                    {
                        var followUpEnvelope = cmdObj.ToObject<RevitCommandEnvelope>();
                        var executor = new DraftingCommandExecutor(_uiapp);
                        string followUpResult = executor.ExecuteEnvelope(followUpEnvelope);

                        string aiMessage = response["message"]?.ToString() ?? "";
                        return string.IsNullOrWhiteSpace(aiMessage)
                            ? followUpResult
                            : $"{aiMessage}\n{followUpResult}";
                    }
                    catch (Exception ex)
                    {
                        return $"❌ Error executing follow-up command: {ex.Message}";
                    }
                }

                if (response["message"] != null)
                    return response["message"].ToString();

                return "✅ Levels synced (Silent success).";
            }
            catch (Exception ex)
            {
                return $"❌ Exception in GetLevelsCommand: {ex.Message}\n{ex.StackTrace}";
            }
        }
    }
}
