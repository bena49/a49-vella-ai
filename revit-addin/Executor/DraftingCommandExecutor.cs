// ============================================================================
// DraftingCommandExecutor.cs — Vella AI Router
// ============================================================================

using System;
using Autodesk.Revit.UI;
using A49AIRevitAssistant.Models;
using A49AIRevitAssistant.Executor.Commands;
using A49AIRevitAssistant.Executor.Commands.Sheets;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace A49AIRevitAssistant.Executor
{
    public class DraftingCommandExecutor
    {
        private readonly UIApplication _uiapp;

        public DraftingCommandExecutor(UIApplication uiapp)
        {
            _uiapp = uiapp;

            // THAI LANGUAGE SUPPORT
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        }

        // ============================================================================
        // MAIN ROUTER
        // ============================================================================
        public string ExecuteEnvelope(RevitCommandEnvelope env, bool useTransaction = true)
        {
            if (env == null || env.command == null)
                return "❌ Invalid envelope.";

            // Grab the token from the envelope and hand it to the Bridge!
            if (!string.IsNullOrEmpty(env.token))
            {
                DjangoBridge.CurrentToken = env.token;
            }

            string cmd = env.command.Trim().ToLower();
            A49Logger.Log("🛠 Router received command string: '" + cmd + "'");

            if (cmd.StartsWith("list_"))
            {
                env.views = null;
                env.sheets = null;
                env.target = null;
                env.new_name = null;
                env.mode = null;
                env.template = null;
                env.view = null;

                if (cmd != "list_views_on_sheet")
                    env.sheet = null;

                A49Logger.Log("🧹 Envelope sanitized for list command (safe mode).");
            }

            try
            {
                switch (cmd)
                {
                    case "revit_info":
                        return "{\"status\":\"success\", \"message\":\"Revit Connection Successful!\", \"session_key\":\"" + env.session_key + "\"}";

                    case "create_view":
                    case "create_views":
                        return new CreateViewCommand(_uiapp).Execute(env.views, useTransaction);

                    case "rename_view":
                        return new RenameViewCommand(_uiapp).Execute(env.target, env.new_name);

                    case "create_sheet":
                    case "create_sheets":
                        return new CreateSheetsCommand(_uiapp).Execute(env.sheets, useTransaction);

                    case "rename_sheet":
                        return new RenameSheetCommand(_uiapp).Execute(env.target, env.new_name);

                    case "renumber_sheets":
                        return new RenumberSheetsCommand(_uiapp).Execute(env.start_number, env.sheet_set);

                    case "rename_views_on_sheet":
                    case "batch_rename_views":
                        return new BatchRenameViewsCommand(_uiapp).Execute(env.strategy, env.find, env.replace, env.target);

                    case "duplicate_view":
                        return new DuplicateViewCommand(_uiapp).Execute(env.target, env.mode);

                    case "apply_template":
                        return new ApplyTemplateCommand(_uiapp).Execute(env.target, env.template);

                    case "place_view_on_sheet":
                        return new PlaceViewOnSheetCommand(_uiapp).Execute(env.view, env.sheet, env.placement, env.reference_sheet, useTransaction);

                    case "start_interactive_room_package":
                        // 💥 Back to your original, perfect code!
                        return new InteractiveRoomPackageCommand(_uiapp, env.raw).Execute();

                    case "execute_batch":
                        return new ExecuteBatchCommand(_uiapp).Execute(env);

                    case "fetch_project_inventory":
                        return new FetchProjectInventoryCommand(_uiapp).Execute();

                    case "execute_batch_update":
                        return new ExecuteBatchUpdateCommand(_uiapp).Execute(env);

                    case "remove_view_from_sheet":
                        return new RemoveViewFromSheetCommand(_uiapp).Execute(env.view, env.sheet);

                    case "list_views":
                    case "listviews":
                        return new ListViewsCommand(_uiapp).Execute(env.session_key);

                    case "list_sheets":
                    case "listsheets":
                        return new ListSheetsCommand(_uiapp).Execute(env.session_key);

                    case "list_views_on_sheet":
                    case "listviewsonsheet":
                        string targetSheet = env.sheet;
                        if (string.IsNullOrEmpty(targetSheet)) targetSheet = env.filter_on_sheet;
                        if (string.IsNullOrEmpty(targetSheet)) targetSheet = env.target;
                        return new ListViewsOnSheetCommand(_uiapp).Execute(targetSheet);

                    case "get_levels":
                        return new GetLevelsCommand(_uiapp).Execute();

                    case "list_scope_boxes":
                        return new ListScopeBoxesCommand(_uiapp).Execute(env.session_key);

                    case "fetch_project_info":
                        {
                            var infoCmd = new GetProjectInfoCommand(_uiapp.ActiveUIDocument.Document);
                            string jsonResult = infoCmd.Execute();

                            A49AIRevitAssistant.UI.DockablePaneViewer.Instance.Dispatcher.Invoke(() =>
                            {
                                A49AIRevitAssistant.UI.DockablePaneViewer.Instance.SendRawMessage(jsonResult);
                            });

                            return "{\"status\":\"silent\"}";
                        }

                    case "preflight_check":
                        return new PreflightCheckCommand(_uiapp).Execute(env);

                    case "preflight_repair":
                        return new PreflightRepairCommand(_uiapp).Execute(env);

                    case "automate_tag":
                        return new AutoTagCommand(_uiapp).Execute(env);

                    default:
                        return $"❌ Unknown command: {env.command}";
                }
            }
            catch (Exception ex)
            {
                return $"❌ Exception in command '{cmd}': {ex.Message}\n{ex.StackTrace}";
            }
        }

        // ============================================================================
        // UNWRAP HELPER
        // ============================================================================
        public static RevitCommandEnvelope ParseIncoming(string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                A49Logger.Log("DraftingCommandExecutor", "ParseIncoming", $"RAW JSON: {json}");

                var root = JObject.Parse(json);
                var cmdToken = root["revit_command"] ?? root;

                string envelopeJson = cmdToken.ToString(Formatting.None);

                A49Logger.Log("DraftingCommandExecutor", "ParseIncoming", $"UNWRAPPED ENVELOPE: {envelopeJson}");

                var env = JsonConvert.DeserializeObject<RevitCommandEnvelope>(envelopeJson);

                if (env != null && !string.IsNullOrEmpty(env.token))
                {
                    DjangoBridge.CurrentToken = env.token;
                }

                return env;
            }
            catch (Exception ex)
            {
                A49Logger.Log("DraftingCommandExecutor", "ParseIncoming", $"❌ Parse failed: {ex.Message}");
                return null;
            }
        }
    }
}