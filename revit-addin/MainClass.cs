using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using System;
using System.Reflection;
using System.Windows.Media.Imaging;
using System.Threading.Tasks; // ✅ Required for Async GA Tracking
using A49AIRevitAssistant.UI;
using A49AIRevitAssistant.Executor;
using A49LicenseManager;      // ✅ Required for License Validator

namespace A49AIRevitAssistant
{
    public class MainClass : IExternalApplication
    {
        // Dockable Pane GUID (must stay constant forever)
        public static readonly Guid PaneGuid =
            new Guid("a7c1c513-8b3b-4a00-90d6-5d6cd2b9c1bb");

        public static UIControlledApplication UIControlledApp;

        private WebViewResultSender _resultSender;

        public Result OnStartup(UIControlledApplication app)
        {
            // ✅ Step 1: License validation before anything else loads
            if (!LicenseValidator.IsLicenseValid())
            {
                TaskDialog.Show("License Error",
                    "This machine is not authorized to use the A49 AI Revit Assistant plugin.\n\nPlease contact the IRIs team.");
                return Result.Failed;
            }

            UIControlledApp = app;

            // 1. Create or find the Ribbon Tab
            try
            {
                app.CreateRibbonTab("A49 Standards");
            }
            catch (Exception)
            {
                // Ignore if the tab already exists
            }

            // 2. Create or find the "Assistant" Panel
            RibbonPanel ribbonPanel = null;
            foreach (RibbonPanel panel in app.GetRibbonPanels("A49 Standards"))
            {
                if (panel.Name == "Assistant")
                {
                    ribbonPanel = panel;
                    break;
                }
            }

            if (ribbonPanel == null)
            {
                ribbonPanel = app.CreateRibbonPanel("A49 Standards", "Assistant");
            }

            // 3. Get the assembly path
            Assembly assembly = Assembly.GetExecutingAssembly();
            string dllPath = assembly.Location;

            // 4. Create the Button Data
            PushButtonData buttonData = new PushButtonData(
                "ShowAIPanel",
                "Vella",
                dllPath,
                "A49AIRevitAssistant.ShowPanel");

            // 5. Add button to panel and configure Icon & Tooltip
            PushButton vellaButton = ribbonPanel.AddItem(buttonData) as PushButton;
            vellaButton.ToolTip = "AI Revit Assistant";

            // Map the embedded resource image (Format: Namespace.Folder.FileName)
            vellaButton.LargeImage = GetEmbeddedImage("A49AIRevitAssistant.Resources.icon_STD_Vella_COLOR_48x48.png");
            vellaButton.Image = GetEmbeddedImage("A49AIRevitAssistant.Resources.icon_STD_Vella_COLOR_48x48.png");

            // Register Dockable Panel (correct provider pattern)
            RegisterDockablePane(app);

            // Capture UIApplication from Revit activation events
            app.ViewActivated += OnViewActivated;

            // 🔥 CRITICAL FIX: Register the final result sender
            _resultSender = new WebViewResultSender();
            _resultSender.Register(app);

            // ✅ Step 2: Send Google Analytics event for API Startup (Async)
            var gaTracking = new GA_Tracking();
            string eventLabel = "A49 AI Revit Assistant R24";
            var analyticsTask = gaTracking.SendAnalyticsEventWithLocationAsync("api_startup", eventLabel);

            analyticsTask.ContinueWith(task =>
            {
                if (task.Exception != null)
                {
                    A49Logger.Log("❌ Startup Analytics Error: " + task.Exception.Message);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);

            return Result.Succeeded;
        }

        // ───────────────────────────────────────────────
        // CAPTURE UIApplication (modern Revit pattern)
        // ───────────────────────────────────────────────
        private void OnViewActivated(object sender, Autodesk.Revit.UI.Events.ViewActivatedEventArgs e)
        {
            if (sender is UIApplication uiApp)
            {
                UIApplicationHolder.UIApp = uiApp;

                // Initialize dispatcher when UIApp becomes available
                CommandEventDispatcher.Initialize();
            }
        }

        // ───────────────────────────────────────────────
        // REGISTER DOCKABLE PANE  (FINAL WORKING VERSION)
        // ───────────────────────────────────────────────
        public Result RegisterDockablePane(UIControlledApplication app)
        {
            DockablePaneId id = new DockablePaneId(MainClass.PaneGuid);

            // Register USING PROVIDER CLASS — NOT DockablePaneViewer itself
            var provider = new A49PaneProvider();

            app.RegisterDockablePane(
                id,
                "A49 AI Revit Assistant",
                provider
            );

            return Result.Succeeded;
        }

        // ───────────────────────────────────────────────
        // SHUTDOWN
        // ───────────────────────────────────────────────
        public Result OnShutdown(UIControlledApplication app)
        {
            try
            {
                app.ViewActivated -= OnViewActivated;
                UIApplicationHolder.UIApp = null;
                return Result.Succeeded;
            }
            catch
            {
                return Result.Succeeded;
            }
        }

        // ───────────────────────────────────────────────
        // EMBEDDED IMAGE LOADER
        // ───────────────────────────────────────────────
        private BitmapImage GetEmbeddedImage(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;

                    BitmapImage img = new BitmapImage();
                    img.BeginInit();
                    img.StreamSource = stream;
                    img.EndInit();
                    return img;
                }
            }
            catch { return null; }
        }
    }

