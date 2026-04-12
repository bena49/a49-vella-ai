// ============================================================================
// ListSheetsCommand.cs 
// 1. Handles JObject response safely.
// 2. Extracts session_key correctly to maintain the handshake.
// ============================================================================
using A49AIRevitAssistant.Models;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq; 
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class ListSheetsCommand
    {
        private readonly UIApplication _uiapp;

        public ListSheetsCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        public string Execute(string sessionKey)
        {
            UIDocument uidoc = _uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            // 1. Collect Sheets
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .OrderBy(s => s.SheetNumber)
                .Select(s => new
                {
                    number = s.SheetNumber,
                    name = s.Name
                })
                .ToList();

            A49Logger.Log($"📤 Sending {sheets.Count} sheets to Django for Session: {sessionKey}");

            // 2. Send to Django (Thread-Safe Call)
            // Uses Task.Run to offload to threadpool.
            // Since DjangoBridge now uses ConfigureAwait(false), GetResult() will NOT deadlock.
            JObject response = Task.Run(async () => await DjangoBridge.SendAsync(new
            {
                list_sheets_result = sheets,
                session_key = sessionKey  // Django uses this to resume intent
            })).GetAwaiter().GetResult();

            if (response == null)
                return "❌ No response from Vella.";

            // --------------------------------------------------------
            // 3. AUTO-EXECUTE FOLLOW-UP (Strongly Typed JObject Handling)
            // --------------------------------------------------------

            // Check if "revit_command" exists and is valid
            if (response["revit_command"] is JObject cmdObj)
            {
                A49Logger.Log("⚡ Follow-up command detected in ListSheets response. Executing...");
                try
                {
                    // Convert JObject to Envelope
                    var followUpEnvelope = cmdObj.ToObject<RevitCommandEnvelope>();

                    var executor = new DraftingCommandExecutor(_uiapp);
                    string followUpResult = executor.ExecuteEnvelope(followUpEnvelope);

                    // 💥 Handle empty AI message gracefully
                    string aiMessage = response["message"]?.ToString() ?? "";

                    if (string.IsNullOrWhiteSpace(aiMessage))
                    {
                        return followUpResult;
                    }
                    else
                    {
                        return $"{aiMessage}\n{followUpResult}";
                    }
                }
                catch (System.Exception ex)
                {
                    return $"❌ Error executing follow-up command: {ex.Message}";
                }
            }

            // 4. Default Message Handling
            if (response["message"] != null)
                return response["message"].ToString();

            return "✅ Sheets synced.";
        }
    }
}