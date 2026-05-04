// ============================================================================
// ListViewsCommand.cs
// 1. Handles JObject response safely.
// 2. Extracts session_key correctly to maintain the handshake.
// ============================================================================
using A49AIRevitAssistant.Models;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class ListViewsCommand
    {
        private readonly UIApplication _uiapp;

        public ListViewsCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        public string Execute(string sessionKey)
        {
            UIDocument uidoc = _uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            A49Logger.Log("🔍 ListViewsCommand: Collecting raw model views...");

            // 1. Collect RAW Data (Safe projection)
            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v =>
                    !v.IsTemplate &&
                    v.ViewType != ViewType.DrawingSheet &&
                    v.ViewType != ViewType.Schedule &&
                    v.ViewType != ViewType.Legend &&
                    v.ViewType != ViewType.Report &&
                    v.ViewType != ViewType.Internal
                )
                .Select(v => new
                {
                    name = v.Name ?? "Unnamed", // Null safety
                    type = v.ViewType.ToString(),
                    id = v.Id.IntegerValue
                })
                .ToList();

            A49Logger.Log($"📤 Sending {views.Count} views to Django for Session: {sessionKey}");

            // 2. Send to Django (Thread-Safe Call)
            // We use Task.Run to offload to threadpool, and GetResult() blocks UI thread.
            // Since DjangoBridge now uses ConfigureAwait(false), this will NOT deadlock.
            JObject response = Task.Run(async () => await DjangoBridge.SendAsync(new
            {
                list_views_result = views,
                session_key = sessionKey
            })).GetAwaiter().GetResult();

            if (response == null)
                return "❌ No response from Vella (Check Internet/Logs).";

            // --------------------------------------------------------
            // 3. AUTO-EXECUTE FOLLOW-UP (Strongly Typed JObject Handling)
            // --------------------------------------------------------

            // Check if "revit_command" exists and is not empty/null
            if (response["revit_command"] is JObject cmdObj)
            {
                A49Logger.Log("⚡ Follow-up command detected in ListViews response.");

                try
                {
                    // Deserialize the inner JObject to the Envelope
                    var followUpEnvelope = cmdObj.ToObject<RevitCommandEnvelope>();

                    // Execute using the main router
                    var executor = new DraftingCommandExecutor(_uiapp);
                    string followUpResult = executor.ExecuteEnvelope(followUpEnvelope);

                    // Handle message display
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

            // 4. Default: Just return the message
            if (response["message"] != null)
            {
                return response["message"].ToString();
            }

            return "✅ Views synced (Silent success).";
        }
    }
}