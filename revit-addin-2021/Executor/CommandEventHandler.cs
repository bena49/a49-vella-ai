// ============================================================================
// CommandEventHandler.cs — Multi-User Session Enabled (Revit 2024)
// ============================================================================

using System;
using Autodesk.Revit.UI;
using A49AIRevitAssistant.Models;
using A49AIRevitAssistant.UI; // Access to DockablePaneViewer

namespace A49AIRevitAssistant.Executor
{
    public class CommandEventHandler : IExternalEventHandler
    {
        public RevitCommandEnvelope PendingEnvelope { get; set; }
        public string ResultMessage { get; set; }
        public bool IsCompleted { get; internal set; }

        public void Execute(UIApplication uiapp)
        {
            if (PendingEnvelope == null) return;

            // 💥 CAPTURE SESSION KEY for the final callback
            string currentSessionKey = PendingEnvelope.session_key;

            try
            {
                var executor = new DraftingCommandExecutor(uiapp);
                string result = executor.ExecuteEnvelope(PendingEnvelope);

                ResultMessage = result ?? "✔ Command executed successfully.";
            }
            catch (Exception ex)
            {
                ResultMessage = $"❌ Exception in CommandEventHandler: {ex.Message}";
                A49Logger.Log(ResultMessage);
            }
            finally
            {
                // 💥 FORWARD TO UI: Pass the result AND the session key back to Vue
                if (DockablePaneViewer.Instance != null)
                {
                    // Pass both pieces of data to maintain the session link
                    DockablePaneViewer.Instance.PostResultToVue(ResultMessage, currentSessionKey);
                }

                PendingEnvelope = null;
                IsCompleted = true;
                A49Logger.Log($"🏁 Command finished for Session: {currentSessionKey}");
            }
        }

        public string GetName() => "A49AIRevitAssistant Command Event Handler";
    }

    public static class CommandEventDispatcher
    {
        public static ExternalEvent ExternalEvent { get; private set; }
        public static CommandEventHandler Handler { get; private set; }

        public static void Initialize()
        {
            if (ExternalEvent != null) return;
            Handler = new CommandEventHandler();
            ExternalEvent = ExternalEvent.Create(Handler);
            A49Logger.Log("🔥 CommandEventDispatcher initialized.");
        }

        public static void Raise(RevitCommandEnvelope envelope)
        {
            if (envelope == null) return;

            Handler.ResultMessage = null;
            Handler.IsCompleted = false;

            // 💥 The envelope now carries the session_key to the handler
            Handler.PendingEnvelope = envelope;

            A49Logger.Log(
                envelope.session_key ?? "NO_SESSION",
                "CommandEventDispatcher",
                $"Raising ExternalEvent for command: {envelope.command}"
            );


            ExternalEvent.Raise(); //
        }
    }
}