export {};

declare global {
  interface Window {
    // Add support for WebView2
    chrome?: {
      webview: {
        postMessage: (message: any) => void;
        addEventListener: (type: string, listener: (event: any) => void) => void;
        removeEventListener: (type: string, listener: (event: any) => void) => void;
        hostObjects?: any;
      };
    };
    // Add support for legacy/other containers
    external?: {
      postMessage: (message: any) => void;
    };
  }
}