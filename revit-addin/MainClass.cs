using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using System;
using System.Reflection;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using A49AIRevitAssistant.UI;
using A49AIRevitAssistant.Executor;
using A49LicenseManager;

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
            // ✅ Step 1: License validation
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
            catch (Exception) { /* Tab exists */ }

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

            // 3. Get assembly path and define Button
            Assembly assembly = Assembly.GetExecutingAssembly();
            string dllPath = assembly.Location;

            PushButtonData buttonData = new PushButtonData(
                "ShowAIPanel",
                "Vella",
                dllPath,
                "A49AIRevitAssistant.ShowPanel");

            // 4. Add button and configure Icon & Tooltip
            PushButton vellaButton = ribbonPanel.AddItem(buttonData) as PushButton;
            vellaButton.ToolTip = "AI Revit Assistant (Vella)";

            // Load images with .NET 8 compatibility
            string iconPath = "A49AIRevitAssistant.Resources.icon_STD_Vella_COLOR_48x48.png";
            vellaButton.LargeImage = GetEmbeddedImage(iconPath);
            vellaButton.Image = GetEmbeddedImage(iconPath);

            // 5. Register Dockable Panel
            RegisterDockablePane(app);

            // 6. Capture UIApplication and result sender
            app.ViewActivated += OnViewActivated;
            _resultSender = new WebViewResultSender();
            _resultSender.Register(app);

            // ✅ Step 2: Version-Specific Analytics (Async)
#if REVIT2025
            string eventLabel = "A49 AI Revit Assistant R25";
#else
            string eventLabel = "A49 AI RevitAssistant R24";
#endif
            var gaTracking = new GA_Tracking();
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

        private void OnViewActivated(object sender, Autodesk.Revit.UI.Events.ViewActivatedEventArgs e)
        {
            if (sender is UIApplication uiApp)
            {
                UIApplicationHolder.UIApp = uiApp;
                CommandEventDispatcher.Initialize();
            }
        }

        public Result RegisterDockablePane(UIControlledApplication app)
        {
            DockablePaneId id = new DockablePaneId(MainClass.PaneGuid);
            var provider = new A49PaneProvider();

            app.RegisterDockablePane(
                id,
                "A49 AI Revit Assistant",
                provider
            );

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            try
            {
                app.ViewActivated -= OnViewActivated;
                UIApplicationHolder.UIApp = null;
                return Result.Succeeded;
            }
            catch { return Result.Succeeded; }
        }

        // ───────────────────────────────────────────────
        // EMBEDDED IMAGE LOADER (.NET 8 & DPI-Aware)
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
                    // CRITICAL for R25 (.NET 8): Load immediately so stream can be disposed
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.StreamSource = stream;
                    img.EndInit();
                    // Ensure the image is thread-safe
                    img.Freeze();
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
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (!LicenseValidator.IsLicenseValid())
                {
                    TaskDialog.Show("License Error", "Machine not authorized.");
                    return Result.Failed;
                }

                UIApplicationHolder.UIApp = commandData.Application;
                CommandEventDispatcher.Initialize();

                DockablePaneId id = new DockablePaneId(MainClass.PaneGuid);
                DockablePane pane = commandData.Application.GetDockablePane(id);
                pane.Show();

                if (DockablePaneViewer.Instance != null)
                {
                    DockablePaneViewer.Instance.InitializeWebView();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                A49Logger.Log("❌ ShowPanel FAILED: " + ex.Message);
                return Result.Failed;
            }
        }
    }
}