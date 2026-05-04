// ============================================================================
// RevitCommandEnvelope.cs — Unified command envelope for Vella AI
// Supports ALL commands including Items 6–9, Modifiers, and List Filters
// ============================================================================

using A49AIRevitAssistant.Executor.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace A49AIRevitAssistant.Models
{
    public class RevitCommandEnvelope
    {
        // -------------------------
        // MULTI-USER SESSION KEY
        // -------------------------
        [JsonProperty("session_key")]
        public string session_key { get; set; }

        // -------------------------
        // AUTHENTICATION TOKEN
        // -------------------------
        [JsonProperty("token")]
        public string token { get; set; }

        // -------------------------
        // CORE COMMAND FIELD
        // -------------------------
        [JsonProperty("command")]
        public string command { get; set; }

        // -------------------------
        // SHARED MODE PROPERTY
        // Used by:
        // 1. Duplicate View ("DEPENDENT", "DETAILED")
        // 2. List Items ("SHEETS", "VIEWS", "VIEWS_ON_SHEET")
        // -------------------------
        [JsonProperty("mode")]
        public string mode { get; set; }

        // -------------------------
        // CREATE VIEWS
        // -------------------------
        [JsonProperty("views")]
        public List<CreateViewRequest> views { get; set; }

        // -------------------------
        // CREATE SHEETS
        // -------------------------
        [JsonProperty("sheets")]
        public List<CreateSheetRequest> sheets { get; set; }

        // -------------------------
        // RENAME VIEW or RENAME SHEET
        // -------------------------
        [JsonProperty("target")]
        public string target { get; set; }

        [JsonProperty("new_name")]
        public string new_name { get; set; }

        // -------------------------
        // APPLY TEMPLATE
        // -------------------------
        [JsonProperty("template")]
        public string template { get; set; }

        // -------------------------
        // PLACE VIEW ON SHEET / REMOVE VIEW
        // -------------------------
        [JsonProperty("view")]
        public string view { get; set; }

        [JsonProperty("sheet")]
        public string sheet { get; set; } // <--- Standard "Sheet" field

        // -------------------------
        // 💥 SMART PLACEMENT
        // -------------------------
        [JsonProperty("placement")]
        public string placement { get; set; }       // "CENTER" or "MATCH"

        [JsonProperty("reference_sheet")]
        public string reference_sheet { get; set; } // e.g. "A1.01"

        // -------------------------
        // 💥 NEW: MODIFIER COMMANDS (Renumber & Batch)
        // -------------------------
        [JsonProperty("start_number")]
        public string start_number { get; set; }    // For Renumbering (e.g., "A1.05")

        [JsonProperty("sheet_set")]
        public string sheet_set { get; set; }       // For Filtering (Placeholder)

        [JsonProperty("strategy")]
        public string strategy { get; set; }        // "match_titleblock"

        [JsonProperty("find")]
        public string find { get; set; }            // Find text (Batch Rename)

        [JsonProperty("replace")]
        public string replace { get; set; }         // Replace text (Batch Rename)

        // -------------------------
        // 💥 NEW: LIST FILTERS 
        // -------------------------
        [JsonProperty("filter_stage")]
        public string filter_stage { get; set; }

        [JsonProperty("filter_category")]
        public string filter_category { get; set; } // For Sheets (A1, A2)

        [JsonProperty("filter_type")]
        public string filter_type { get; set; }     // For Views (Elevation, Plan)

        // 💥 CRITICAL: This is the field needed for ListViewsOnSheet
        [JsonProperty("filter_on_sheet")]
        public string filter_on_sheet { get; set; } // For "Views on A1.01"

        // -------------------------
        // RAW passthrough fallback
        // -------------------------
        [JsonProperty("raw")]
        public JObject raw { get; set; }

        // -------------------------
        // BATCH COMMAND (EXECUTION)
        // -------------------------
        [JsonProperty("steps")]
        public List<RevitCommandEnvelope> steps { get; set; } // List of commands to run

        // -------------------------
        // 💥 BATCH SUCCESS MESSAGE DATA
        // Used by execute_batch to carry necessary data for final message generation
        // -------------------------
        [JsonProperty("message_override_data")]
        public Dictionary<string, string> message_override_data { get; set; }
    }
}