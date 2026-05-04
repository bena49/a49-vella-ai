// ============================================================================
// ListScopeBoxesCommand.cs
// 1. Collects Scope Boxes
// 2. Sends to Django via DjangoBridge
// 3. Handles immediate follow-up responses
// ============================================================================
using A49AIRevitAssistant.Models;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class ListScopeBoxesCommand
    {
        private readonly UIApplication _uiapp;

        public ListScopeBoxesCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        // 💥 NOTE: We added 'sessionKey' parameter to match ListViewsCommand
        public string Execute(string sessionKey)
        {
            UIDocument uidoc = _uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // 1. Collect Scope Boxes
                var scopeBoxes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Select(sb => new
                    {
                        name = sb.Name,
                        id = sb.UniqueId
                    })
                    .OrderBy(x => x.name)
                    .ToList();

                var payload = new
                {
                    scope_boxes = scopeBoxes,
                    count = scopeBoxes.Count
                };

                // 2. Send to Django (DjangoBridge pattern)
                JObject response = Task.Run(async () => await DjangoBridge.SendAsync(new
                {
                    list_scope_boxes_result = payload,
                    session_key = sessionKey
                })).GetAwaiter().GetResult();

                if (response == null)
                    return "❌ No response from Vella (Check Internet/Logs).";

                // 3. AUTO-EXECUTE FOLLOW-UP
                if (response["revit_command"] is JObject cmdObj)
                {
                    A49Logger.Log("⚡ Follow-up command detected in ListScopeBoxes response.");

                    try
                    {
                        var followUpEnvelope = cmdObj.ToObject<RevitCommandEnvelope>();
                        var executor = new DraftingCommandExecutor(_uiapp);
                        string followUpResult = executor.ExecuteEnvelope(followUpEnvelope);

                        string aiMessage = response["message"]?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(aiMessage))
                            return followUpResult;
                        else
                            return $"{aiMessage}\n{followUpResult}";
                    }
                    catch (System.Exception ex)
                    {
                        return $"❌ Error executing follow-up: {ex.Message}";
                    }
                }

                // 4. Default Return
                if (response["message"] != null)
                {
                    return response["message"].ToString();
                }

                return "✅ Scope boxes cached (Silent success).";
            }
            catch (System.Exception ex)
            {
                return $"❌ Error listing scope boxes: {ex.Message}";
            }
        }
    }
}