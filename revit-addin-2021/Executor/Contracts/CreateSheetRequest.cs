// ============================================================================
// CreateSheetRequest.cs — Vella AI
// ----------------------------------------------------------------------------
// DTO for CreateSheetsCommand
// ============================================================================
namespace A49AIRevitAssistant.Executor.Contracts
{
    public class CreateSheetRequest
    {
        public string sheet_number { get; set; }       // "A1.01", "X2.03", etc.
        public string sheet_name { get; set; }         // "First Floor Plan"
        public string sheet_type { get; set; }         // A0–A9, X0–X9
        public string project_phase { get; set; }      // "03 - CONSTRUCTION DOCUMENTS"
        public string discipline { get; set; }         // "ARCHITECTURE"
        public string sheet_set { get; set; }          // "A1_FLOOR PLANS"
        public string titleblock_family { get; set; }  // "A49_TB_A1_Horizontal"
        public string titleblock_type { get; set; }    // "Plan Sheet"
    }
}
