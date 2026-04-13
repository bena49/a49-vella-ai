<template>
  <div
    class="h-screen flex flex-col items-center 
           bg-gradient-to-br from-[#0A1D4A] via-[#0E3A7A] to-[#00C1D4] 
           text-white p-4 relative">

    <div v-if="!isAuthenticated" class="flex-1 flex flex-col items-center justify-center text-center px-4 w-full">
      <div class="mb-8">
        <h1 class="text-4xl font-semibold mb-2 tracking-wide">Vella</h1>
        <p class="opacity-80 text-xs max-w-xs mx-auto leading-relaxed">
          Hello I am your Revit AI Assistant
        </p>
      </div>

      <button
        @click="handleLogin"
        :disabled="isLoggingIn"
        class="bg-white/10 hover:bg-white/20 border border-white/30 transition-all 
               px-6 py-3 rounded-2xl flex items-center justify-center gap-3 backdrop-blur-md w-full max-w-[220px]"
      >
        <Icon v-if="!isLoggingIn" name="mdi:shield-alert-outline" class="text-xl" />
        <span v-else class="animate-spin block w-5 h-5 border-2 border-white/30 border-t-white rounded-full"></span>
        <span class="font-medium text-sm">
          {{ isLoggingIn ? 'Authenticating...' : 'Sign in with A49' }}
        </span>
      </button>

      <div class="mt-8 text-[10px] opacity-50 flex items-center gap-1">
        <Icon name="lucide:lock-keyhole" class="text-sm" />
        Secure Microsoft SSO Required
      </div>
    </div>

    <template v-else>
      <div
        ref="chatContainer"
        class="w-full flex-1 overflow-y-auto space-y-3 px-0 mb-4 no-scrollbar min-h-0">

        <div class="text-center pt-2 pb-4">
          <h1 class="mb-1 text-xl font-semibold">Hi, {{ userName.split(' ')[0] }}.</h1>
          <p class="opacity-60 text-xs">How can I help you today?</p>
        </div>

        <div
          v-for="(msg, i) in messages"
          :key="i"
          class="w-full flex items-end gap-2"
          :class="msg.from === 'user' ? 'justify-end' : 'justify-start'"
        >
          <Icon
            v-if="msg.from !== 'user'"
            name="ri:chat-smile-ai-line"
            class="text-2xl flex-shrink-0 transform scale-x-[-1] vella-color-shift"
          />

          <div
            class="px-3 py-1.5 rounded-2xl backdrop-blur-xl select-text
                   border text-xs leading-snug max-w-[85%] break-words 
                   whitespace-pre-wrap font-mono"
            :class="msg.from === 'user'
              ? 'border-white/20 bg-white/30'
              : 'border-white/20 bg-white/10'"
          >
            {{ msg.text }}
          </div>
        </div>

        <div v-if="isThinking" class="w-full flex items-end gap-2 justify-start">
          <Icon
            name="ri:chat-smile-ai-line"
            class="text-2xl flex-shrink-0 transform scale-x-[-1] vella-color-shift"
          />
          <div
            class="px-3 py-1.5 rounded-2xl backdrop-blur-xl 
                   bg-white/10 border border-white/20 
                   text-xs font-extrabold max-w-[40%] tracking-widest thinking-dots"
          >
            <span class="dot">.</span><span class="dot">.</span><span class="dot">.</span>
          </div>
        </div>
      </div>

      <ChatInput 
        @submit="handleUserSubmit"
        @clear="clearChatAndMemory"
        @action="handleAction"
      />
    </template>

    <div class="absolute bottom-0 w-full text-center text-[10px] text-white/60 mb-1 pointer-events-none">
      Version 1.0 Beta - Developed by IRIs 2026
    </div>

    <PlanViewWizard v-if="showWizard" v-bind="wizardProps" :key="wizardKey" @close="closeWizard" @submit="handleWizardSubmit" />
    <SheetWizard v-if="showSheetWizard" v-bind="sheetWizardProps" :key="wizardKey" @close="showSheetWizard = false" @submit="handleWizardSubmit" />
    <CreateAndPlaceWizard v-if="showCreatePlaceWizard" v-bind="createPlaceWizardProps" :key="'cp-'+wizardKey" @close="showCreatePlaceWizard = false" @submit="handleWizardSubmit" />

    <!--IMPORTANT - Uncoment this line below to see UI in  localhost:3000 screen -->
    <RenameWizard v-if="true" v-bind="renameWizardProps" :key="'rw-'+wizardKey" @close="showRenameWizard = false" @submit="handleBatchSubmit" /> 
    
    <RoomElevationWizard v-if="showRoomWizard" v-bind="roomWizardProps" :key="'rm-'+wizardKey" @close="showRoomWizard = false" @executeRaw="handleRoomElevationExecute" />
    <HelpModal v-if="showHelp" @close="showHelp = false" @submit="handleHelpPrompt" />

    <VellaModal
      :visible="modal.visible"
      :showFooter="true"
      @confirm="modalConfirm"
      @cancel="modalCancel"
      @close="modalClose"
    >
      <component :is="modal.component" v-bind="modal.props" />
    </VellaModal>

  </div>
</template>

<script setup>
import { ref, nextTick, onMounted } from "vue";
import { useRuntimeConfig } from "#app";
import { PublicClientApplication } from "@azure/msal-browser";
import ChatInput from "~/components/chat/ChatInput.vue";
import VellaModal from "~/components/VellaModal.vue";
import PlanViewWizard from "~/components/wizards/PlanViewWizard.vue"; 
import CreateAndPlaceWizard from '~/components/wizards/CreateAndPlaceWizard.vue';
import SheetWizard from '~/components/wizards/SheetWizard.vue';
import RenameWizard from '~/components/wizards/RenameWizard.vue'; 
import RoomElevationWizard from '~/components/wizards/RoomElevationWizard.vue';
import HelpModal from '~/components/help/HelpModal.vue';
import { useRevitBridge } from "~/composables/useRevitBridge";

// --- STATE ---
const messages = ref([]);
const isThinking = ref(false);
const chatContainer = ref(null);
const sessionKey = ref(null);
const API_URL = "https://a49iris.com/irisai-api/api/ai/";

// 💥 AUTHENTICATION STATE
const isAuthenticated = ref(false);
const isLoggingIn = ref(false);
const userName = ref("");
const accessToken = ref("");
let msalInstance = null;

// WIZARD STATES
const showWizard = ref(false);
const wizardProps = ref({}); 
const wizardKey = ref(0); 
const showSheetWizard = ref(false);
const sheetWizardProps = ref({}); 
const showCreatePlaceWizard = ref(false); 
const createPlaceWizardProps = ref({});
const showRenameWizard = ref(false);
const renameWizardProps = ref({});

const showRoomWizard = ref(false);
const roomWizardProps = ref({});

const showHelp = ref(false);

const { sendToRevit, listenToRevit } = useRevitBridge();

const modal = ref({ visible: false, component: null, props: {}, onConfirm: null, onCancel: null });

/* ---------------------------------------------------------------------------
   AUTHENTICATION LOGIC (MSAL)
--------------------------------------------------------------------------- */
const config = useRuntimeConfig();

const msalConfig = {
  auth: {
    clientId: config.public.azureClientId,
    authority: `https://login.microsoftonline.com/${config.public.azureTenantId}`,
    redirectUri: window.location.origin + config.app.baseURL
  },
  cache: {
    cacheLocation: "localStorage", 
    storeAuthStateInCookie: false,
  }
};

async function handleLogin() {
  try {
    isLoggingIn.value = true;
    // 💥 SWITCH TO REDIRECT: Much more reliable for Revit WebView2
    await msalInstance.loginRedirect({
      scopes: ["User.Read"]
    });
    // Note: The page will redirect away to Microsoft, so we don't need to turn off the spinner here.
  } catch (err) {
    console.error("Login failed:", err);
    isLoggingIn.value = false;
  }
}

async function getValidToken() {
  try {
    const account = msalInstance.getActiveAccount();
    if (!account) throw new Error("No active account");

    const response = await msalInstance.acquireTokenSilent({
      scopes: ["User.Read"],
      account: account
    });
    
    accessToken.value = response.idToken; 
    return response.idToken;
    
  } catch (err) {
    console.warn("Silent token acquisition failed.", err);
    isAuthenticated.value = false;
    return null;
  }
}

/* ---------------------------------------------------------------------------
   REVIT HANDLER
--------------------------------------------------------------------------- */
async function handleRevitMessage(raw) {
  let data = raw;
  if (typeof raw === 'string') {
    try { data = JSON.parse(raw); } 
    catch (e) { return; }
  }

  if (data.session_key) sessionKey.value = data.session_key;

  if (data.project_info) {
      wizardProps.value = { levels: data.project_info.levels || [], templates: data.project_info.templates || [], scopeBoxes: data.project_info.scope_boxes || [], initialStage: 'CD' };
      sheetWizardProps.value = { levels: data.project_info.levels || [], titleblocks: data.project_info.titleblocks || [], initialStage: 'CD' };
      createPlaceWizardProps.value = { levels: data.project_info.levels || [], templates: data.project_info.templates || [], scopeBoxes: data.project_info.scope_boxes || [], titleblocks: data.project_info.titleblocks || [], existingSheets: data.project_info.sheets || [], initialStage: 'CD' };
      roomWizardProps.value = { levels: data.project_info.levels || [], templates: data.project_info.templates || [], titleblocks: data.project_info.titleblocks || [], rooms: data.project_info.rooms || [], initialStage: 'CD' };
      wizardKey.value++; 
      return; 
  }

  if (data.project_inventory) {
      renameWizardProps.value = { inventoryData: data.project_inventory };
      showRenameWizard.value = true;
      wizardKey.value++;
      return;
  }

  if (data.status === "silent") return;
  if (data.result === "✔ Command executed successfully.") return;

  if (data.list_views_result || data.list_sheets_result || data.list_views_on_sheet_result || data.list_scope_boxes_result) {
    if (!data.session_key && sessionKey.value) data.session_key = sessionKey.value;
    await sendUserPrompt(data);
    return;
  }

  const text = data.error ? "⚠️ " + data.error : data.message || data.result;
  if (text && typeof text === 'string') {
    if (text.trim().startsWith('{')) return;
    messages.value.push({ from: "vella", text });
    scrollToBottom();
  }
}

/* ---------------------------------------------------------------------------
   CHAT LOGIC
--------------------------------------------------------------------------- */
function handleUserSubmit(text) {
  messages.value.push({ from: "user", text });
  scrollToBottom();
  sendToBackend({ message: text, session_key: sessionKey.value });
}

async function sendUserPrompt(revitData = null) {
  const payload = revitData || {}; 
  if (!payload.session_key) payload.session_key = sessionKey.value;
  sendToBackend(payload);
}

async function sendToBackend(payload) {
  isThinking.value = true;
  
  // 💥 GET FRESH TOKEN BEFORE EVERY REQUEST
  const token = await getValidToken();
  if (!token) {
      isThinking.value = false;
      return; // Stop if not authenticated
  }

  try {
    const res = await fetch(API_URL, {
      method: "POST",
      headers: { 
        "Content-Type": "application/json",
        "Authorization": `Bearer ${token}` // 💥 SEND THE SECURE TOKEN TO DJANGO
      },
      body: JSON.stringify(payload)
    });
    
    // Check if Django rejected the token (401 Unauthorized)
    if (res.status === 401) {
        isAuthenticated.value = false;
        throw new Error("Session expired or unauthorized.");
    }

    const data = await res.json();
    isThinking.value = false;

    if (data.session_key && data.session_key !== "unknown") sessionKey.value = data.session_key;

    if (data.error) messages.value.push({ from: "vella", text: "⚠️ " + data.error });
    else if (data.action) { handleAction(data.action); }
    else if (data.revit_command) {
      if (data.revit_command.command && data.revit_command.command.startsWith('wizard:')) {
        handleAction(data.revit_command.command);
      } else {
        if (!data.revit_command.session_key) data.revit_command.session_key = sessionKey.value;
        data.revit_command.token = accessToken.value;

        const interactiveStartMessage =
          data.revit_command.command === "start_interactive_room_package"
            ? "Interactive Mode Started! Please click on the target room in your active Revit floor plan..."
            : null;

        const messageToShow = data.message || interactiveStartMessage;

        if (messageToShow) {
          messages.value.push({ from: "vella", text: messageToShow });
        }

        nextTick(() => {
          scrollToBottom();
          setTimeout(() => {
            sendToRevit({ revit_command: data.revit_command });
          }, 600);
        });
      }
    }
    else if (data.message) messages.value.push({ from: "vella", text: data.message });
    
    scrollToBottom();

  } catch (err) {
    isThinking.value = false;
    messages.value.push({ from: "vella", text: "⚠️ Connection error or unauthorized." });
    scrollToBottom();
  }
}

/* ---------------------------------------------------------------------------
   ACTION HANDLERS & UTILITIES
--------------------------------------------------------------------------- */
function handleAction(action) {
  if (action === 'ui:help') showHelp.value = true;
  else if (action === 'wizard:create_views') {
    wizardProps.value = { levels: [], templates: [], scopeBoxes: [] };
    showWizard.value = true;
    sendToRevit({ command: "fetch_project_info", args: { types: ["levels", "templates", "scope_boxes"] } });
  } else if (action === 'wizard:create_sheets') {
      sheetWizardProps.value = { levels: [], titleblocks: [] };
      showSheetWizard.value = true;
      sendToRevit({ command: "fetch_project_info", args: { types: ["levels", "titleblocks"] } });
  } else if (action === 'wizard:create_and_place') {
      createPlaceWizardProps.value = { levels: [], templates: [], scopeBoxes: [], titleblocks: [], existingSheets: [] };
      showCreatePlaceWizard.value = true;
      sendToRevit({ command: "fetch_project_info", args: { types: ["levels", "templates", "scope_boxes", "titleblocks", "sheets"] } });
  } else if (action === 'wizard:rename_wizard') {
      renameWizardProps.value = { inventoryData: { sheets: [], views: [] } };
      sendToRevit({ command: "fetch_project_inventory" });
  }
  else if (action === 'wizard:room_elevations') {
    roomWizardProps.value = { levels: [], templates: [], titleblocks: [], rooms: [] };
    showRoomWizard.value = true;
    // Ask Revit to fetch the data, including our new "rooms" collector!
    sendToRevit({ command: "fetch_project_info", args: { types: ["levels", "templates", "titleblocks", "rooms"] } });
  }
}

function handleHelpPrompt(promptText) { showHelp.value = false; handleUserSubmit(promptText); }
function handleWizardSubmit(result) { showWizard.value = false; showSheetWizard.value = false; showCreatePlaceWizard.value = false; handleUserSubmit(result); }
function handleBatchSubmit(updates) {
    showRenameWizard.value = false;
    sendToRevit({ revit_command: { command: "execute_batch_update", raw: { updates: updates } } });
    messages.value.push({ from: "vella", text: `🔄 Applying ${updates.length} updates...` });
}

// 💥 DIRECT TO REVIT HANDLER
function handleRoomElevationExecute(payload) {
    showRoomWizard.value = false;
    
    // 1. Put the interactive instruction in the chat UI FIRST
    messages.value.push({ 
        from: "vella", 
        text: `Interactive Mode Started! Please click on the target room in your active Revit floor plan... (Stage: ${payload.stage || 'CD'})` 
    });

    // 2. Wait for DOM, then give WebView2 600ms to paint before freezing Revit!
    nextTick(() => {
        scrollToBottom();
        setTimeout(() => {
            sendToRevit({ 
                revit_command: { 
                    command: payload.command, 
                    raw: payload 
                } 
            });
        }, 600);
    });
}

async function scrollToBottom() {
  await nextTick();
  if (chatContainer.value) chatContainer.value.scrollTop = chatContainer.value.scrollHeight;
}
function clearChatAndMemory() { messages.value = []; messages.value.push({ from: "vella", text: "Chat cleared." }); }
function modalConfirm() { if (modal.value.onConfirm) modal.value.onConfirm(); modalClose(); }
function modalCancel() { if (modal.value.onCancel) modal.value.onCancel(); modalClose(); }
function closeModal() { modal.value.visible = false; modal.value.component = null; }
function closeWizard() { showWizard.value = false; }
function modalClose() { closeModal(); }

// 💥 INITIALIZE MSAL ON MOUNT
onMounted(async () => {
  try {
      msalInstance = new PublicClientApplication(msalConfig);
      await msalInstance.initialize();

      // 💥 NEW: CATCH THE REDIRECT FROM MICROSOFT
      const redirectResponse = await msalInstance.handleRedirectPromise();
      
      if (redirectResponse) {
          // If we just bounced back from Microsoft, log them in!
          msalInstance.setActiveAccount(redirectResponse.account);
          isAuthenticated.value = true;
          userName.value = redirectResponse.account.name;
          accessToken.value = redirectResponse.idToken;
      } else {
          // Otherwise, check if they are already logged in from a previous session
          const activeAccount = msalInstance.getAllAccounts()[0];
          if (activeAccount) {
              msalInstance.setActiveAccount(activeAccount);
              isAuthenticated.value = true;
              userName.value = activeAccount.name;
              await getValidToken();
          }
      }
  } catch (error) {
      console.error("MSAL Initialization or Redirect Error:", error);
  }

  listenToRevit(handleRevitMessage);
});
</script>

<style scoped>
/* 💥 Custom Keyframe for Vella's AI Aura */
@keyframes vella-shift {
  0% { color: #4EE29B; }   /* Green */
  33% { color: #00BCD4; }  /* Cyan */
  66% { color: #60A5FA; }  /* Blue */
  100% { color: #4EE29B; } /* Back to Green */
}

.vella-color-shift {
  animation: vella-shift 5s infinite alternate ease-in-out;
}

/* 💥 Vella's Thinking Dots - Bounce & Color Wave */
@keyframes dot-bounce {
  0%, 80%, 100% { transform: translateY(0); opacity: 0.4; }
  40% { transform: translateY(-3px); opacity: 1; }
}

.thinking-dots .dot {
  display: inline-block;
  /* Apply BOTH the bounce and the color shift animations */
  animation: dot-bounce 1.4s infinite ease-in-out both, 
             vella-shift 5s infinite alternate ease-in-out;
}

/* Stagger the animations so the bounce and color flow left-to-right */
.thinking-dots .dot:nth-child(1) {
  animation-delay: -0.32s, 0s;
}
.thinking-dots .dot:nth-child(2) {
  animation-delay: -0.16s, 0.3s;
}
.thinking-dots .dot:nth-child(3) {
  animation-delay: 0s, 0.6s;
}
</style>