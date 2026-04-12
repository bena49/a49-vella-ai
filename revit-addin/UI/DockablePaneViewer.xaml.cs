using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.UI;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using A49AIRevitAssistant.Models;
using A49AIRevitAssistant.Executor;
using System.Diagnostics;
using System.Windows.Threading; // NEW: Required for DispatcherPriority

namespace A49AIRevitAssistant.UI
{
    public partial class DockablePaneViewer : Page
    {
        // Global instance (ShowPanel and WebViewResultSender use this)
        public static DockablePaneViewer Instance { get; set; }

        private WebView2 webView;

        public DockablePaneViewer()
        {
            A49Logger.Log("📌 DockablePaneViewer constructor called.");

            // NOTE: Instance is set here AND in A49PaneProvider.cs (redundancy for safety)
            Instance = this;

            InitializeComponent();

            this.Loaded += DockablePaneViewer_Loaded;
        }

        // ───────────────────────────────────────────────────────────────
        // NEW PUBLIC ENTRY — Called by Idling Handler (WebViewResultSender)
        // ───────────────────────────────────────────────────────────────
        public void PostResultToVue(string finalRevitResult, string sessionKey)
        {
            this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                // 💥 Include the session_key in the response JSON
                string jsonResponse = "{" +
                    "\"status\":\"complete\"," +
                    "\"result\":\"" + Escape(finalRevitResult) + "\"," +
                    "\"session_key\":\"" + sessionKey + "\"," +
                    "\"timestamp\":\"" + DateTime.Now.ToString("HH:mm:ss") + "\"" +
                    "}";

                A49Logger.Log("📤 Final result sent to JS with Session: " + sessionKey);
                SendResponseDirect(jsonResponse);
            }));
        }

        // ───────────────────────────────────────────────────────────────
        // MAIN LOAD EVENT
        // ───────────────────────────────────────────────────────────────
        private void DockablePaneViewer_Loaded(object sender, RoutedEventArgs e)
        {
            A49Logger.Log("📌 DockablePaneViewer Loaded event fired.");

            try
            {
                if (webView == null)
                {
                    A49Logger.Log("🔥 Initializing WebView from Loaded event…");
                    InitializeWebView();
                }
                else
                {
                    A49Logger.Log("ℹ️ WebView already exists — skipping init.");
                }
            }
            catch (Exception ex)
            {
                A49Logger.Log("❌ Error in Loaded: " + ex.Message);
            }
        }

        // ───────────────────────────────────────────────────────────────
        // PUBLIC ENTRY — SAFE WEBVIEW INITIALIZATION WRAPPER
        // ───────────────────────────────────────────────────────────────
        public void InitializeWebView()
        {
            A49Logger.Log("📌 InitializeWebView() called.");

            try
            {
                if (webView != null)
                {
                    A49Logger.Log("ℹ️ WebView already initialized — skipping.");
                    return;
                }

                InitializeWebViewSafe();
            }
            catch (Exception ex)
            {
                A49Logger.Log("❌ InitializeWebView fatal error: " + ex.Message);
                LoadErrorPage($"WebView initialization failed:\n{ex.Message}");
            }
        }


        // ───────────────────────────────────────────────────────────────
        // INTERNAL SAFE INITIALIZER — REAL WORK HAPPENS HERE
        // ───────────────────────────────────────────────────────────────
        private async void InitializeWebViewSafe()
        {
            try
            {
                A49Logger.Log("🚀 Initializing WebView2 (InitializeWebViewSafe)…");

                webView = new WebView2();
                webViewerContainer.Children.Add(webView);

                string dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "A49AIRevitAssistant",
                    "WebViewCache"
                );

                var env = await CoreWebView2Environment.CreateAsync(null, dataDir);

                webView.CoreWebView2InitializationCompleted += (s, e) =>
                {
                    A49Logger.Log($"📌 WebView2 InitializationCompleted fired (Success={e.IsSuccess})");

                    if (!e.IsSuccess)
                    {
                        A49Logger.Log("❌ WebView2 initialization failed.");
                        LoadErrorPage("WebView2 initialization failed.");
                        return;
                    }

                    A49Logger.Log("✅ WebView2 initialized successfully.");

                    // Debug Listener: Log JS → C# messages
                    webView.CoreWebView2.WebMessageReceived += (s2, e2) =>
                    {
                        try
                        {
                            string msg = e2.WebMessageAsJson;
                            A49Logger.Log($"🟦 JS → C# (Debug): {msg}");
                        }
                        catch (Exception ex)
                        {
                            A49Logger.Log("❌ Error logging JS→C# debug message: " + ex.Message);
                        }
                    };

                    // REAL HANDLER: This actually executes Revit commands
                    webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                    A49Logger.Log("🔗 Hooked OnWebMessageReceived() handler.");

                    // Debug Listener 2: Detect load failures
                    webView.CoreWebView2.NavigationCompleted += (s2, e2) =>
                    {
                        string url = webView.Source?.ToString() ?? "(unknown)";
                        if (!e2.IsSuccess)
                        {
                            A49Logger.Log($"❌ Navigation FAILED — URL={url}, Error={e2.WebErrorStatus}");
                        }
                        else
                        {
                            A49Logger.Log($"✅ Navigation SUCCESS — URL={url}");
                        }
                    };

                    webView.CoreWebView2.Settings.IsWebMessageEnabled = true;

                    // Debug Listener: Capture window.postMessage() fallback
                    webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                        window.addEventListener('message', event => {
                            try {
                                chrome.webview.postMessage(event.data);
                            } catch (e) {
                                // ignore
                            }
                        });
                    ");
                    A49Logger.Log("🧩 Injected JS postMessage bridge.");

                    // Load your Vue UI
                    A49Logger.Log("🌐 Navigating to Vella URL: https://a49iris.com/irisaiassistant/");
                    webView.Source = new Uri("https://a49iris.com/irisaiassistant/");
                };

                await webView.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                A49Logger.Log($"❌ WebView2 fatal error: {ex.Message}");
                LoadErrorPage($"WebView2 fatal error:\n{ex.Message}");
            }
        }


        // ───────────────────────────────────────────────────────────────
        // JS → REVIT MESSAGE HANDLER (UPDATED FOR WIZARD SUPPORT)
        // ───────────────────────────────────────────────────────────────
        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string raw = e.WebMessageAsJson;
            A49Logger.Log("📨 JS → Revit message received (RAW JSON): " + raw);

            try
            {
                if (string.IsNullOrWhiteSpace(raw)) return;

                // 🔹 1. Handle Django Callbacks (Direct Pass-Through)
                if (raw.Contains("list_views_result") ||
                    raw.Contains("list_sheets_result") ||
                    raw.Contains("list_views_on_sheet_result"))
                {
                    SendResponseDirect(raw);
                    return;
                }

                if (UIApplicationHolder.UIApp == null)
                {
                    SendResponseDirect("{\"status\":\"error\",\"message\":\"Revit not ready.\"}");
                    return;
                }

                // 🔹 2. Parse JSON
                JObject obj = JObject.Parse(raw);
                if (obj == null) return;

                string incomingSessionKey = obj["session_key"]?.ToString();

                // 🔹 3. SMART COMMAND EXTRACTION
                // Check if wrapped in "revit_command" OR if it's a top-level command
                JObject cmdObj = obj["revit_command"] as JObject;

                if (cmdObj == null)
                {
                    // Fallback: Check if the root object IS the command (e.g. Wizard Request)
                    if (obj["command"] != null)
                    {
                        cmdObj = obj;
                    }
                    else
                    {
                        A49Logger.Log("⚠️ No valid command structure found in JSON.");
                        return;
                    }
                }

                // Convert to envelope
                var envelope = cmdObj.ToObject<RevitCommandEnvelope>();
                if (envelope == null || envelope.command == null) return;

                // Attach session key if missing
                if (string.IsNullOrEmpty(envelope.session_key))
                    envelope.session_key = incomingSessionKey;

                A49Logger.Log($"📌 Processing Command: {envelope.command}");

                // 🔹 4. SPECIAL HANDLER: WIZARD DATA FETCH
                // We handle this directly here to return data immediately to Vue
                if (envelope.command == "fetch_project_info")
                {
                    CommandEventDispatcher.Initialize();

                    // Raise External Event with specific Action logic
                    // Note: If your CommandEventDispatcher only accepts Envelopes, 
                    // you must ensure your Executor handles "fetch_project_info".
                    // Assuming standard dispatch:
                    CommandEventDispatcher.Raise(envelope);
                    return;
                }

                // 🔹 5. STANDARD HANDLER
                CommandEventDispatcher.Initialize();
                CommandEventDispatcher.Raise(envelope);
            }
            catch (Exception ex)
            {
                A49Logger.Log("❌ FULL ERROR in OnWebMessageReceived: " + ex.ToString());
                SendResponseDirect("{\"status\":\"error\",\"message\":\"" + Escape(ex.Message) + "\"}");
            }
        }


        // ───────────────────────────────────────────────────────────────
        // ERROR PAGE
        // ───────────────────────────────────────────────────────────────
        private void LoadErrorPage(string msg)
        {
            A49Logger.Log("❌ LoadErrorPage(): " + msg);

            webViewerContainer.Children.Clear();
            webViewerContainer.Children.Add(new TextBlock
            {
                Text = msg,
                Foreground = System.Windows.Media.Brushes.White,
                Background = System.Windows.Media.Brushes.Red,
                Padding = new Thickness(20),
                TextWrapping = TextWrapping.Wrap
            });
        }

        // ───────────────────────────────────────────────────────────────
        // ESCAPE FOR JSON
        // ───────────────────────────────────────────────────────────────
        private string Escape(string input)
        {
            return input?
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t")
                ?? "";
        }

        // ───────────────────────────────────────────────────────────────
        // SEND MESSAGE BACK TO WEBVIEW
        // ───────────────────────────────────────────────────────────────
        private void SendResponseDirect(string json)
        {
            A49Logger.Log("📤 Sending JS response: " + json);

            try
            {
                if (webView?.CoreWebView2 != null)
                {
                    webView.CoreWebView2.PostWebMessageAsString(json);
                }
                else
                {
                    A49Logger.Log("⚠️ webView.CoreWebView2 is null — cannot send.");
                }
            }
            catch (Exception ex)
            {
                A49Logger.Log("❌ Failed to send JS response: " + ex.Message);
            }
        }

        // ───────────────────────────────────────────────────────────────
        // 💥 NEW HELPER: ALLOW EXTERNAL CLASSES TO RUN SCRIPTS
        // ───────────────────────────────────────────────────────────────
        //public void ExecuteScript(string script)
        //{
        //    if (webView != null && webView.CoreWebView2 != null)
        //    {
        //       webView.ExecuteScriptAsync(script);
        //   }
        //}

        // ───────────────────────────────────────────────────────────────
        // 💥 NEW: Send Raw JSON (For Wizard Data)
        // ───────────────────────────────────────────────────────────────
        public void SendRawMessage(string json)
        {
            // Re-use the existing private helper
            SendResponseDirect(json);
        }
    }
}