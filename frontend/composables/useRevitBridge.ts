// composables/useRevitBridge.ts
export const useRevitBridge = () => {
  // Check if we are in Revit/WebView2
  // We use optional chaining (?.) to safely check if chrome exists
  const isWebView = () => typeof window !== 'undefined' && !!window.chrome?.webview;

  // Send Data to Revit
  const sendToRevit = (payload: any) => {
    try {
      // Clone to remove Vue reactivity proxies
      const envelope = JSON.parse(JSON.stringify(payload));
      
      // Safe check: window.chrome?.webview?.postMessage
      if (window.chrome?.webview?.postMessage) {
        window.chrome.webview.postMessage(envelope);
      } 
      // Safe check: window.external
      else if (window.external && (window.external as any).postMessage) {
        (window.external as any).postMessage(envelope);
      } 
      else {
        console.warn("[DEV] Mock Send to Revit:", envelope);
      }
    } catch (err) {
      console.error("❌ JS → Revit Send Error:", err);
    }
  };

  // Setup Listener (Call this in onMounted)
  const listenToRevit = (callback: (data: any) => void) => {
    const handler = (event: any) => {
      // Parse data whether it's from WebView (event.data) or Dev (event.data)
      const data = (typeof event.data === 'string') ? JSON.parse(event.data) : event.data;
      callback(data);
    };

    if (isWebView()) {
      // We must use strict checking here to satisfy TypeScript
      // "!" tells TS "I know this exists because isWebView() returned true"
      window.chrome!.webview.addEventListener("message", handler);
    } else {
      window.addEventListener("message", handler);
    }
  };

  return { sendToRevit, listenToRevit };
};