    // ───────────────────────────────────────────────
    // SHOW PANEL COMMAND
    // ───────────────────────────────────────────────
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ShowPanel : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
                              ref string message,
                              ElementSet elements)
        {
            try
            {
                // ✅ Step 3: Secondary License validation before opening the panel
                if (!LicenseValidator.IsLicenseValid())
                {
                    TaskDialog.Show("License Error",
                        "This machine is not authorized to use the A49 AI Revit Assistant plugin.\n\nPlease contact the IRIs team.");
                    return Result.Failed;
                }

                // ✅ Step 4: Initialize Google Analytics tracking for opening the panel (Async)
                var gaTracking = new GA_Tracking();
                string eventLabel = "A49 AI Revit Assistant R24";
                var analyticsTask = gaTracking.SendAnalyticsEventWithLocationAsync("plugin_opened", eventLabel);

                analyticsTask.ContinueWith(task =>
                {
                    if (task.Exception != null)
                    {
                        A49Logger.Log("❌ Plugin Opened Analytics Error: " + task.Exception.Message);
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);

                // 1️⃣ Capture UIApplication (must ALWAYS happen first)
                UIApplicationHolder.UIApp = commandData.Application;
                A49Logger.Log("📌 ShowPanel: UIApplication captured.");

                // 2️⃣ Initialize the dispatcher — but ONLY here!
                CommandEventDispatcher.Initialize();
                A49Logger.Log("🚀 Dispatcher initialized from ShowPanel (correct place)");

                // 3️⃣ Open the dockable pane
                DockablePaneId id = new DockablePaneId(MainClass.PaneGuid);
                DockablePane pane = commandData.Application.GetDockablePane(id);
                pane.Show();
                A49Logger.Log("📌 ShowPanel: Dockable pane shown.");

                // 4️⃣ Initialize the WebView after pane is visible
                if (DockablePaneViewer.Instance != null)
                {
                    A49Logger.Log("📌 ShowPanel: Calling WebView Initialization…");
                    DockablePaneViewer.Instance.InitializeWebView();
                }
                else
                {
                    A49Logger.Log("⚠️ ShowPanel: DockablePaneViewer.Instance is NULL.");
                    TaskDialog.Show("AI Assistant",
                        "⚠️ Error: Panel instance is not ready.\nTry closing & reopening the AI panel.");
                }
            }
            catch (Exception ex)
            {
                A49Logger.Log("❌ ShowPanel FAILED: " + ex.Message);
                TaskDialog.Show("AI Assistant Error", ex.Message);
                return Result.Failed;
            }

            return Result.Succeeded;
        }
    }
}