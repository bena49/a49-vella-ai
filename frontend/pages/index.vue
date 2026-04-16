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
    
    <!--LIVE - <RenameWizard> Line 1 ON + Line 2 OFF -->
    <RenameWizard v-if="showRenameWizard" v-bind="renameWizardProps" :key="'rw-'+wizardKey" @close="showRenameWizard = false" @submit="handleBatchSubmit" />
    <!--localhost:3000 - <RenameWizard> Line 1 OFF + Line 2 ON -->
    <!--<RenameWizard v-if="true" v-bind="renameWizardProps" :key="'rw-'+wizardKey" @close="showRenameWizard = false" @submit="handleBatchSubmit" /> -->
    
    <RoomElevationWizard v-if="showRoomWizard" v-bind="roomWizardProps" :key="'rm-'+wizardKey" @close="showRoomWizard = false" @executeRaw="handleRoomElevationExecute" />
    <AutomateTagWizard v-if="showAutomateTagWizard" v-bind="automateTagWizardProps" :key="'amt-'+wizardKey" @close="showAutomateTagWizard = false" @submit="handleAutomateTagSubmit" />
    <AutoTagWizard v-if="showAutoTagWizard" v-bind="autoTagWizardProps" :key="'at-'+wizardKey" @close="showAutoTagWizard = false" @submit="handleAutoTagSubmit" />
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
import { ref, onMounted } from "vue";
import ChatInput from "~/components/chat/ChatInput.vue";
import VellaModal from "~/components/VellaModal.vue";
import PlanViewWizard from "~/components/wizards/PlanViewWizard.vue"; 
import CreateAndPlaceWizard from '~/components/wizards/CreateAndPlaceWizard.vue';
import SheetWizard from '~/components/wizards/SheetWizard.vue';
import RenameWizard from '~/components/wizards/RenameWizard.vue'; 
import RoomElevationWizard from '~/components/wizards/RoomElevationWizard.vue';
import AutoTagWizard from '~/components/wizards/AutoTagWizard.vue';
import AutomateTagWizard from '~/components/wizards/AutomateTagWizard.vue';
import HelpModal from '~/components/help/HelpModal.vue';
import { useRevitBridge } from "~/composables/useRevitBridge";

// --- COMPOSABLES ---
import { useAuth } from "~/composables/useAuth";
import { useChat } from "~/composables/useChat";
import { useWizards } from "~/composables/useWizards";
import { useRevitHandler } from "~/composables/useRevitHandler";

// --- SESSION ---
const sessionKey = ref(null);

// --- BRIDGE ---
const { sendToRevit, listenToRevit } = useRevitBridge();

// --- AUTH ---
const {
  isAuthenticated, isLoggingIn, userName, accessToken,
  handleLogin, getValidToken, initMsal
} = useAuth();

// --- WIZARDS (needs to be created before useChat so handleAction exists) ---
// We forward-declare handleAction via a wrapper since useChat needs it
let _handleAction = () => {};
const handleActionProxy = (action) => _handleAction(action);

// --- CHAT ---
const {
  messages, isThinking, chatContainer,
  scrollToBottom, clearChatAndMemory,
  handleUserSubmit, sendUserPrompt, sendToBackend
} = useChat(getValidToken, isAuthenticated, accessToken, sessionKey, sendToRevit, handleActionProxy);

// --- WIZARDS (actual init) ---
const {
  showWizard, showSheetWizard, showCreatePlaceWizard,
  showRenameWizard, showRoomWizard, showAutoTagWizard, showAutomateTagWizard, showHelp,
  wizardProps, sheetWizardProps, createPlaceWizardProps,
  renameWizardProps, roomWizardProps, autoTagWizardProps, automateTagWizardProps, wizardKey,
  handleAction, handleHelpPrompt, handleWizardSubmit,
  handleBatchSubmit, handleRoomElevationExecute, handleAutoTagSubmit, handleAutomateTagSubmit,
  closeWizard, updateWizardProps, updateInventoryProps
} = useWizards(messages, scrollToBottom, handleUserSubmit, sendToBackend, sendToRevit, sessionKey);

// Wire up the proxy now that handleAction exists
_handleAction = handleAction;

// --- REVIT HANDLER ---
const { handleRevitMessage } = useRevitHandler(
  messages, sessionKey, scrollToBottom, sendUserPrompt,
  updateWizardProps, updateInventoryProps
);

// --- MODAL (tiny, stays inline) ---
const modal = ref({ visible: false, component: null, props: {}, onConfirm: null, onCancel: null });
function modalConfirm() { if (modal.value.onConfirm) modal.value.onConfirm(); modalClose(); }
function modalCancel() { if (modal.value.onCancel) modal.value.onCancel(); modalClose(); }
function modalClose() { modal.value.visible = false; modal.value.component = null; }

// --- LIFECYCLE ---
onMounted(async () => {
  await initMsal();
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
  animation: dot-bounce 1.4s infinite ease-in-out both, 
             vella-shift 5s infinite alternate ease-in-out;
}

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
