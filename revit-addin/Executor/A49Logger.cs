using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using Autodesk.Revit.DB;

namespace A49AIRevitAssistant.Executor
{
    public static class A49Logger
    {
        // 💥💥💥 THE TOGGLE: Set to true to write logs, false to disable file writing.
        public static bool EnableFileLogging = false;

        private static readonly string LogPath;

        static A49Logger()
        {
            try
            {
                string folder = @"C:\ProgramData\Autodesk\Revit\Addins\2024\A49AIRevitAssistantAddin";

                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                LogPath = Path.Combine(folder, "A49AI_Debug.log");
            }
            catch (Exception ex)
            {
                string fallbackFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "A49AIRevitAssistant_FallbackLogs"
                );

                if (!Directory.Exists(fallbackFolder))
                    Directory.CreateDirectory(fallbackFolder);

                LogPath = Path.Combine(fallbackFolder, "A49AI_Debug.log");

                Debug.WriteLine("❌ Failed to create ProgramData log directory: " + ex.Message);
                Debug.WriteLine("➡ Using fallback path: " + LogPath);
            }
        }

        // ----------------------------------------------------
        // CORE LOG
        // ----------------------------------------------------
        public static void Log(string message)
        {
            WriteLine("GLOBAL", "INFO", message, null);
        }

        // ----------------------------------------------------
        // SESSION AWARE LOG
        // ----------------------------------------------------
        public static void Log(
            string sessionKey,
            string source,
            string message,
            Document doc = null)
        {
            WriteLine(sessionKey, source, message, doc);
        }

        // ----------------------------------------------------
        // INTERNAL WRITER
        // ----------------------------------------------------
        private static void WriteLine(
            string sessionKey,
            string source,
            string message,
            Document doc)
        {
            try
            {
                string docName = doc != null ? doc.Title : "N/A";
                int threadId = Thread.CurrentThread.ManagedThreadId;

                string line =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] " +
                    $"[Session:{sessionKey}] " +
                    $"[Source:{source}] " +
                    $"[Thread:{threadId}] " +
                    $"[Doc:{docName}] " +
                    $"{message}";

                // 💥 ONLY write to the hard drive if the toggle is ON
                if (EnableFileLogging)
                {
                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }

                // We leave this outside the 'if' so you can still see errors 
                // in the Visual Studio Output window during active debugging!
                Debug.WriteLine(line);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("❌ Logger failed: " + ex.Message);
            }
        }

        public static string GetLogPath() => LogPath;
    }
}
