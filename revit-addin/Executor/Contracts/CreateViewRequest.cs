// ============================================================================
// CreateViewRequest.cs — Vella AI
// ----------------------------------------------------------------------------
// DTO for CreateViewsCommand
// ============================================================================
namespace A49AIRevitAssistant.Executor.Contracts
{
    public class CreateViewRequest
    {
        public string view_type { get; set; }
        public string level { get; set; }
        public string name { get; set; }
        public string template { get; set; }

        // 💥 NEW: Support for Scope Box
        public string scope_box_id { get; set; }
    }
}