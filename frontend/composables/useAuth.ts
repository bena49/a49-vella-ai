// composables/useAuth.ts
// Extracted from index.vue — MSAL authentication logic

import { ref } from "vue";
import { useRuntimeConfig } from "#app";
import { PublicClientApplication } from "@azure/msal-browser";

export function useAuth() {
  // --- STATE ---
  const isAuthenticated = ref(false);
  const isLoggingIn = ref(false);
  const userName = ref("");
  const accessToken = ref("");
  let msalInstance: PublicClientApplication | null = null;

  // --- MSAL CONFIG ---
  const config = useRuntimeConfig();

  const msalConfig = {
    auth: {
      clientId: config.public.azureClientId as string,
      authority: `https://login.microsoftonline.com/${config.public.azureTenantId}`,
      redirectUri: window.location.origin + config.app.baseURL,
    },
    cache: {
      cacheLocation: "localStorage" as const,
      storeAuthStateInCookie: false,
    },
  };

  // --- LOGIN ---
  async function handleLogin() {
    try {
      isLoggingIn.value = true;
      // 💥 SWITCH TO REDIRECT: Much more reliable for Revit WebView2
      await msalInstance!.loginRedirect({
        scopes: ["User.Read"],
      });
      // Note: The page will redirect away to Microsoft, so we don't need to turn off the spinner here.
    } catch (err) {
      console.error("Login failed:", err);
      isLoggingIn.value = false;
    }
  }

  // --- TOKEN REFRESH ---
  async function getValidToken(): Promise<string | null> {
    try {
      const account = msalInstance!.getActiveAccount();
      if (!account) throw new Error("No active account");

      const response = await msalInstance!.acquireTokenSilent({
        scopes: ["User.Read"],
        account: account,
      });

      accessToken.value = response.idToken;
      return response.idToken;
    } catch (err) {
      console.warn("Silent token acquisition failed.", err);
      isAuthenticated.value = false;
      return null;
    }
  }

  // --- INITIALIZE MSAL ---
  async function initMsal() {
    try {
      msalInstance = new PublicClientApplication(msalConfig);
      await msalInstance.initialize();

      // 💥 CATCH THE REDIRECT FROM MICROSOFT
      const redirectResponse = await msalInstance.handleRedirectPromise();

      if (redirectResponse) {
        // If we just bounced back from Microsoft, log them in!
        msalInstance.setActiveAccount(redirectResponse.account);
        isAuthenticated.value = true;
        userName.value = redirectResponse.account.name || "";
        accessToken.value = redirectResponse.idToken;
      } else {
        // Otherwise, check if they are already logged in from a previous session
        const activeAccount = msalInstance.getAllAccounts()[0];
        if (activeAccount) {
          msalInstance.setActiveAccount(activeAccount);
          isAuthenticated.value = true;
          userName.value = activeAccount.name || "";
          await getValidToken();
        }
      }
    } catch (error) {
      console.error("MSAL Initialization or Redirect Error:", error);
    }
  }

  return {
    isAuthenticated,
    isLoggingIn,
    userName,
    accessToken,
    handleLogin,
    getValidToken,
    initMsal,
  };
}
