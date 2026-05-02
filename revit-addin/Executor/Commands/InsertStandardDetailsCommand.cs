// ============================================================================
// A49AIRevitAssistant/Executor/Commands/InsertStandardDetailsCommand.cs
// ============================================================================
// Streamlines Revit's native "Insert Views from File" workflow.
//
// Two modes:
//   - "preview" : returns Revit version + computed UNC path + file existence,
//                 used by the wizard to show live status before the user clicks
//                 Browse. No side effects.
//   - "execute" : copies the resolved file path to the clipboard and posts
//                 ID_INSERT_VIEWS_FROM_FILE. The user pastes (Ctrl+V) the path
//                 in Revit's native dialog and proceeds with view selection.
//
// Envelope payload (env.raw):
//   {
//     "mode":    "preview" | "execute",
//     "package": "standard" | "eia"
//   }
// ============================================================================

using System;
using System.IO;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace A49AIRevitAssistant.Executor.Commands
{
    public class InsertStandardDetailsCommand
    {
        private readonly UIApplication _uiapp;

        // WebDAV-as-UNC root for the Revit standard detail files. Same server as
        // standards.json's templates path; access is mounted at Revit startup
        // by a separate A49 utility, so no credential handling is required here.
        private const string ServerRoot =
            @"\\159.192.115.63@4999\DavWWWRoot\03_BIM_References\02_Revit_Standard_Detail_File";

        // Folder + file naming conventions per package type.
        private const string StandardFolder = "20240905_A49_Standard_Details_Package";
        private const string EiaFolder      = "20240905_A49_EIA_Details_Package";
        private const string StandardPrefix = "A49_Standard_Details_V";
        private const string EiaPrefix      = "A49_EIA_Details_V";

        public InsertStandardDetailsCommand(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        public string Execute(Models.RevitCommandEnvelope env)
        {
            try
            {
                JObject payload = env.raw;
                string mode    = (payload?.Value<string>("mode")    ?? "execute").ToLowerInvariant();
                string package = (payload?.Value<string>("package") ?? "").ToLowerInvariant();

                if (package != "standard" && package != "eia")
                    return BuildError("Invalid package. Must be 'standard' or 'eia'.");

                // Resolve Revit version → "2024" → "V24"
                string versionNumber = _uiapp.Application.VersionNumber; // "2024", "2025", ...
                string versionSuffix = ResolveVersionSuffix(versionNumber);

                // Resolve folder + file name
                string folder   = (package == "standard") ? StandardFolder : EiaFolder;
                string prefix   = (package == "standard") ? StandardPrefix : EiaPrefix;
                string fileName = $"{prefix}{versionSuffix}.rvt";
                string fullPath = Path.Combine(ServerRoot, folder, fileName);

                bool fileExists = false;
                try { fileExists = File.Exists(fullPath); } catch { }

                if (mode == "preview")
                    return BuildPreviewResult(versionNumber, fileName, fullPath, fileExists, package);

                // ── EXECUTE ─────────────────────────────────────────────────
                if (!fileExists)
                    return BuildError(
                        $"File not found on server: {fileName}. " +
                        $"Either the file is missing for Revit {versionNumber} or the network drive is not mounted.");

                // Clipboard.SetText must run on an STA thread. The Revit UI
                // dispatcher gives us that; the executor itself is already on it.
                bool clipboardOk = false;
                try
                {
                    System.Windows.Clipboard.SetText(fullPath);
                    clipboardOk = true;
                }
                catch (Exception cex)
                {
                    A49Logger.Log($"⚠️ InsertStandardDetails: clipboard failed: {cex.Message}");
                }

                // Fire Revit's native Insert Views from File command.
                RevitCommandId commandId = RevitCommandId.LookupCommandId("ID_INSERT_VIEWS_FROM_FILE");
                if (commandId == null)
                    return BuildError("Revit command ID_INSERT_VIEWS_FROM_FILE not available.");

                _uiapp.PostCommand(commandId);

                return BuildExecuteResult(versionNumber, fileName, fullPath, clipboardOk, package);
            }
            catch (Exception ex)
            {
                A49Logger.Log($"❌ InsertStandardDetails failed: {ex}");
                return BuildError(ex.Message);
            }
        }

        // ── HELPERS ─────────────────────────────────────────────────────────

        // "2024" → "24". Defensive against unexpected formats; falls back to the
        // last two characters of the version string.
        private static string ResolveVersionSuffix(string versionNumber)
        {
            if (string.IsNullOrEmpty(versionNumber)) return "00";
            string trimmed = versionNumber.Trim();
            if (trimmed.Length >= 2) return trimmed.Substring(trimmed.Length - 2);
            return trimmed;
        }

        private static string BuildPreviewResult(string version, string fileName, string fullPath, bool fileExists, string package)
        {
            var obj = new JObject
            {
                ["insert_standard_details_result"] = new JObject
                {
                    ["status"]         = "preview",
                    ["mode"]           = "preview",
                    ["package"]        = package,
                    ["revit_version"]  = version,
                    ["file_name"]      = fileName,
                    ["file_path"]      = fullPath,
                    ["file_exists"]    = fileExists,
                    ["server_reachable"] = fileExists  // Best signal we have without an extra round-trip
                }
            };
            return JsonConvert.SerializeObject(obj);
        }

        private static string BuildExecuteResult(string version, string fileName, string fullPath, bool clipboardOk, string package)
        {
            string msg = clipboardOk
                ? $"📚 {fileName} path copied to clipboard. Paste with Ctrl+V in Revit's dialog, then press Enter."
                : $"📚 Revit's Insert Views dialog is opening. Path: {fullPath}";

            var obj = new JObject
            {
                ["insert_standard_details_result"] = new JObject
                {
                    ["status"]         = "success",
                    ["mode"]           = "execute",
                    ["package"]        = package,
                    ["revit_version"]  = version,
                    ["file_name"]      = fileName,
                    ["file_path"]      = fullPath,
                    ["clipboard_set"]  = clipboardOk,
                    ["message"]        = msg
                }
            };
            return JsonConvert.SerializeObject(obj);
        }

        private static string BuildError(string message)
        {
            A49Logger.Log($"❌ InsertStandardDetails: {message}");
            var obj = new JObject
            {
                ["insert_standard_details_result"] = new JObject
                {
                    ["status"]  = "error",
                    ["message"] = message
                }
            };
            return JsonConvert.SerializeObject(obj);
        }
    }
}
