// ============================================================================
// WebViewResultSender.cs — DISABLED (avoid duplicate callbacks)
// ============================================================================

using Autodesk.Revit.UI;

namespace A49AIRevitAssistant.Executor
{
    public class WebViewResultSender
    {
        // Intentionally disabled:
        // CommandEventHandler already sends results back to Vue with the correct session_key.
        public void Register(UIControlledApplication app)
        {
            A49Logger.Log("📌 WebViewResultSender is disabled (CommandEventHandler handles result posting).");
        }
    }
}
